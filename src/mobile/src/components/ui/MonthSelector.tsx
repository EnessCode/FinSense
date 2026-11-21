import { Calendar, ChevronLeft, ChevronRight } from 'lucide-react-native';
import React from 'react';
import { useTranslation } from 'react-i18next';
import {
  Pressable,
  StyleSheet,
  Text,
  View,
  type StyleProp,
  type ViewStyle,
} from 'react-native';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { getActiveLocale } from '@/i18n';
import { radii, spacing } from '@/theme/tokens';
import { formatMonthYear, shiftYearMonth } from '@/utils/formatters';

export interface MonthSelectorProps {
  year: number;
  month: number;
  onChange: (next: { year: number; month: number }) => void;
  style?: StyleProp<ViewStyle>;
}

export function MonthSelector({
  year,
  month,
  onChange,
  style,
}: MonthSelectorProps) {
  const { t } = useTranslation();
  const { colors: themeColors, isDark } = useTheme();
  const { activeTheme } = useWorkspace();
  const locale = getActiveLocale();

  const accentTint = isDark
    ? activeTheme.mode === 'Household'
      ? '#FBBF24'
      : '#34D399'
    : activeTheme.primarySurface;

  const label = formatMonthYear(year, month, locale);

  return (
    <View
      style={[
        styles.container,
        {
          backgroundColor: themeColors.surface,
          borderColor: themeColors.border,
        },
        style,
      ]}
    >
      <Pressable
        onPress={() => onChange(shiftYearMonth(year, month, -1))}
        hitSlop={8}
        accessibilityRole="button"
        accessibilityLabel={t('common.previousMonth')}
        style={({ pressed }) => [
          styles.navButton,
          {
            backgroundColor: pressed
              ? themeColors.surfaceSubtle
              : 'transparent',
          },
        ]}
      >
        <ChevronLeft size={18} color={themeColors.ink} strokeWidth={1.85} />
      </Pressable>

      <View style={styles.labelWrap}>
        <Calendar size={14} color={accentTint} strokeWidth={1.85} />
        <Text
          style={[styles.labelText, { color: themeColors.ink }]}
          numberOfLines={1}
        >
          {label}
        </Text>
      </View>

      <Pressable
        onPress={() => onChange(shiftYearMonth(year, month, 1))}
        hitSlop={8}
        accessibilityRole="button"
        accessibilityLabel={t('common.nextMonth')}
        style={({ pressed }) => [
          styles.navButton,
          {
            backgroundColor: pressed
              ? themeColors.surfaceSubtle
              : 'transparent',
          },
        ]}
      >
        <ChevronRight size={18} color={themeColors.ink} strokeWidth={1.85} />
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    height: 40,
    borderRadius: radii.full,
    borderWidth: 1,
    paddingHorizontal: 4,
  },
  navButton: {
    width: 32,
    height: 32,
    borderRadius: radii.full,
    alignItems: 'center',
    justifyContent: 'center',
  },
  labelWrap: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.xs + 2,
    flex: 1,
  },
  labelText: {
    fontSize: 13.5,
    fontWeight: '600',
    letterSpacing: -0.1,
  },
});
