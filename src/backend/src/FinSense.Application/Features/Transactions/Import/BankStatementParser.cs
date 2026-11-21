using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FinSense.Domain.Exceptions;

namespace FinSense.Application.Features.Transactions.Import;

/// <summary>
/// 100% local, zero-external-dependency parser for Turkish &amp; international bank statements
/// in CSV (.csv), Excel (.xlsx), and text-based PDF (.pdf) formats.
/// </summary>
public static class BankStatementParser
{
    private const string PdfNoTextMessage =
        "Bu PDF metin içermiyor. Lütfen dijital banka ekstresi (PDF/Excel/CSV) yükleyin.";

    private const string PdfCustomFontEncodingMessage =
        "Bu PDF özel font kodlaması içerdiği için okunamadı. Lütfen bankanızdan Excel (.xlsx) veya CSV formatında ekstre indirin.";

    private static readonly string[] DateFormats =
    [
        "dd.MM.yyyy",
        "d.M.yyyy",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd.MM.yy",
        "dd/MM/yy"
    ];

    private static readonly Regex PdfRowRegex = new(
        @"(?<date>\b(?:\d{2}[./-]\d{2}[./-]\d{2,4}|\d{4}-\d{2}-\d{2})\b)\s+(?<rest>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex PdfAmountTokenRegex = new(
        @"(?<amount>(?:[+-]\s*)?(?:₺|TL|TRY|\$|€|£)?\s*(?:[+-]\s*)?\(?(?:\d{1,3}(?:[.,]\d{3})+|\d+)(?:[.,]\d{1,2})\)?-?)(?:\s*(?:TL|TRY|₺|USD|EUR|GBP))?(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] IgnoredPdfSummaryMarkers =
    [
        "DEVREDEN BAKIYE",
        "DONEM SONU BAKIYE",
        "GENEL TOPLAM",
        "TOPLAM HARCAMA",
        "TOPLAM BORC",
        "TOPLAM ALACAK",
        "TARIH ACIKLAMA",
        "ISLEM TARIHI ACIKLAMA"
    ];

    public static ParsedStatementResult Parse(
        string fileName,
        byte[] content,
        string? contentType = null,
        Action<IReadOnlyList<string>>? onPdfLinesExtracted = null)
    {
        if (content is null || content.Length == 0)
        {
            throw new DomainException("EMPTY_STATEMENT_FILE", "Yüklenen ekstre dosyası boş.");
        }

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var format = DetectFormat(ext, content, contentType);

        return format switch
        {
            "PDF" => new ParsedStatementResult("PDF", ParsePdf(content, onPdfLinesExtracted)),
            "XLSX" => new ParsedStatementResult("XLSX", ParseXlsx(content)),
            "CSV" => new ParsedStatementResult("CSV", ParseCsv(content)),
            _ => throw new DomainException(
                "UNSUPPORTED_STATEMENT_FORMAT",
                "Desteklenmeyen dosya formatı. Lütfen PDF, Excel (.xlsx) veya CSV dosyası yükleyin.")
        };
    }

    private static string DetectFormat(string extension, byte[] content, string? contentType)
    {
        // Always inspect magic bytes first (Android document pickers may omit .pdf extension or MIME type)
        if (HasPdfMagicBytes(content) ||
            extension == ".pdf" ||
            (!string.IsNullOrWhiteSpace(contentType) &&
             contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)))
        {
            return "PDF";
        }

        if ((content.Length >= 4 && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04) ||
            extension == ".xlsx" ||
            (!string.IsNullOrWhiteSpace(contentType) &&
             contentType.Contains("spreadsheetml", StringComparison.OrdinalIgnoreCase)))
        {
            return "XLSX";
        }

        if (extension is ".csv" or ".txt" or "" ||
            (!string.IsNullOrWhiteSpace(contentType) &&
             contentType.Contains("csv", StringComparison.OrdinalIgnoreCase)))
        {
            return "CSV";
        }

        return string.Empty;
    }

    public static bool HasPdfMagicBytes(byte[] content)
    {
        var scanLimit = Math.Min(content.Length, 1024);
        for (var i = 0; i + 3 < scanLimit; i++)
        {
            if (content[i] == 0x25 && content[i + 1] == 0x50 && content[i + 2] == 0x44 && content[i + 3] == 0x46)
            {
                return true;
            }
        }

        return false;
    }

    // ───────────────────────────── CSV ─────────────────────────────

    public static IReadOnlyList<ParsedStatementRow> ParseCsv(byte[] content)
    {
        var text = DecodeTextWithTurkishFallback(content);
        return ParseCsvText(text);
    }

    public static IReadOnlyList<ParsedStatementRow> ParseCsvText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = text.TrimStart('\uFEFF');
        var delimiter = DetectCsvDelimiter(normalized);
        var table = ParseCsvTable(normalized, delimiter);
        return ParseTabularRows(table);
    }

    private static char DetectCsvDelimiter(string text)
    {
        var sampleLines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Take(10)
            .ToList();

        int semiCount = 0, commaCount = 0, tabCount = 0;
        foreach (var line in sampleLines)
        {
            var inQuotes = false;
            foreach (var ch in line)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (inQuotes) continue;

                if (ch == ';') semiCount++;
                else if (ch == ',') commaCount++;
                else if (ch == '\t') tabCount++;
            }
        }

        if (semiCount >= commaCount && semiCount >= tabCount && semiCount > 0) return ';';
        if (tabCount > commaCount && tabCount > 0) return '\t';
        return ',';
    }

    private static List<List<string>> ParseCsvTable(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
            }
            else
            {
                if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == delimiter)
                {
                    currentRow.Add(field.ToString().Trim());
                    field.Clear();
                }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    currentRow.Add(field.ToString().Trim());
                    field.Clear();

                    if (currentRow.Any(c => !string.IsNullOrWhiteSpace(c)))
                    {
                        rows.Add(currentRow);
                    }

                    currentRow = new List<string>();
                }
                else
                {
                    field.Append(ch);
                }
            }
        }

        if (field.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString().Trim());
            if (currentRow.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                rows.Add(currentRow);
            }
        }

        return rows;
    }

    // ───────────────────────────── XLSX ─────────────────────────────

    public static IReadOnlyList<ParsedStatementRow> ParseXlsx(byte[] content)
    {
        try
        {
            using var ms = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            var sharedStrings = ReadXlsxSharedStrings(archive);
            var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
                ?? archive.Entries.FirstOrDefault(e =>
                    e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

            if (sheetEntry is null)
            {
                throw new DomainException("INVALID_XLSX_FILE", "Excel dosyasında okunabilir bir sayfa bulunamadı.");
            }

            using var sheetStream = sheetEntry.Open();
            var doc = XDocument.Load(sheetStream);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            var table = new List<List<string>>();
            foreach (var rowEl in doc.Descendants(ns + "row"))
            {
                var cells = new List<string>();
                foreach (var cEl in rowEl.Elements(ns + "c"))
                {
                    var cellRef = cEl.Attribute("r")?.Value;
                    var colIdx = cellRef is not null ? GetExcelColumnIndex(cellRef) : cells.Count;
                    while (cells.Count < colIdx)
                    {
                        cells.Add(string.Empty);
                    }

                    var typeAttr = cEl.Attribute("t")?.Value;
                    string value;
                    if (typeAttr == "s")
                    {
                        var idxStr = cEl.Element(ns + "v")?.Value;
                        value = int.TryParse(idxStr, out var ssIdx) && ssIdx >= 0 && ssIdx < sharedStrings.Count
                            ? sharedStrings[ssIdx]
                            : string.Empty;
                    }
                    else if (typeAttr == "inlineStr")
                    {
                        value = string.Concat(cEl.Descendants(ns + "t").Select(t => t.Value));
                    }
                    else
                    {
                        value = cEl.Element(ns + "v")?.Value ?? string.Empty;
                    }

                    cells.Add(value.Trim());
                }

                if (cells.Any(c => !string.IsNullOrWhiteSpace(c)))
                {
                    table.Add(cells);
                }
            }

            return ParseTabularRows(table);
        }
        catch (DomainException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw new DomainException("INVALID_XLSX_FILE", "Geçersiz veya bozuk Excel (.xlsx) dosyası.");
        }
    }

    private static List<string> ReadXlsxSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var result = new List<string>();
        foreach (var si in doc.Descendants(ns + "si"))
        {
            var text = string.Concat(si.Descendants(ns + "t").Select(t => t.Value));
            result.Add(text);
        }

        return result;
    }

    private static int GetExcelColumnIndex(string cellReference)
    {
        var col = 0;
        foreach (var ch in cellReference)
        {
            if (char.IsLetter(ch))
            {
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            else
            {
                break;
            }
        }

        return Math.Max(0, col - 1);
    }

    // ───────────────────────────── Shared Tabular Mapper (CSV & XLSX) ─────────────────────────────

    private static IReadOnlyList<ParsedStatementRow> ParseTabularRows(List<List<string>> table)
    {
        if (table.Count == 0)
        {
            return [];
        }

        var headerRowIdx = -1;
        var dateCol = -1;
        var descCol = -1;
        var amountCol = -1;
        var debitCol = -1;
        var creditCol = -1;

        for (var r = 0; r < Math.Min(table.Count, 15); r++)
        {
            var row = table[r];
            int dCol = -1, desc = -1, amt = -1, deb = -1, cred = -1;

            for (var c = 0; c < row.Count; c++)
            {
                var h = StatementAutoCategorizer.NormalizeForMatch(row[c]);
                if (string.IsNullOrEmpty(h)) continue;

                if (dCol == -1 && (h.Contains("TARIH") || h.Contains("DATE") || h.Contains("VALOR")))
                {
                    dCol = c;
                }
                else if (desc == -1 && (h.Contains("ACIKLAMA") || h.Contains("DESCRIPTION") || h.Contains("ISLEM") && !h.Contains("TUTAR") && !h.Contains("TARIH") || h.Contains("MERCHANT") || h.Contains("DETAY")))
                {
                    desc = c;
                }
                else if (deb == -1 && (h == "BORC" || h.Contains("BORC") || h == "CIKIS" || h == "GIDER" || h.Contains("DEBIT") || h.Contains("WITHDRAWAL")))
                {
                    deb = c;
                }
                else if (cred == -1 && (h == "ALACAK" || h.Contains("ALACAK") || h == "GIRIS" || h == "GELIR" || h.Contains("CREDIT") || h.Contains("DEPOSIT")))
                {
                    cred = c;
                }
                else if (amt == -1 && (h.Contains("TUTAR") || h.Contains("AMOUNT") || h.Contains("MIKTAR")) && !h.Contains("BAKIYE") && !h.Contains("BALANCE"))
                {
                    amt = c;
                }
            }

            if (dCol != -1 && (amt != -1 || deb != -1 || cred != -1))
            {
                headerRowIdx = r;
                dateCol = dCol;
                descCol = desc;
                amountCol = amt;
                debitCol = deb;
                creditCol = cred;
                break;
            }
        }

        var startRow = headerRowIdx >= 0 ? headerRowIdx + 1 : 0;

        // Fallback when no header row is present: infer columns from the first parsable data row
        if (headerRowIdx == -1)
        {
            foreach (var row in table.Take(10))
            {
                for (var c = 0; c < row.Count; c++)
                {
                    if (dateCol == -1 && TryParseDate(row[c], out _))
                    {
                        dateCol = c;
                    }
                    else if (amountCol == -1 && TryParseAmount(row[c], out _))
                    {
                        amountCol = c;
                    }
                    else if (descCol == -1 && !string.IsNullOrWhiteSpace(row[c]) && !TryParseDate(row[c], out _) && !TryParseAmount(row[c], out _))
                    {
                        descCol = c;
                    }
                }

                if (dateCol != -1 && amountCol != -1)
                {
                    break;
                }
            }
        }

        if (dateCol == -1 || (amountCol == -1 && debitCol == -1 && creditCol == -1))
        {
            return [];
        }

        var parsed = new List<ParsedStatementRow>();
        for (var r = startRow; r < table.Count; r++)
        {
            var row = table[r];
            if (dateCol >= row.Count || !TryParseDate(row[dateCol], out var date))
            {
                continue;
            }

            decimal? signedAmount = null;

            if (amountCol >= 0 && amountCol < row.Count && TryParseAmount(row[amountCol], out var directAmt))
            {
                signedAmount = directAmt;
            }
            else if (debitCol >= 0 || creditCol >= 0)
            {
                decimal debitVal = 0m;
                decimal creditVal = 0m;
                var hasDebit = debitCol >= 0 && debitCol < row.Count && TryParseAmount(row[debitCol], out debitVal) && debitVal != 0m;
                var hasCredit = creditCol >= 0 && creditCol < row.Count && TryParseAmount(row[creditCol], out creditVal) && creditVal != 0m;

                if (hasDebit)
                {
                    signedAmount = -Math.Abs(debitVal);
                }
                else if (hasCredit)
                {
                    signedAmount = Math.Abs(creditVal);
                }
            }

            if (!signedAmount.HasValue || signedAmount.Value == 0m)
            {
                continue;
            }

            var description = descCol >= 0 && descCol < row.Count
                ? row[descCol].Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(description))
            {
                description = signedAmount.Value > 0 ? "Gelir İşlemi" : "Harcama İşlemi";
            }

            parsed.Add(new ParsedStatementRow(
                date,
                description.Length > 200 ? description[..200] : description,
                decimal.Round(signedAmount.Value, 2, MidpointRounding.AwayFromZero)));
        }

        return parsed;
    }

    // ───────────────────────────── PDF ─────────────────────────────

    public static IReadOnlyList<ParsedStatementRow> ParsePdf(
        byte[] content,
        Action<IReadOnlyList<string>>? onPdfLinesExtracted = null)
    {
        var (coordinateLines, sequentialLines) = ExtractPdfTextLines(content);
        if (coordinateLines.Count == 0 && sequentialLines.Count == 0)
        {
            throw new DomainException("PDF_NO_TEXT", PdfNoTextMessage);
        }

        onPdfLinesExtracted?.Invoke(coordinateLines.Count > 0 ? coordinateLines : sequentialLines);

        var rows = ParsePdfLines(coordinateLines);
        if (rows.Count == 0 && sequentialLines.Count > 0)
        {
            rows = ParsePdfLines(sequentialLines);
        }

        // Global full-text regex fallback: if line-based parsing yields 0 rows (e.g. collapsed single-line PDF text),
        // split the full page text on date boundaries and parse each (Date) (Description) (Amount) segment.
        if (rows.Count == 0)
        {
            var fullText = NormalizePdfLine(string.Join(" ", coordinateLines.Count > 0 ? coordinateLines : sequentialLines));
            rows = ParsePdfFullTextFallback(fullText);

            if (rows.Count == 0 && IsLikelyCustomEncodedPdfText(fullText))
            {
                throw new DomainException("PDF_CUSTOM_FONT_ENCODING", PdfCustomFontEncodingMessage);
            }
        }

        return rows;
    }

    public static bool IsLikelyCustomEncodedPdfText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int totalNonWs = 0;
        int garbledCount = 0;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            totalNonWs++;
            var isStandard =
                (ch is >= 'a' and <= 'z') ||
                (ch is >= 'A' and <= 'Z') ||
                (ch is >= '0' and <= '9') ||
                "çğıöşüÇĞİÖŞÜ.,:/-+()₺$€£%?!&*'\"_#;[]@".Contains(ch);

            if (!isStandard)
            {
                garbledCount++;
            }
        }

        return totalNonWs >= 15 && (double)garbledCount / totalNonWs >= 0.25;
    }

    public static IReadOnlyList<ParsedStatementRow> ParsePdfFullTextFallback(string fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return [];
        }

        var normalized = NormalizePdfLine(fullText);
        var dateMatches = Regex.Matches(normalized, @"\b(?:\d{2}[./-]\d{2}[./-]\d{2,4}|\d{4}-\d{2}-\d{2})\b");
        if (dateMatches.Count == 0)
        {
            return [];
        }

        var results = new List<ParsedStatementRow>();
        for (var i = 0; i < dateMatches.Count; i++)
        {
            var start = dateMatches[i].Index;
            var end = i + 1 < dateMatches.Count ? dateMatches[i + 1].Index : normalized.Length;
            var segment = normalized[start..end].Trim();

            var upper = StatementAutoCategorizer.NormalizeForMatch(segment);
            if (IsIgnoredPdfHeaderOrSummary(upper))
            {
                continue;
            }

            if (TryParseSinglePdfLine(segment, out var row))
            {
                results.Add(row);
            }
        }

        return results;
    }

    public static string NormalizePdfLine(string? rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return string.Empty;
        }

        // Normalize Unicode minus/en-dash/em-dash to ASCII '-', non-breaking spaces to space
        var normalized = rawLine
            .Replace('\u2212', '-') // Unicode minus −
            .Replace('\u2013', '-') // En dash –
            .Replace('\u2014', '-') // Em dash —
            .Replace('\u00A0', ' ');

        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        // Collapse spaces inside dates (e.g. "01 .11 .2026" -> "01.11.2026", "2026 - 11 - 01" -> "2026-11-01")
        normalized = Regex.Replace(normalized, @"(\d{1,2})\s*([./-])\s*(\d{1,2})\s*([./-])\s*(\d{2,4})", "$1$2$3$4$5");
        normalized = Regex.Replace(normalized, @"(\d{4})\s*([./-])\s*(\d{1,2})\s*([./-])\s*(\d{1,2})", "$1$2$3$4$5");

        // Collapse spaces between sign and digit/currency (e.g. "- 3.420,50" -> "-3.420,50", "+ 65.000,00" -> "+65.000,00")
        normalized = Regex.Replace(normalized, @"(?<=^|\s)([+-])\s+(?=(?:₺|TL|TRY|\$|€|£)?\s*\d)", "$1");
        normalized = Regex.Replace(normalized, @"([+-])\s+(\d)", "$1$2");

        // Collapse spaces around decimal/thousands separators inside numbers (run twice for chained separators)
        normalized = Regex.Replace(normalized, @"(\d)\s*([.,])\s*(\d)", "$1$2$3");
        normalized = Regex.Replace(normalized, @"(\d)\s*([.,])\s*(\d)", "$1$2$3");

        return normalized;
    }

    public static IReadOnlyList<ParsedStatementRow> ParsePdfLines(IEnumerable<string> lines)
    {
        var normalizedLines = lines
            .Select(NormalizePdfLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var results = new List<ParsedStatementRow>();

        for (var idx = 0; idx < normalizedLines.Count; idx++)
        {
            var line = normalizedLines[idx];
            var normalizedUpper = StatementAutoCategorizer.NormalizeForMatch(line);
            if (IsIgnoredPdfHeaderOrSummary(normalizedUpper))
            {
                continue;
            }

            // Try matching current line directly first
            if (TryParseSinglePdfLine(line, out var parsedRow))
            {
                results.Add(parsedRow);
                continue;
            }

            // Support multi-line wrapped rows where the date is on this line and description/amount
            // continues on the next 1-2 lines (e.g. wrapped Turkish bank statements)
            var dateOnlyMatch = Regex.Match(line, @"^(?:\d{2}[./-]\d{2}[./-]\d{2,4}|\d{4}-\d{2}-\d{2})(?:\s|$)");
            if (!dateOnlyMatch.Success)
            {
                continue;
            }

            var combined = line;
            for (var lookAhead = 1; lookAhead <= 2 && idx + lookAhead < normalizedLines.Count; lookAhead++)
            {
                var nextLine = normalizedLines[idx + lookAhead];
                if (IsIgnoredPdfHeaderOrSummary(StatementAutoCategorizer.NormalizeForMatch(nextLine)))
                {
                    break;
                }

                // Stop merging if the continuation line starts a brand-new dated transaction row
                if (Regex.IsMatch(nextLine, @"^(?:\d{2}[./-]\d{2}[./-]\d{2,4}|\d{4}-\d{2}-\d{2})(?:\s|$)"))
                {
                    break;
                }

                combined = $"{combined} {nextLine}";
                if (TryParseSinglePdfLine(combined, out var wrappedRow))
                {
                    results.Add(wrappedRow);
                    idx += lookAhead;
                    break;
                }
            }
        }

        return results;
    }

    private static bool TryParseSinglePdfLine(string line, out ParsedStatementRow row)
    {
        row = default!;
        if (line.Length < 10)
        {
            return false;
        }

        var rowMatch = PdfRowRegex.Match(line);
        if (!rowMatch.Success)
        {
            return false;
        }

        if (!TryParseDate(rowMatch.Groups["date"].Value, out var date))
        {
            return false;
        }

        var rest = rowMatch.Groups["rest"].Value.Trim();
        var amountMatches = PdfAmountTokenRegex.Matches(rest);
        if (amountMatches.Count == 0)
        {
            return false;
        }

        // Filter to monetary tokens (containing decimal/thousands separators) and exclude date-like tokens
        var monetaryMatches = amountMatches
            .Cast<Match>()
            .Where(m =>
            {
                var val = m.Groups["amount"].Value.Trim();
                return (val.Contains(',') || val.Contains('.')) && !TryParseDate(val, out _);
            })
            .ToList();

        if (monetaryMatches.Count == 0)
        {
            return false;
        }

        // Turkish bank PDF tables often contain [Amount] [Running Balance] at the end of a row,
        // e.g. "05.11.2026 MIGROS MARKET -3.420,50 TL 42.100,00 TL".
        // Prefer the signed transaction amount when present; otherwise pick the amount before the trailing balance.
        Match chosenMatch;
        if (monetaryMatches.Count >= 2)
        {
            var signedMatch = monetaryMatches
                .Take(monetaryMatches.Count - 1)
                .FirstOrDefault(m =>
                {
                    var v = m.Groups["amount"].Value.Trim();
                    return v.StartsWith('-') || v.StartsWith('+') || v.EndsWith('-') ||
                           (v.StartsWith('(') && v.EndsWith(')'));
                });

            chosenMatch = signedMatch ?? monetaryMatches[^2];
        }
        else
        {
            chosenMatch = monetaryMatches[0];
        }

        if (!TryParseAmount(chosenMatch.Groups["amount"].Value, out var signedAmount) || signedAmount == 0m)
        {
            return false;
        }

        var description = rest[..chosenMatch.Index].Trim(' ', '-', '|', '\t');
        // Strip a second date column (Valör Tarihi) if present at the start of description
        var valorPrefix = Regex.Match(description, @"^(?:\d{2}[./-]\d{2}[./-]\d{2,4}|\d{4}-\d{2}-\d{2})\s+");
        if (valorPrefix.Success)
        {
            description = description[valorPrefix.Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = signedAmount > 0 ? "Gelir İşlemi" : "Harcama İşlemi";
        }

        row = new ParsedStatementRow(
            date,
            description.Length > 200 ? description[..200] : description,
            decimal.Round(signedAmount, 2, MidpointRounding.AwayFromZero));
        return true;
    }

    private static bool IsIgnoredPdfHeaderOrSummary(string normalizedLine)
    {
        if (string.IsNullOrWhiteSpace(normalizedLine))
        {
            return true;
        }

        if (IgnoredPdfSummaryMarkers.Any(marker => normalizedLine.Contains(marker, StringComparison.Ordinal)))
        {
            return true;
        }

        return normalizedLine.StartsWith("TOPLAM ", StringComparison.Ordinal) ||
               normalizedLine.StartsWith("SAYFA ", StringComparison.Ordinal);
    }

    private static (List<string> CoordinateLines, List<string> SequentialLines) ExtractPdfTextLines(byte[] pdfBytes)
    {
        var latin1 = Encoding.Latin1.GetString(pdfBytes);
        var cmap = new Dictionary<int, string>();
        var decodedStreams = new List<string>();

        var searchPos = 0;
        while (searchPos < latin1.Length)
        {
            var streamIdx = latin1.IndexOf("stream", searchPos, StringComparison.Ordinal);
            if (streamIdx < 0)
            {
                break;
            }

            // Ensure "stream" is not part of "endstream"
            if (streamIdx >= 3 && latin1.AsSpan(streamIdx - 3, 3).SequenceEqual("end"))
            {
                searchPos = streamIdx + 6;
                continue;
            }

            var dataStart = streamIdx + 6;
            if (dataStart < latin1.Length && latin1[dataStart] == '\r') dataStart++;
            if (dataStart < latin1.Length && latin1[dataStart] == '\n') dataStart++;

            var endStreamIdx = latin1.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (endStreamIdx < 0)
            {
                break;
            }

            var dictStart = Math.Max(0, streamIdx - 512);
            var dictSnippet = latin1[dictStart..streamIdx];
            var isFlate = dictSnippet.Contains("/FlateDecode", StringComparison.Ordinal);

            var rawLen = endStreamIdx - dataStart;
            while (rawLen > 0 && (pdfBytes[dataStart + rawLen - 1] == '\r' || pdfBytes[dataStart + rawLen - 1] == '\n'))
            {
                rawLen--;
            }

            if (rawLen > 0)
            {
                byte[] streamData;
                if (isFlate)
                {
                    streamData = TryDecompressFlate(pdfBytes, dataStart, rawLen) ?? [];
                }
                else
                {
                    streamData = new byte[rawLen];
                    Buffer.BlockCopy(pdfBytes, dataStart, streamData, 0, rawLen);
                }

                if (streamData.Length > 0)
                {
                    var streamText = Encoding.Latin1.GetString(streamData);
                    if (streamText.Contains("beginbfchar", StringComparison.Ordinal) ||
                        streamText.Contains("beginbfrange", StringComparison.Ordinal))
                    {
                        ParseToUnicodeCMap(streamText, cmap);
                    }
                    else if (streamText.Contains("BT", StringComparison.Ordinal) &&
                             (streamText.Contains("Tj", StringComparison.Ordinal) ||
                              streamText.Contains("TJ", StringComparison.Ordinal) ||
                              streamText.Contains("'", StringComparison.Ordinal)))
                    {
                        decodedStreams.Add(streamText);
                    }
                }
            }

            searchPos = endStreamIdx + 9;
        }

        var coordinateLines = new List<string>();
        var sequentialLines = new List<string>();
        foreach (var contentStream in decodedStreams)
        {
            ExtractLinesFromContentStream(contentStream, cmap, coordinateLines, sequentialLines);
        }

        return (coordinateLines, sequentialLines);
    }

    private static byte[]? TryDecompressFlate(byte[] source, int offset, int length)
    {
        try
        {
            using var input = new MemoryStream(source, offset, length, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            try
            {
                using var input = new MemoryStream(source, offset, length, writable: false);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                return output.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }

    private static void ParseToUnicodeCMap(string cmapText, Dictionary<int, string> map)
    {
        // Parse beginbfchar ... endbfchar
        foreach (Match section in Regex.Matches(cmapText, @"beginbfchar(?<body>[\s\S]*?)endbfchar"))
        {
            foreach (Match m in Regex.Matches(section.Groups["body"].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
            {
                if (int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var src))
                {
                    var decoded = DecodeUnicodeHex(m.Groups[2].Value);
                    if (!string.IsNullOrEmpty(decoded))
                    {
                        map[src] = decoded;
                    }
                }
            }
        }

        // Parse beginbfrange ... endbfrange
        foreach (Match section in Regex.Matches(cmapText, @"beginbfrange(?<body>[\s\S]*?)endbfrange"))
        {
            var body = section.Groups["body"].Value;

            // Format 1: <lo> <hi> <dstStart>
            foreach (Match m in Regex.Matches(body, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
            {
                if (int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lo) &&
                    int.TryParse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hi) &&
                    int.TryParse(m.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var dstStart) &&
                    hi >= lo && hi - lo <= 512)
                {
                    for (var code = lo; code <= hi; code++)
                    {
                        map[code] = char.ConvertFromUtf32(dstStart + (code - lo));
                    }
                }
            }

            // Format 2: <lo> <hi> [ <dst1> <dst2> ... ]
            foreach (Match m in Regex.Matches(body, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*\[(?<arr>[^\]]+)\]"))
            {
                if (int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lo) &&
                    int.TryParse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hi) &&
                    hi >= lo && hi - lo <= 512)
                {
                    var items = Regex.Matches(m.Groups["arr"].Value, @"<([0-9A-Fa-f]+)>");
                    for (var idx = 0; idx < items.Count && lo + idx <= hi; idx++)
                    {
                        var decoded = DecodeUnicodeHex(items[idx].Groups[1].Value);
                        if (!string.IsNullOrEmpty(decoded))
                        {
                            map[lo + idx] = decoded;
                        }
                    }
                }
            }
        }
    }

    private static string DecodeUnicodeHex(string dstHex)
    {
        if (dstHex.Length < 4 || dstHex.Length % 4 != 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < dstHex.Length; i += 4)
        {
            if (int.TryParse(dstHex.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp))
            {
                sb.Append((char)cp);
            }
        }

        return sb.ToString();
    }

    private sealed record PdfPositionedText(
        double X,
        double Y,
        double EstimatedEndX,
        double FontSize,
        int Order,
        string Text,
        bool IsWhitespaceOnly);

    private readonly record struct PdfMatrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly PdfMatrix Identity = new(1, 0, 0, 1, 0, 0);

        public static PdfMatrix Multiply(PdfMatrix m1, PdfMatrix m2) =>
            new(
                m1.A * m2.A + m1.B * m2.C,
                m1.A * m2.B + m1.B * m2.D,
                m1.C * m2.A + m1.D * m2.C,
                m1.C * m2.B + m1.D * m2.D,
                m1.E * m2.A + m1.F * m2.C + m2.E,
                m1.E * m2.B + m1.F * m2.D + m2.F);

        public (double X, double Y) TransformOrigin() => (E, F);
    }

    private static double EstimateTextAdvanceWidth(string text, double fontSize)
    {
        var width = 0.0;
        foreach (var ch in text)
        {
            var factor = ch switch
            {
                ' ' or '\t' => 0.28,
                '.' or ',' or ':' or ';' or '!' or '\'' or '|' or 'i' or 'l' or 'I' or 'ı' or 'İ' or '1' or '-' => 0.32,
                'M' or 'W' or 'm' or 'w' or '%' or '@' or '₺' => 0.68,
                _ => 0.54
            };
            width += factor * fontSize;
        }

        return width;
    }

    /// <summary>
    /// Extracts positioned text fragments from a PDF content stream by tracking CTM (q/Q/cm),
    /// font size (Tf), and text matrices (Tm/Td/TD/TL/T*), then reconstructs visual table lines
    /// using gap-aware horizontal joining (only inserting spaces when horizontal gap exceeds
    /// the word-space threshold, preventing spaced-out single glyphs like "0 1 . 1 1 . 2 0 2 6").
    /// </summary>
    private static void ExtractLinesFromContentStream(
        string streamText,
        Dictionary<int, string> cmap,
        List<string> coordinateLines,
        List<string> sequentialLines)
    {
        const double yBucketTolerancePt = 5.0;

        var fragments = new List<PdfPositionedText>();
        var numericOperands = new List<double>();
        var ctmStack = new Stack<PdfMatrix>();
        var ctm = PdfMatrix.Identity;
        var textMatrix = PdfMatrix.Identity;
        var textLineMatrix = PdfMatrix.Identity;
        var textLeading = 0.0;
        var currentFontSize = 10.0;
        var inTextObject = false;
        var order = 0;

        void EmitText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var combined = PdfMatrix.Multiply(textMatrix, ctm);
            var (x, y) = combined.TransformOrigin();
            var scaleX = Math.Sqrt(combined.A * combined.A + combined.B * combined.B);
            var effectiveFontSize = Math.Max(4.0, currentFontSize * (scaleX > 0.01 ? scaleX : 1.0));
            var advanceWidth = EstimateTextAdvanceWidth(text, effectiveFontSize);
            var isWs = string.IsNullOrWhiteSpace(text);

            fragments.Add(new PdfPositionedText(
                x,
                y,
                x + advanceWidth,
                effectiveFontSize,
                order++,
                isWs ? " " : text,
                isWs));
        }

        var i = 0;
        while (i < streamText.Length)
        {
            while (i < streamText.Length && char.IsWhiteSpace(streamText[i])) i++;
            if (i >= streamText.Length) break;

            // Skip PDF comments (%)
            if (streamText[i] == '%')
            {
                while (i < streamText.Length && streamText[i] != '\r' && streamText[i] != '\n') i++;
                continue;
            }

            if (streamText[i] == '(')
            {
                var literal = ReadPdfLiteralString(streamText, ref i);
                var j = i;
                while (j < streamText.Length && char.IsWhiteSpace(streamText[j])) j++;
                if (j + 1 < streamText.Length && streamText[j] == 'T' && streamText[j + 1] == 'j')
                {
                    if (inTextObject) EmitText(literal);
                    numericOperands.Clear();
                    i = j + 2;
                    continue;
                }

                if (j < streamText.Length && (streamText[j] == '\'' || streamText[j] == '"'))
                {
                    if (inTextObject)
                    {
                        textLineMatrix = PdfMatrix.Multiply(new PdfMatrix(1, 0, 0, 1, 0, -textLeading), textLineMatrix);
                        textMatrix = textLineMatrix;
                        EmitText(literal);
                    }
                    numericOperands.Clear();
                    i = j + 1;
                    continue;
                }

                continue;
            }

            if (streamText[i] == '<' && (i + 1 >= streamText.Length || streamText[i + 1] != '<'))
            {
                var hexStr = ReadPdfHexString(streamText, ref i, cmap);
                var j = i;
                while (j < streamText.Length && char.IsWhiteSpace(streamText[j])) j++;
                if (j + 1 < streamText.Length && streamText[j] == 'T' && streamText[j + 1] == 'j')
                {
                    if (inTextObject) EmitText(hexStr);
                    numericOperands.Clear();
                    i = j + 2;
                    continue;
                }

                continue;
            }

            if (streamText[i] == '[')
            {
                var arrayText = ReadPdfTjArray(streamText, ref i, cmap);
                var j = i;
                while (j < streamText.Length && char.IsWhiteSpace(streamText[j])) j++;
                if (j + 1 < streamText.Length && streamText[j] == 'T' && streamText[j + 1] == 'J')
                {
                    if (inTextObject) EmitText(arrayText);
                    numericOperands.Clear();
                    i = j + 2;
                    continue;
                }

                continue;
            }

            // Skip dictionary markers << >>
            if (streamText[i] == '<' && i + 1 < streamText.Length && streamText[i + 1] == '<')
            {
                i += 2;
                continue;
            }

            if (streamText[i] == '>' && i + 1 < streamText.Length && streamText[i + 1] == '>')
            {
                i += 2;
                continue;
            }

            var tokenStart = i;
            while (i < streamText.Length && !char.IsWhiteSpace(streamText[i]) &&
                   streamText[i] != '(' && streamText[i] != '[' && streamText[i] != '<' && streamText[i] != '>')
            {
                i++;
            }

            var token = streamText[tokenStart..i];
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            {
                numericOperands.Add(num);
                continue;
            }

            switch (token)
            {
                case "q":
                    ctmStack.Push(ctm);
                    break;
                case "Q":
                    if (ctmStack.Count > 0) ctm = ctmStack.Pop();
                    break;
                case "cm":
                    if (numericOperands.Count >= 6)
                    {
                        var n = numericOperands.Count;
                        var m = new PdfMatrix(
                            numericOperands[n - 6], numericOperands[n - 5],
                            numericOperands[n - 4], numericOperands[n - 3],
                            numericOperands[n - 2], numericOperands[n - 1]);
                        ctm = PdfMatrix.Multiply(m, ctm);
                    }
                    break;
                case "BT":
                    inTextObject = true;
                    textMatrix = PdfMatrix.Identity;
                    textLineMatrix = PdfMatrix.Identity;
                    break;
                case "ET":
                    inTextObject = false;
                    break;
                case "Tm":
                    if (inTextObject && numericOperands.Count >= 6)
                    {
                        var n = numericOperands.Count;
                        textMatrix = new PdfMatrix(
                            numericOperands[n - 6], numericOperands[n - 5],
                            numericOperands[n - 4], numericOperands[n - 3],
                            numericOperands[n - 2], numericOperands[n - 1]);
                        textLineMatrix = textMatrix;
                    }
                    break;
                case "Td":
                    if (inTextObject && numericOperands.Count >= 2)
                    {
                        var n = numericOperands.Count;
                        var tx = numericOperands[n - 2];
                        var ty = numericOperands[n - 1];
                        textLineMatrix = PdfMatrix.Multiply(new PdfMatrix(1, 0, 0, 1, tx, ty), textLineMatrix);
                        textMatrix = textLineMatrix;
                    }
                    break;
                case "TD":
                    if (inTextObject && numericOperands.Count >= 2)
                    {
                        var n = numericOperands.Count;
                        var tx = numericOperands[n - 2];
                        var ty = numericOperands[n - 1];
                        textLeading = -ty;
                        textLineMatrix = PdfMatrix.Multiply(new PdfMatrix(1, 0, 0, 1, tx, ty), textLineMatrix);
                        textMatrix = textLineMatrix;
                    }
                    break;
                case "Tf":
                    if (numericOperands.Count >= 1 && numericOperands[^1] > 0)
                    {
                        currentFontSize = numericOperands[^1];
                    }
                    break;
                case "TL":
                    if (numericOperands.Count >= 1)
                    {
                        textLeading = numericOperands[^1];
                    }
                    break;
                case "T*":
                    if (inTextObject)
                    {
                        textLineMatrix = PdfMatrix.Multiply(new PdfMatrix(1, 0, 0, 1, 0, -textLeading), textLineMatrix);
                        textMatrix = textLineMatrix;
                    }
                    break;
            }

            numericOperands.Clear();
        }

        if (fragments.Count == 0)
        {
            return;
        }

        // Group positioned fragments into visual lines using 4 pt Y-coordinate buckets.
        // Sort fragments top-to-bottom (descending Y) first so buckets form cleanly from top to bottom.
        var orderedByY = fragments
            .OrderByDescending(f => f.Y)
            .ThenBy(f => f.X)
            .ThenBy(f => f.Order)
            .ToList();

        var visualRows = new List<(double AnchorY, List<PdfPositionedText> Items)>();
        foreach (var frag in orderedByY)
        {
            var matchedIdx = -1;
            for (var r = visualRows.Count - 1; r >= 0; r--)
            {
                if (Math.Abs(frag.Y - visualRows[r].AnchorY) <= yBucketTolerancePt)
                {
                    matchedIdx = r;
                    break;
                }
            }

            if (matchedIdx >= 0)
            {
                visualRows[matchedIdx].Items.Add(frag);
            }
            else
            {
                visualRows.Add((frag.Y, [frag]));
            }
        }

        foreach (var (_, rowItems) in visualRows.OrderByDescending(r => r.AnchorY))
        {
            var sorted = rowItems
                .OrderBy(item => item.X)
                .ThenBy(item => item.Order)
                .ToList();

            var lineBuilder = new StringBuilder();
            PdfPositionedText? previous = null;

            foreach (var current in sorted)
            {
                if (current.IsWhitespaceOnly)
                {
                    if (lineBuilder.Length > 0 && lineBuilder[^1] != ' ')
                    {
                        lineBuilder.Append(' ');
                    }
                    previous = current;
                    continue;
                }

                if (previous is not null && !previous.IsWhitespaceOnly && lineBuilder.Length > 0 && lineBuilder[^1] != ' ')
                {
                    var gap = current.X - previous.EstimatedEndX;
                    var spaceThreshold = Math.Max(previous.FontSize * 0.20, 1.8);

                    if (gap > spaceThreshold)
                    {
                        lineBuilder.Append(' ');
                    }
                }

                lineBuilder.Append(current.Text);
                previous = current;
            }

            var lineText = lineBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(lineText))
            {
                coordinateLines.Add(lineText);
            }
        }

        // Also build sequential stream-order lines as a fallback if coordinate grouping misses a layout
        foreach (var frag in fragments)
        {
            foreach (var sub in frag.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = sub.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    sequentialLines.Add(trimmed);
                }
            }
        }
    }

    private static string ReadPdfLiteralString(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++; // skip '('
        var depth = 1;
        while (i < s.Length && depth > 0)
        {
            var ch = s[i++];
            if (ch == '\\' && i < s.Length)
            {
                var esc = s[i++];
                switch (esc)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '(': sb.Append('('); break;
                    case ')': sb.Append(')'); break;
                    case '\\': sb.Append('\\'); break;
                    default:
                        if (esc is >= '0' and <= '7')
                        {
                            var oct = esc.ToString();
                            for (var k = 0; k < 2 && i < s.Length && s[i] is >= '0' and <= '7'; k++)
                            {
                                oct += s[i++];
                            }
                            sb.Append(DecodeWindows1254Byte((byte)Convert.ToInt32(oct, 8)));
                        }
                        else
                        {
                            sb.Append(esc);
                        }
                        break;
                }
            }
            else if (ch == '(')
            {
                depth++;
                sb.Append(ch);
            }
            else if (ch == ')')
            {
                depth--;
                if (depth > 0) sb.Append(ch);
            }
            else
            {
                sb.Append(DecodeWindows1254Byte((byte)ch));
            }
        }

        return sb.ToString();
    }

    private static string ReadPdfHexString(string s, ref int i, Dictionary<int, string> cmap)
    {
        var end = s.IndexOf('>', i + 1);
        if (end < 0)
        {
            i = s.Length;
            return string.Empty;
        }

        var hex = Regex.Replace(s[(i + 1)..end], @"\s+", "");
        i = end + 1;
        if (hex.Length % 2 == 1) hex += "0";

        // Pick 4-hex (2-byte CID) vs 2-hex (1-byte) dynamically based on actual CMap hits
        var step = 2;
        if (cmap.Count > 0 && hex.Length >= 4 && hex.Length % 4 == 0)
        {
            int hits4 = 0, hits2 = 0;
            for (var k = 0; k + 4 <= hex.Length; k += 4)
            {
                if (int.TryParse(hex.AsSpan(k, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c4) && cmap.ContainsKey(c4))
                {
                    hits4++;
                }
            }
            for (var k = 0; k + 2 <= hex.Length; k += 2)
            {
                if (int.TryParse(hex.AsSpan(k, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c2) && cmap.ContainsKey(c2))
                {
                    hits2++;
                }
            }

            if (hits4 > 0 && hits4 * 2 >= hits2)
            {
                step = 4;
            }
        }

        var sb = new StringBuilder();
        for (var k = 0; k + step <= hex.Length; k += step)
        {
            if (int.TryParse(hex.AsSpan(k, step), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
            {
                if (cmap.TryGetValue(code, out var mapped))
                {
                    sb.Append(mapped);
                }
                else if (code <= 255)
                {
                    sb.Append(DecodeWindows1254Byte((byte)code));
                }
                else
                {
                    sb.Append((char)code);
                }
            }
        }

        return sb.ToString();
    }

    private static string ReadPdfTjArray(string s, ref int i, Dictionary<int, string> cmap)
    {
        var sb = new StringBuilder();
        i++; // skip '['
        while (i < s.Length && s[i] != ']')
        {
            if (s[i] == '(')
            {
                sb.Append(ReadPdfLiteralString(s, ref i));
            }
            else if (s[i] == '<')
            {
                sb.Append(ReadPdfHexString(s, ref i, cmap));
            }
            else if (s[i] == '-' || char.IsDigit(s[i]))
            {
                var start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                if (double.TryParse(s[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var kern) && kern < -110)
                {
                    sb.Append(' ');
                }
            }
            else
            {
                i++;
            }
        }

        if (i < s.Length && s[i] == ']') i++;
        return sb.ToString();
    }

    // ───────────────────────────── Parsing & Encoding Helpers ─────────────────────────────

    public static bool TryParseDate(string raw, out DateOnly result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var cleaned = raw.Trim();
        // Strip trailing time portion if present (e.g., "15.11.2026 14:32:00")
        var spaceIdx = cleaned.IndexOf(' ');
        if (spaceIdx > 0)
        {
            cleaned = cleaned[..spaceIdx];
        }

        if (DateOnly.TryParseExact(cleaned, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            return result.Year is >= 2000 and <= 2100;
        }

        // Excel serial date (days since 1899-12-30)
        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) &&
            serial is >= 36526 and <= 73050)
        {
            var dt = new DateTime(1899, 12, 30).AddDays(Math.Floor(serial));
            result = DateOnly.FromDateTime(dt);
            return true;
        }

        return false;
    }

    public static bool TryParseAmount(string raw, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var cleaned = raw.Trim()
            .Replace("TRY", "", StringComparison.OrdinalIgnoreCase)
            .Replace("TL", "", StringComparison.OrdinalIgnoreCase)
            .Replace("USD", "", StringComparison.OrdinalIgnoreCase)
            .Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
            .Replace("GBP", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₺", "")
            .Replace("$", "")
            .Replace("€", "")
            .Replace("£", "")
            .Replace(" ", "")
            .Replace("\u00A0", "");

        if (string.IsNullOrEmpty(cleaned))
        {
            return false;
        }

        var isNegative = false;
        if (cleaned.StartsWith('(') && cleaned.EndsWith(')'))
        {
            isNegative = true;
            cleaned = cleaned[1..^1];
        }
        else if (cleaned.StartsWith('-'))
        {
            isNegative = true;
            cleaned = cleaned[1..];
        }
        else if (cleaned.EndsWith('-'))
        {
            isNegative = true;
            cleaned = cleaned[..^1];
        }
        else if (cleaned.StartsWith('+'))
        {
            cleaned = cleaned[1..];
        }

        if (string.IsNullOrEmpty(cleaned))
        {
            return false;
        }

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');

        string normalized;
        if (lastComma > lastDot)
        {
            // Turkish/European format: 1.250,50 -> 1250.50
            normalized = cleaned.Replace(".", "").Replace(',', '.');
        }
        else if (lastDot > lastComma)
        {
            // Check if dot is thousands separator (e.g., 1.250 with 3 trailing digits and no comma)
            var afterDot = cleaned.Length - lastDot - 1;
            if (lastComma == -1 && afterDot == 3 && cleaned.Count(c => c == '.') >= 1)
            {
                normalized = cleaned.Replace(".", "");
            }
            else
            {
                // US/Standard format: 1,250.50 -> 1250.50
                normalized = cleaned.Replace(",", "");
            }
        }
        else
        {
            normalized = cleaned;
        }

        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        result = isNegative ? -Math.Abs(parsed) : parsed;
        return true;
    }

    public static string DecodeTextWithTurkishFallback(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Decode as Windows-1254 (Turkish) without requiring CodePagesEncodingProvider
            var chars = new char[bytes.Length];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i] = DecodeWindows1254Byte(bytes[i]);
            }

            return new string(chars);
        }
    }

    private static char DecodeWindows1254Byte(byte b) => b switch
    {
        0x80 => '\u20AC', // €
        0xD0 => 'Ğ',
        0xDD => 'İ',
        0xDE => 'Ş',
        0xF0 => 'ğ',
        0xFD => 'ı',
        0xFE => 'ş',
        _ => (char)b
    };
}
