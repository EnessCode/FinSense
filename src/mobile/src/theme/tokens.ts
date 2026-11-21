import { Platform, type TextStyle } from 'react-native';

export type WorkspaceAmbientMode = 'Personal' | 'Household';

export const lightColors = {
  // Base canvas & surfaces (Editorial Quiet Luxury)
  canvas: '#F7F6F2',
  surface: '#FFFFFF',
  surfaceSubtle: '#F1EFE9',
  surfaceMuted: '#EDEBE3',

  // Hairline borders (no heavy drop shadows)
  border: '#E6E4DD',
  cardBorder: '#E8E6DF',
  darkBorder: '#22252A',

  // Ink hierarchy
  ink: '#111315',
  inkSecondary: '#686B6E',
  inkSubtle: '#9A9D9F',
  inkOnDark: '#FFFFFF',
  inkOnDarkMuted: 'rgba(255, 255, 255, 0.72)',

  // Semantic financial colors
  income: '#15803D',
  incomeSoft: '#DCFCE7',

  expense: '#B91C1C',
  expenseSoft: '#FEE2E2',
  expenseRowInk: '#111315',

  warning: '#B45309',
  warningSoft: '#FEF3C7',

  transfer: '#475569',
  transferSoft: '#F1F5F9',
} as const;

export type ColorPalette = {
  [K in keyof typeof lightColors]: string;
};

export const darkColors: ColorPalette = {
  // Base canvas & surfaces (Dark Slate / Obsidian)
  canvas: '#0B0F17',
  surface: '#131B29',
  surfaceSubtle: '#182234',
  surfaceMuted: '#1E293B',

  // Hairline borders
  border: 'rgba(148, 163, 184, 0.14)',
  cardBorder: 'rgba(255, 255, 255, 0.08)',
  darkBorder: 'rgba(255, 255, 255, 0.07)',

  // Ink hierarchy
  ink: '#F8FAFC',
  inkSecondary: '#94A3B8',
  inkSubtle: '#94A3B8',
  inkOnDark: '#FFFFFF',
  inkOnDarkMuted: 'rgba(255, 255, 255, 0.78)',

  // Semantic financial colors
  income: '#34D399',
  incomeSoft: 'rgba(16, 185, 129, 0.14)',

  expense: '#F87171',
  expenseSoft: 'rgba(248, 113, 113, 0.14)',
  expenseRowInk: '#F87171',

  warning: '#FBBF24',
  warningSoft: 'rgba(251, 191, 36, 0.14)',

  transfer: '#94A3B8',
  transferSoft: 'rgba(148, 163, 184, 0.14)',
};

export const colors = lightColors;

export interface WorkspaceTheme {
  mode: WorkspaceAmbientMode;
  primarySurface: string;
  primarySurfacePressed: string;
  accentHighlight: string;
  softTint: string;
  softTintBorder: string;
  heroInk: string;
  heroMutedInk: string;
  heroBorder: string;
  heroPillBg: string;
}

export const workspaceThemes: Record<WorkspaceAmbientMode, WorkspaceTheme> = {
  Personal: {
    mode: 'Personal',
    primarySurface: '#0D2B22',
    primarySurfacePressed: '#081E17',
    accentHighlight: '#10B981',
    softTint: '#EAF5F0',
    softTintBorder: '#CBE6DA',
    heroInk: '#FFFFFF',
    heroMutedInk: 'rgba(255, 255, 255, 0.72)',
    heroBorder: 'rgba(255, 255, 255, 0.14)',
    heroPillBg: 'rgba(255, 255, 255, 0.10)',
  },
  Household: {
    mode: 'Household',
    primarySurface: '#2C1A12',
    primarySurfacePressed: '#1E110B',
    accentHighlight: '#D97706',
    softTint: '#FDF3E7',
    softTintBorder: '#F5DEC0',
    heroInk: '#FFFFFF',
    heroMutedInk: 'rgba(255, 255, 255, 0.72)',
    heroBorder: 'rgba(255, 255, 255, 0.14)',
    heroPillBg: 'rgba(255, 255, 255, 0.10)',
  },
};

export const spacing = {
  xs: 4,
  sm: 8,
  md: 12,
  base: 16,
  lg: 20,
  xl: 24,
  xxl: 32,
  xxxl: 40,
} as const;

export const radii = {
  sm: 10,
  md: 14,
  lg: 20,
  xl: 28,
  full: 9999,
} as const;

const editorialSerif = Platform.select({
  ios: 'Georgia',
  android: 'serif',
  default: 'Georgia',
});

export const typography = {
  editorialDisplay: {
    fontFamily: editorialSerif,
    fontSize: 30,
    lineHeight: 36,
    fontWeight: '600',
    letterSpacing: -0.6,
    color: colors.ink,
  } satisfies TextStyle,

  h1: {
    fontSize: 26,
    lineHeight: 32,
    fontWeight: '700',
    letterSpacing: -0.5,
    color: colors.ink,
  } satisfies TextStyle,

  h2: {
    fontSize: 20,
    lineHeight: 26,
    fontWeight: '600',
    letterSpacing: -0.3,
    color: colors.ink,
  } satisfies TextStyle,

  h3: {
    fontSize: 16,
    lineHeight: 22,
    fontWeight: '600',
    letterSpacing: -0.2,
    color: colors.ink,
  } satisfies TextStyle,

  body: {
    fontSize: 15,
    lineHeight: 21,
    fontWeight: '400',
    color: colors.ink,
  } satisfies TextStyle,

  bodyMedium: {
    fontSize: 15,
    lineHeight: 21,
    fontWeight: '500',
    color: colors.ink,
  } satisfies TextStyle,

  caption: {
    fontSize: 13,
    lineHeight: 18,
    fontWeight: '400',
    color: colors.inkSecondary,
  } satisfies TextStyle,

  overline: {
    fontSize: 11,
    lineHeight: 15,
    fontWeight: '600',
    letterSpacing: 0.9,
    textTransform: 'uppercase',
    color: colors.inkSubtle,
  } satisfies TextStyle,

  tabularNums: {
    fontVariant: ['tabular-nums'],
  } satisfies TextStyle,
} as const;
