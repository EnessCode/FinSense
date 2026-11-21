import {
  formatDateShort,
  formatMoney,
  formatMonthYear,
  formatPercent,
  formatQuantity,
  formatRelativeDate,
  formatSignedPercent,
  getMonthRange,
  parseDecimalInput,
  parseMoneyInput,
  shiftYearMonth,
  splitMoneyParts,
  toIsoCalendarDate,
} from '@/utils/formatters';

describe('Turkish financial formatters', () => {
  test('splitMoneyParts formats Turkish lira with dot thousands and comma decimals', () => {
    expect(splitMoneyParts(1420.75, 'TRY')).toEqual({
      sign: '',
      symbol: '₺',
      integer: '1.420',
      fraction: ',75',
      formatted: '₺1.420,75',
    });
  });

  test('splitMoneyParts handles negative and signed positive amounts', () => {
    expect(splitMoneyParts(-850.5, 'TRY').formatted).toBe('-₺850,50');
    expect(splitMoneyParts(25000, 'TRY', { showSign: true }).formatted).toBe('+₺25.000,00');
    expect(formatMoney(0, 'TRY')).toBe('₺0,00');
  });

  test('formatPercent formats Turkish and English percentages', () => {
    expect(formatPercent(13.3, 1, 'tr-TR')).toBe('%13,3');
    expect(formatPercent(83, 0, 'tr-TR')).toBe('%83');
    expect(formatPercent(13.3, 1, 'en-US')).toBe('13.3%');
    expect(formatSignedPercent(8.45, 2, 'tr-TR')).toBe('+%8,45');
    expect(formatSignedPercent(-2.1, 2, 'tr-TR')).toBe('-%2,10');
    expect(formatSignedPercent(-2.1, 2, 'en-US')).toBe('-2.10%');
  });

  test('formatDateShort formats ISO dates as DD.MM.YYYY', () => {
    expect(formatDateShort('2026-11-10')).toBe('10.11.2026');
  });

  test('formatRelativeDate returns Bugün, Dün, and short Turkish month', () => {
    const ref = new Date(2026, 10, 11); // 11 Nov 2026
    expect(formatRelativeDate('2026-11-11', 'tr-TR', ref)).toBe('Bugün');
    expect(formatRelativeDate('2026-11-10', 'tr-TR', ref)).toBe('Dün');
    expect(formatRelativeDate('2026-11-02', 'tr-TR', ref)).toBe('2 Kas');
    expect(formatRelativeDate('2026-11-11', 'en-US', ref)).toBe('Today');
  });

  test('parseMoneyInput, toIsoCalendarDate, and getMonthRange handle Turkish inputs', () => {
    expect(parseMoneyInput('1.420,75')).toBe(1420.75);
    expect(parseMoneyInput('850,5')).toBe(850.5);
    expect(parseMoneyInput('')).toBe(0);
    expect(toIsoCalendarDate(new Date(2026, 10, 11))).toBe('2026-11-11');
    expect(getMonthRange(2026, 11)).toEqual({
      dateFrom: '2026-11-01',
      dateTo: '2026-11-30',
    });
    expect(parseDecimalInput('12,5')).toBe(12.5);
    expect(parseDecimalInput('0,0525')).toBe(0.0525);
    expect(formatQuantity(15)).toBe('15,00');
    expect(formatQuantity(0.0525)).toBe('0,0525');
  });

  test('formatMonthYear and shiftYearMonth handle Turkish/English labels and year rollovers', () => {
    expect(formatMonthYear(2026, 11, 'tr-TR')).toBe('Kasım 2026');
    expect(formatMonthYear(2026, 11, 'en-US')).toBe('November 2026');
    expect(shiftYearMonth(2026, 12, 1)).toEqual({ year: 2027, month: 1 });
    expect(shiftYearMonth(2026, 1, -1)).toEqual({ year: 2025, month: 12 });
    expect(shiftYearMonth(2026, 11, 3)).toEqual({ year: 2027, month: 2 });
  });
});
