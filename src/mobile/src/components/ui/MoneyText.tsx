import React from 'react';
import { StyleSheet, Text, type StyleProp, type TextStyle } from 'react-native';
import { useTheme } from '@/context/ThemeContext';
import { typography } from '@/theme/tokens';
import { splitMoneyParts } from '@/utils/formatters';

export type MoneyTextSize = 'sm' | 'md' | 'lg' | 'xl' | 'hero';

export interface MoneyTextProps {
  amount: number;
  currency?: string;
  size?: MoneyTextSize;
  color?: string;
  showSign?: boolean;
  explicitPositive?: boolean;
  fractionOpacity?: number;
  style?: StyleProp<TextStyle>;
  numberOfLines?: number;
}

const SIZE_MAP: Record<MoneyTextSize, { fontSize: number; lineHeight: number; fontWeight: TextStyle['fontWeight'] }> = {
  sm: { fontSize: 13, lineHeight: 18, fontWeight: '600' },
  md: { fontSize: 15, lineHeight: 20, fontWeight: '600' },
  lg: { fontSize: 18, lineHeight: 24, fontWeight: '700' },
  xl: { fontSize: 24, lineHeight: 30, fontWeight: '700' },
  hero: { fontSize: 36, lineHeight: 42, fontWeight: '700' },
};

export function MoneyText({
  amount,
  currency = 'TRY',
  size = 'md',
  color,
  showSign = false,
  explicitPositive = false,
  fractionOpacity = 0.7,
  style,
  numberOfLines = 1,
}: MoneyTextProps) {
  const { colors } = useTheme();
  const resolvedColor = color ?? colors.ink;
  const parts = splitMoneyParts(amount, currency, { showSign, explicitPositive });
  const metrics = SIZE_MAP[size];
  const fractionFontSize = Math.round(metrics.fontSize * 0.75 * 10) / 10;

  return (
    <Text
      numberOfLines={numberOfLines}
      adjustsFontSizeToFit={size === 'hero'}
      minimumFontScale={0.78}
      style={[
        styles.base,
        typography.tabularNums,
        {
          fontSize: metrics.fontSize,
          lineHeight: metrics.lineHeight,
          fontWeight: metrics.fontWeight,
          color: resolvedColor,
        },
        style,
      ]}
    >
      {parts.sign}
      {parts.symbol}
      {parts.integer}
      <Text
        style={[
          typography.tabularNums,
          {
            fontSize: fractionFontSize,
            fontWeight: '600',
            color: resolvedColor,
            opacity: fractionOpacity,
          },
        ]}
      >
        {parts.fraction}
      </Text>
    </Text>
  );
}

const styles = StyleSheet.create({
  base: {
    letterSpacing: -0.4,
  },
});
