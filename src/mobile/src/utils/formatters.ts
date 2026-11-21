export type SupportedLocale = 'tr-TR' | 'en-US';

export interface SplitMoneyParts {
  sign: '' | '+' | '-';
  symbol: string;
  integer: string;
  fraction: string;
  formatted: string;
}

export interface FormatMoneyOptions {
  showSign?: boolean;
  explicitPositive?: boolean;
}

const CURRENCY_SYMBOLS: Record<string, string> = {
  TRY: '₺',
  USD: '$',
  EUR: '€',
  GBP: '£',
};

const TR_MONTHS_SHORT = [
  'Oca',
  'Şub',
  'Mar',
  'Nis',
  'May',
  'Haz',
  'Tem',
  'Ağu',
  'Eyl',
  'Eki',
  'Kas',
  'Ara',
] as const;

const EN_MONTHS_SHORT = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
] as const;

const TR_MONTHS_LONG = [
  'Ocak',
  'Şubat',
  'Mart',
  'Nisan',
  'Mayıs',
  'Haziran',
  'Temmuz',
  'Ağustos',
  'Eylül',
  'Ekim',
  'Kasım',
  'Aralık',
] as const;

const EN_MONTHS_LONG = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
] as const;

export function getCurrencySymbol(currencyCode: string = 'TRY'): string {
  const normalized = currencyCode.trim().toUpperCase();
  return CURRENCY_SYMBOLS[normalized] ?? `${normalized} `;
}

/**
 * Splits a numeric monetary amount into Turkish-formatted integer and kuruş parts
 * (`1420.75` -> `{ symbol: '₺', integer: '1.420', fraction: ',75' }`).
 * Deterministic across JSC, Hermes, and V8 without Intl locale inconsistencies.
 */
export function splitMoneyParts(
  amount: number,
  currency: string = 'TRY',
  options?: FormatMoneyOptions,
): SplitMoneyParts {
  const safeAmount = Number.isFinite(amount) ? amount : 0;
  const isNegative = safeAmount < 0;
  const absRounded = Math.round(Math.abs(safeAmount) * 100);

  const wholePart = Math.floor(absRounded / 100);
  const kurusPart = absRounded % 100;

  const integerFormatted = wholePart
    .toString()
    .replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  const fractionFormatted = `,${kurusPart.toString().padStart(2, '0')}`;

  let sign: '' | '+' | '-' = '';
  if (isNegative) {
    sign = '-';
  } else if (options?.showSign && (absRounded > 0 || options?.explicitPositive)) {
    sign = '+';
  }

  const symbol = getCurrencySymbol(currency);
  const formatted = `${sign}${symbol}${integerFormatted}${fractionFormatted}`;

  return {
    sign,
    symbol,
    integer: integerFormatted,
    fraction: fractionFormatted,
    formatted,
  };
}

export function formatMoney(
  amount: number,
  currency: string = 'TRY',
  options?: FormatMoneyOptions,
): string {
  return splitMoneyParts(amount, currency, options).formatted;
}

export function formatPercent(
  value: number,
  decimals: number = 1,
  locale: SupportedLocale = 'tr-TR',
): string {
  const safeValue = Number.isFinite(value) ? value : 0;
  const fixed = safeValue.toFixed(decimals);
  if (locale === 'tr-TR') {
    return `%${fixed.replace('.', ',')}`;
  }
  return `${fixed}%`;
}

/**
 * Formats a signed percentage such as `+%8,45` or `-%2,10` (tr-TR) / `+8.45%` or `-2.10%` (en-US).
 */
export function formatSignedPercent(
  value: number,
  decimals: number = 2,
  locale: SupportedLocale = 'tr-TR',
): string {
  const safeValue = Number.isFinite(value) ? value : 0;
  const sign = safeValue > 0 ? '+' : safeValue < 0 ? '-' : '';
  return `${sign}${formatPercent(Math.abs(safeValue), decimals, locale)}`;
}

/**
 * Parses an ISO calendar date (`YYYY-MM-DD`) or ISO timestamp into a local Date
 * without UTC-midnight day-shift issues.
 */
function parseLocalCalendarDate(dateInput: string): Date | null {
  const datePart = dateInput.slice(0, 10);
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(datePart);
  if (match) {
    const year = Number(match[1]);
    const month = Number(match[2]);
    const day = Number(match[3]);
    return new Date(year, month - 1, day);
  }

  const fallback = new Date(dateInput);
  return Number.isNaN(fallback.getTime()) ? null : fallback;
}

/**
 * Formats an ISO date (`2026-11-10`) as Turkish `DD.MM.YYYY` (`10.11.2026`).
 */
export function formatDateShort(dateInput: string): string {
  const parsed = parseLocalCalendarDate(dateInput);
  if (!parsed) {
    return dateInput;
  }

  const day = parsed.getDate().toString().padStart(2, '0');
  const month = (parsed.getMonth() + 1).toString().padStart(2, '0');
  const year = parsed.getFullYear();
  return `${day}.${month}.${year}`;
}

/**
 * Formats an ISO date as relative Turkish ("Bugün", "Dün", "10 Kas") or English ("Today", "Yesterday", "Nov 10").
 */
export function formatRelativeDate(
  dateInput: string,
  locale: SupportedLocale = 'tr-TR',
  referenceDate: Date = new Date(),
): string {
  const parsed = parseLocalCalendarDate(dateInput);
  if (!parsed) {
    return dateInput;
  }

  const today = new Date(
    referenceDate.getFullYear(),
    referenceDate.getMonth(),
    referenceDate.getDate(),
  );
  const target = new Date(parsed.getFullYear(), parsed.getMonth(), parsed.getDate());
  const diffDays = Math.round((today.getTime() - target.getTime()) / 86_400_000);

  if (diffDays === 0) {
    return locale === 'en-US' ? 'Today' : 'Bugün';
  }
  if (diffDays === 1) {
    return locale === 'en-US' ? 'Yesterday' : 'Dün';
  }

  const day = target.getDate();
  const monthIndex = target.getMonth();

  if (locale === 'en-US') {
    const monthName = EN_MONTHS_SHORT[monthIndex] ?? '';
    return `${monthName} ${day}`;
  }

  const monthName = TR_MONTHS_SHORT[monthIndex] ?? '';
  return `${day} ${monthName}`;
}

/**
 * Formats a Date object as local calendar ISO date (`YYYY-MM-DD`) without UTC shift.
 */
export function toIsoCalendarDate(date: Date = new Date()): string {
  const year = date.getFullYear();
  const month = (date.getMonth() + 1).toString().padStart(2, '0');
  const day = date.getDate().toString().padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/**
 * Parses a Turkish or standard decimal input string (e.g. "1.420,75" or "1420,75" or "1420.75")
 * into a rounded 2-decimal number. Returns 0 for invalid/empty input.
 */
export function parseMoneyInput(raw: string): number {
  const cleaned = raw.trim().replace(/\s+/g, '');
  if (!cleaned) {
    return 0;
  }

  // If comma is present, treat dots as thousands separators and comma as decimal point.
  const normalized = cleaned.includes(',')
    ? cleaned.replace(/\./g, '').replace(',', '.')
    : cleaned;

  const parsed = Number(normalized);
  if (!Number.isFinite(parsed)) {
    return 0;
  }
  return Math.round(parsed * 100) / 100;
}

/**
 * Returns ISO `dateFrom` and `dateTo` bounds (`YYYY-MM-DD`) for a given calendar month.
 */
export function getMonthRange(
  year: number,
  month: number,
): { dateFrom: string; dateTo: string } {
  const start = new Date(year, month - 1, 1);
  const end = new Date(year, month, 0);
  return {
    dateFrom: toIsoCalendarDate(start),
    dateTo: toIsoCalendarDate(end),
  };
}

/**
 * Formats a calendar `(year, month)` as localized `"Kasım 2026"` (tr-TR) or `"November 2026"` (en-US).
 */
export function formatMonthYear(
  year: number,
  month: number,
  locale: SupportedLocale = 'tr-TR',
): string {
  const monthIdx = ((month - 1) % 12 + 12) % 12;
  const names = locale === 'en-US' ? EN_MONTHS_LONG : TR_MONTHS_LONG;
  return `${names[monthIdx]} ${year}`;
}

/**
 * Shifts a `(year, month)` pair by `deltaMonths` (positive or negative), handling year rollovers.
 */
export function shiftYearMonth(
  year: number,
  month: number,
  deltaMonths: number,
): { year: number; month: number } {
  const totalMonths = year * 12 + (month - 1) + deltaMonths;
  const nextYear = Math.floor(totalMonths / 12);
  const nextMonth = ((totalMonths % 12) + 12) % 12 + 1;
  return { year: nextYear, month: nextMonth };
}

/**
 * Parses a decimal quantity string (e.g. "12,5" or "0,0525") supporting up to 4 decimals.
 */
export function parseDecimalInput(raw: string): number {
  const cleaned = raw.trim().replace(/\s+/g, '');
  if (!cleaned) {
    return 0;
  }

  const normalized = cleaned.includes(',')
    ? cleaned.replace(/\./g, '').replace(',', '.')
    : cleaned;

  const parsed = Number(normalized);
  if (!Number.isFinite(parsed)) {
    return 0;
  }
  return parsed;
}

/**
 * Formats an investment quantity with Turkish dot/comma separators (2 to 4 decimals).
 */
export function formatQuantity(quantity: number): string {
  const safe = Number.isFinite(quantity) ? quantity : 0;
  const fixed4 = safe.toFixed(4);
  const [whole = '0', frac4 = '0000'] = fixed4.split('.');
  const trimmedFrac = frac4.replace(/0+$/, '');
  const finalFrac = trimmedFrac.length <= 2 ? frac4.slice(0, 2) : trimmedFrac;
  const wholeFormatted = whole.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return `${wholeFormatted},${finalFrac}`;
}
