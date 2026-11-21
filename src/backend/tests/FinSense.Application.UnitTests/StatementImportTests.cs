using System.IO.Compression;
using System.Text;
using FinSense.Application.Features.Transactions.Import;
using FinSense.Application.UnitTests.Common;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FinSense.Domain.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.UnitTests;

public class StatementImportTests
{
    [Fact]
    public void CsvParser_ShouldHandleSemicolonDelimiter_TurkishDecimals_QuotedFields_AndSeparateDebitCreditColumns()
    {
        var csv = """
            İşlem Tarihi;Açıklama;Tutar
            15.11.2026;"MIGROS TICARET A.S.; ISTANBUL";-1.250,50
            16.11.2026;STARBUCKS COFFEE;-185,00 TL
            18.11.2026;MAAS ODEMESI;+65.000,00
            """;

        var rows = BankStatementParser.ParseCsv(Encoding.UTF8.GetBytes(csv));

        rows.Should().HaveCount(3);
        rows[0].TransactionDate.Should().Be(new DateOnly(2026, 11, 15));
        rows[0].Description.Should().Be("MIGROS TICARET A.S.; ISTANBUL");
        rows[0].SignedAmount.Should().Be(-1250.50m);

        rows[1].SignedAmount.Should().Be(-185.00m);
        rows[2].SignedAmount.Should().Be(65000.00m);

        // Also test separate Borç / Alacak columns with Turkish comma decimals
        var debitCreditSemi = """
            Tarih;Açıklama;Borç;Alacak
            01/11/2026;EV KIRA ODEMESI;22.000,00;
            02/11/2026;FREELANCE PROJE;;12.500,50
            """;

        var dcRows = BankStatementParser.ParseCsv(Encoding.UTF8.GetBytes(debitCreditSemi));
        dcRows.Should().HaveCount(2);
        dcRows[0].SignedAmount.Should().Be(-22000.00m);
        dcRows[1].SignedAmount.Should().Be(12500.50m);
    }

    [Fact]
    public void XlsxParser_ShouldParseSharedStringsAndNumericCells()
    {
        var xlsxBytes = BuildMinimalXlsx(
        [
            ["Tarih", "Açıklama", "Tutar"],
            ["10.11.2026", "SPOTIFY ABONELIK", "-59.99"],
            ["12.11.2026", "MAAS ODEMESI", "65000.00"]
        ]);

        var result = BankStatementParser.Parse("ekstre.xlsx", xlsxBytes);

        result.DetectedFormat.Should().Be("XLSX");
        result.Rows.Should().HaveCount(2);
        result.Rows[0].TransactionDate.Should().Be(new DateOnly(2026, 11, 10));
        result.Rows[0].Description.Should().Be("SPOTIFY ABONELIK");
        result.Rows[0].SignedAmount.Should().Be(-59.99m);
        result.Rows[1].SignedAmount.Should().Be(65000.00m);
    }

    [Fact]
    public void PdfParser_ShouldParseTurkishBankLines_AndRejectImageOnlyPdfWithExactMessage()
    {
        var pdfLines = new[]
        {
            "HESAP EKSTRESI - KASIM 2026",
            "14.11.2026 SHELL AKARYAKIT ISTASYONU -1.450,00 TL 42.300,00 TL",
            "15/11/2026 TURKCELL FATURA ODEME -420,50 41.879,50",
            "16.11.2026 MAAS YATIRMA +65.000,00 TL"
        };

        var rows = BankStatementParser.ParsePdfLines(pdfLines);

        rows.Should().HaveCount(3);
        rows[0].TransactionDate.Should().Be(new DateOnly(2026, 11, 14));
        rows[0].Description.Should().Be("SHELL AKARYAKIT ISTASYONU");
        rows[0].SignedAmount.Should().Be(-1450.00m);

        rows[1].TransactionDate.Should().Be(new DateOnly(2026, 11, 15));
        rows[1].SignedAmount.Should().Be(-420.50m);

        rows[2].SignedAmount.Should().Be(65000.00m);

        // Text-based PDF bytes with BT/ET stream
        var textPdf = Encoding.Latin1.GetBytes(
            "%PDF-1.4\n1 0 obj\n<< /Length 64 >>\nstream\nBT\n(15.11.2026 MIGROS MARKET -850,25 TL) Tj\nET\nendstream\nendobj\n%%EOF");
        var pdfResult = BankStatementParser.Parse("ekstre.pdf", textPdf);
        pdfResult.DetectedFormat.Should().Be("PDF");
        pdfResult.Rows.Should().ContainSingle(r => r.SignedAmount == -850.25m && r.Description.Contains("MIGROS"));

        // Image-only PDF (no selectable text) must throw exact Turkish validation message
        var scannedPdf = Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj\n<< /Subtype /Image >>\nendobj\n%%EOF");
        var act = () => BankStatementParser.Parse("taranmis.pdf", scannedPdf);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "PDF_NO_TEXT" &&
                        e.Message == "Bu PDF metin içermiyor. Lütfen dijital banka ekstresi (PDF/Excel/CSV) yükleyin.");
    }

    [Fact]
    public void PdfParser_ShouldReconstructMultiColumnPdfTableRowsByCoordinates_AndIgnoreHeaderSummaryLines()
    {
        // Simulate a real table-based PDF where each column cell is emitted in its own BT...ET block
        // with distinct X/Y coordinates (including slight 1-2 pt vertical baseline jitter and trailing Balance column).
        var tableStream = """
            BT 1 0 0 1 40 760 Tm (Tarih) Tj ET
            BT 1 0 0 1 120 760 Tm (Aciklama) Tj ET
            BT 1 0 0 1 360 760 Tm (Tutar) Tj ET
            BT 1 0 0 1 460 760 Tm (Bakiye) Tj ET

            BT 1 0 0 1 40 735 Tm (01.11.2026) Tj ET
            BT 1 0 0 1 120 735 Tm (Devreden Bakiye) Tj ET
            BT 1 0 0 1 460 735 Tm (45.520,50 TL) Tj ET

            BT 1 0 0 1 460 710 Tm (42.100,00 TL) Tj ET
            BT 1 0 0 1 40 711 Tm (05.11.2026) Tj ET
            BT 1 0 0 1 360 710 Tm (-3.420,50 TL) Tj ET
            BT 1 0 0 1 120 709 Tm (MIGROS MARKET) Tj ET

            BT 1 0 0 1 40 685 Tm (07/11/2026) Tj ET
            BT 1 0 0 1 120 685 Tm (STARBUCKS KADIKOY) Tj ET
            BT 1 0 0 1 360 685 Tm (-257,00 TRY) Tj ET
            BT 1 0 0 1 460 685 Tm (41.843,00 TRY) Tj ET

            BT 1 0 0 1 40 660 Tm (2026-11-15) Tj ET
            BT 1 0 0 1 120 660 Tm (AYLIK MAAS ODEMESI) Tj ET
            BT 1 0 0 1 360 660 Tm (+65.000,00) Tj ET
            BT 1 0 0 1 460 660 Tm (106.843,00) Tj ET

            BT 1 0 0 1 40 620 Tm (Toplam Harcama -3.677,50 TL) Tj ET
            """;

        var pdfBytes = Encoding.Latin1.GetBytes(
            $"%PDF-1.4\n1 0 obj\n<< /Length {tableStream.Length} >>\nstream\n{tableStream}\nendstream\nendobj\n%%EOF");

        var parsed = BankStatementParser.Parse("banka-tablo.pdf", pdfBytes);

        parsed.DetectedFormat.Should().Be("PDF");
        parsed.Rows.Should().HaveCount(3);

        parsed.Rows[0].TransactionDate.Should().Be(new DateOnly(2026, 11, 5));
        parsed.Rows[0].Description.Should().Be("MIGROS MARKET");
        parsed.Rows[0].SignedAmount.Should().Be(-3420.50m);

        parsed.Rows[1].TransactionDate.Should().Be(new DateOnly(2026, 11, 7));
        parsed.Rows[1].Description.Should().Be("STARBUCKS KADIKOY");
        parsed.Rows[1].SignedAmount.Should().Be(-257.00m);

        parsed.Rows[2].TransactionDate.Should().Be(new DateOnly(2026, 11, 15));
        parsed.Rows[2].Description.Should().Be("AYLIK MAAS ODEMESI");
        parsed.Rows[2].SignedAmount.Should().Be(65000.00m);
    }

    [Fact]
    public void PdfParser_ShouldDetectByMagicBytesWithoutExtension_NormalizeUnicodeMinus_AndHandleWrappedLines()
    {
        // 1. Magic-byte detection even when Android document picker omits .pdf extension & MIME type
        var stream = "BT 1 0 0 1 40 700 Tm (05.11.2026 MIGROS - 3.420,50 TL 42.100,00 TL) Tj ET";
        var pdfBytes = Encoding.Latin1.GetBytes(
            $"%PDF-1.7\n1 0 obj\n<< /Length {stream.Length} >>\nstream\n{stream}\nendstream\nendobj\n%%EOF");

        var detected = BankStatementParser.Parse("document_98765", pdfBytes, "application/octet-stream");
        detected.DetectedFormat.Should().Be("PDF");
        detected.Rows.Should().ContainSingle(r => r.SignedAmount == -3420.50m && r.Description == "MIGROS");

        // 2. Unicode minus signs (−, –) + spaces after sign + multi-line wrapped rows
        var wrappedLines = new[]
        {
            "05.11.2026 MIGROS TICARET A.S.",
            "KADIKOY ISTANBUL \u2212 1.250,50 TL 42.100,00 TL",
            "08.11.2026",
            "SPOTIFY PREMIUM \u201359,99 TRY",
            "15.11.2026 MAAS ODEMESI + 65.000,00 TL"
        };

        var rows = BankStatementParser.ParsePdfLines(wrappedLines);
        rows.Should().HaveCount(3);
        rows[0].TransactionDate.Should().Be(new DateOnly(2026, 11, 5));
        rows[0].SignedAmount.Should().Be(-1250.50m);
        rows[1].TransactionDate.Should().Be(new DateOnly(2026, 11, 8));
        rows[1].SignedAmount.Should().Be(-59.99m);
        rows[2].TransactionDate.Should().Be(new DateOnly(2026, 11, 15));
        rows[2].SignedAmount.Should().Be(65000.00m);

        // 3. Global full-text fallback when multiple transactions are concatenated without newlines
        var singleLineBlob = "HESAP EKSTRESI 05.11.2026 MIGROS MARKET -3.420,50 TL 42.100,00 TL 10.11.2026 SPOTIFY -59,99 42.040,01 15.11.2026 MAAS +65.000,00 TRY";
        var fallbackRows = BankStatementParser.ParsePdfFullTextFallback(singleLineBlob);
        fallbackRows.Should().HaveCount(3);
        fallbackRows[0].SignedAmount.Should().Be(-3420.50m);
        fallbackRows[1].SignedAmount.Should().Be(-59.99m);
        fallbackRows[2].SignedAmount.Should().Be(65000.00m);
    }

    [Fact]
    public void PdfParser_ShouldReconstructSingleGlyphPositionedStreamsWithoutIntraWordSpaces_AndAutoCategorize()
    {
        // Build a PDF content stream where every individual character is positioned with its own Tm + Tj,
        // mirroring PDF generators that emit glyph-by-glyph text objects.
        static string EmitGlyphByGlyphRow(string text, double startX, double y)
        {
            var sb = new StringBuilder();
            var x = startX;
            sb.AppendLine("BT /F1 10 Tf");
            foreach (var ch in text)
            {
                if (ch == ' ')
                {
                    x += 4.5; // word-space gap > threshold
                    continue;
                }

                var escaped = ch is '(' or ')' or '\\' ? $"\\{ch}" : ch.ToString();
                sb.AppendLine(FormattableString.Invariant($"1 0 0 1 {x:F2} {y:F2} Tm ({escaped}) Tj"));
                x += ch is '.' or ',' or '-' or 'I' or '1' ? 3.5 : 5.6;
            }
            sb.AppendLine("ET");
            return sb.ToString();
        }

        var streamContent =
            EmitGlyphByGlyphRow("01.11.2026 MAAS ODEMESI - KASIM 2026 +65.000,00 TL 83.500,00 TL", 40, 720) +
            EmitGlyphByGlyphRow("02.11.2026 EV KIRA ODEMESI HAVALE -18.500,00 TL 65.000,00 TL", 40, 700);

        var pdfBytes = Encoding.Latin1.GetBytes(
            $"%PDF-1.4\n1 0 obj\n<< /Length {streamContent.Length} >>\nstream\n{streamContent}\nendstream\nendobj\n%%EOF");

        IReadOnlyList<string>? extractedLines = null;
        var parsed = BankStatementParser.Parse("glyph-ekstre.pdf", pdfBytes, "application/pdf", l => extractedLines = l);

        extractedLines.Should().NotBeNull();
        extractedLines!.Should().Contain(l => l.Contains("01.11.2026") && l.Contains("+65.000,00"));
        extractedLines.Should().Contain(l => l.Contains("02.11.2026") && l.Contains("-18.500,00"));

        parsed.DetectedFormat.Should().Be("PDF");
        parsed.Rows.Should().HaveCount(2);

        parsed.Rows[0].TransactionDate.Should().Be(new DateOnly(2026, 11, 1));
        parsed.Rows[0].Description.Should().Be("MAAS ODEMESI - KASIM 2026");
        parsed.Rows[0].SignedAmount.Should().Be(65000.00m);

        parsed.Rows[1].TransactionDate.Should().Be(new DateOnly(2026, 11, 2));
        parsed.Rows[1].Description.Should().Be("EV KIRA ODEMESI HAVALE");
        parsed.Rows[1].SignedAmount.Should().Be(-18500.00m);

        // Verify auto-categorization maps both rows accurately
        var categories = DefaultCategoryCatalog.CreateForWorkspace(Guid.NewGuid());
        StatementAutoCategorizer.Categorize(parsed.Rows[0].Description, TransactionType.Income, categories)!.NameTr
            .Should().Be("Maaş");
        StatementAutoCategorizer.Categorize(parsed.Rows[1].Description, TransactionType.Expense, categories)!.NameTr
            .Should().Be("Kira");
    }

    [Fact]
    public void PdfParser_ShouldNormalizeSpacedDatesAndNumbers_AndRejectCustomEncodedGarbledPdfWithHelpfulMessage()
    {
        // Exact spaced-date line observed in real PDF logs
        var spacedLines = new[]
        {
            "01 .11 .2026 MAAS O DEMESI - KASIM 2026 +65.000,00 TL 83.500,00 TL",
            "02 / 11 / 2026 EV KIRA ODEMESI - 18 . 500 , 00 TL 65 . 000 , 00 TL"
        };

        var rows = BankStatementParser.ParsePdfLines(spacedLines);
        rows.Should().HaveCount(2);

        rows[0].TransactionDate.Should().Be(new DateOnly(2026, 11, 1));
        rows[0].Description.Should().Be("MAAS O DEMESI - KASIM 2026");
        rows[0].SignedAmount.Should().Be(65000.00m);

        rows[1].TransactionDate.Should().Be(new DateOnly(2026, 11, 2));
        rows[1].Description.Should().Be("EV KIRA ODEMESI");
        rows[1].SignedAmount.Should().Be(-18500.00m);

        // Garbled / custom-encoded PDF without ToUnicode CMap must return friendly Turkish Excel/CSV guidance
        var garbledStream = "BT 1 0 0 1 40 700 Tm (\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000B\u000C\u000E\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017) Tj ET";
        var garbledPdf = Encoding.Latin1.GetBytes(
            $"%PDF-1.4\n1 0 obj\n<< /Length {garbledStream.Length} >>\nstream\n{garbledStream}\nendstream\nendobj\n%%EOF");

        var act = () => BankStatementParser.Parse("garbled-bank.pdf", garbledPdf, "application/pdf");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "PDF_CUSTOM_FONT_ENCODING" &&
                        e.Message == "Bu PDF özel font kodlaması içerdiği için okunamadı. Lütfen bankanızdan Excel (.xlsx) veya CSV formatında ekstre indirin.");
    }

    [Fact]
    public void AutoCategorizer_ShouldMapTurkishMerchantsToExpectedWorkspaceCategories()
    {
        var categories = DefaultCategoryCatalog.CreateForWorkspace(Guid.NewGuid());

        StatementAutoCategorizer.Categorize("MIGROS TICARET A.S.", TransactionType.Expense, categories)!.NameTr.Should().Be("Market");
        StatementAutoCategorizer.Categorize("STARBUCKS KADIKOY", TransactionType.Expense, categories)!.NameTr.Should().Be("Yeme-İçme");
        StatementAutoCategorizer.Categorize("SPOTIFY PREMIUM", TransactionType.Expense, categories)!.NameTr.Should().Be("Abonelikler");
        StatementAutoCategorizer.Categorize("SHELL TURCAS PETROL", TransactionType.Expense, categories)!.NameTr.Should().Be("Ulaşım");
        StatementAutoCategorizer.Categorize("TURKCELL ILETISIM", TransactionType.Expense, categories)!.NameTr.Should().Be("Faturalar");
        StatementAutoCategorizer.Categorize("EV KIRA ODEMESI", TransactionType.Expense, categories)!.NameTr.Should().Be("Kira");
        StatementAutoCategorizer.Categorize("AYLIK MAAS ODEMESI", TransactionType.Income, categories)!.NameTr.Should().Be("Maaş");
    }

    [Fact]
    public async Task PreviewAndConfirmImport_ShouldFlagDuplicates_NotWriteOnPreview_AndUpdateWalletBalanceOnConfirm()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("import@finsense.app", "hash", "Import User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var categories = DefaultCategoryCatalog.CreateForWorkspace(workspace.Id);
        var marketCategory = categories.First(c => c.NameTr == "Market");

        var wallet = Wallet.Create(workspace.Id, "Vadesiz TL", WalletType.BankAccount, initialBalance: 10000m);

        // Pre-existing transaction that should be flagged as duplicate in preview
        var existingTx = Transaction.Create(
            workspace.Id,
            wallet.Id,
            TransactionType.Expense,
            1250.50m,
            new DateOnly(2026, 11, 15),
            categoryId: marketCategory.Id,
            merchantName: "MIGROS",
            createdByUserId: user.Id,
            utcNow: dateTimeProvider.UtcNow);
        wallet.ApplyExpense(1250.50m); // 8749.50

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Categories.AddRange(categories);
        dbContext.Wallets.Add(wallet);
        dbContext.Transactions.Add(existingTx);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id, Locale = "tr-TR" };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);

        var csv = """
            Tarih;Açıklama;Tutar
            15.11.2026;MIGROS MARKET;-1.250,50
            16.11.2026;STARBUCKS KAHVE;-200,00
            18.11.2026;MAAS ODEMESI;+5.000,00
            """;

        var previewHandler = new PreviewStatementImportCommandHandler(dbContext, accessService, currentUser);
        var preview = await previewHandler.Handle(
            new PreviewStatementImportCommand(workspace.Id, wallet.Id, "ekstre.csv", Encoding.UTF8.GetBytes(csv)),
            CancellationToken.None);

        preview.TotalRows.Should().Be(3);
        preview.DuplicateCount.Should().Be(1);
        preview.TotalIncome.Should().Be(5000m);
        preview.TotalExpense.Should().Be(1450.50m);
        preview.Items[0].IsDuplicate.Should().BeTrue();
        preview.Items[0].CategoryName.Should().Be("Market");
        preview.Items[1].IsDuplicate.Should().BeFalse();
        preview.Items[1].CategoryName.Should().Be("Yeme-İçme");
        preview.Items[2].IsDuplicate.Should().BeFalse();
        preview.Items[2].CategoryName.Should().Be("Maaş");

        // Preview must NOT write to DB
        (await dbContext.Transactions.CountAsync(t => t.WorkspaceId == workspace.Id)).Should().Be(1);

        // Confirm only non-duplicate items
        var approved = preview.Items
            .Where(i => !i.IsDuplicate)
            .Select(i => new ConfirmImportItemInput(i.TransactionDate, i.Description, i.Type, i.Amount, i.CategoryId!.Value))
            .ToList();

        var confirmHandler = new ConfirmStatementImportCommandHandler(dbContext, accessService, dateTimeProvider);
        var confirmResult = await confirmHandler.Handle(
            new ConfirmStatementImportCommand(workspace.Id, wallet.Id, approved),
            CancellationToken.None);

        confirmResult.ImportedCount.Should().Be(2);
        confirmResult.TotalIncome.Should().Be(5000m);
        confirmResult.TotalExpense.Should().Be(200m);
        // 8749.50 - 200 + 5000 = 13549.50
        confirmResult.UpdatedWalletBalance.Should().Be(13549.50m);

        (await dbContext.Transactions.CountAsync(t => t.WorkspaceId == workspace.Id)).Should().Be(3);
        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id))
            .CurrentBalance.Should().Be(13549.50m);
    }

    private static byte[] BuildMinimalXlsx(string[][] rows)
    {
        var sharedStrings = new List<string>();
        var ssMap = new Dictionary<string, int>();

        int GetSharedStringIndex(string val)
        {
            if (!ssMap.TryGetValue(val, out var idx))
            {
                idx = sharedStrings.Count;
                sharedStrings.Add(val);
                ssMap[val] = idx;
            }
            return idx;
        }

        var sheetSb = new StringBuilder();
        sheetSb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sheetSb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Length; r++)
        {
            sheetSb.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var colLetter = (char)('A' + c);
                var cellRef = $"{colLetter}{r + 1}";
                var val = rows[r][c];
                if (decimal.TryParse(val, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    sheetSb.Append($"<c r=\"{cellRef}\"><v>{val}</v></c>");
                }
                else
                {
                    var idx = GetSharedStringIndex(val);
                    sheetSb.Append($"<c r=\"{cellRef}\" t=\"s\"><v>{idx}</v></c>");
                }
            }
            sheetSb.Append("</row>");
        }
        sheetSb.Append("</sheetData></worksheet>");

        var ssSb = new StringBuilder();
        ssSb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        ssSb.Append("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        foreach (var s in sharedStrings)
        {
            ssSb.Append($"<si><t>{System.Security.SecurityElement.Escape(s)}</t></si>");
        }
        ssSb.Append("</sst>");

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ssEntry = archive.CreateEntry("xl/sharedStrings.xml");
            using (var w = new StreamWriter(ssEntry.Open(), Encoding.UTF8)) w.Write(ssSb.ToString());

            var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using (var w = new StreamWriter(sheetEntry.Open(), Encoding.UTF8)) w.Write(sheetSb.ToString());
        }

        return ms.ToArray();
    }
}
