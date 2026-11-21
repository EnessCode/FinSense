import { ArrowDownLeft, ArrowUpRight, Sparkles } from 'lucide-react-native';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, Text, View } from 'react-native';
import { MoneyText } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { radii, spacing, typography } from '@/theme/tokens';
import { formatPercent } from '@/utils/formatters';
import { getActiveLocale } from '@/i18n';

export interface NetBalanceCardProps {
  totalBalance: number;
  monthlyIncome: number;
  monthlyExpense: number;
  savingsRatePercent: number;
  currency?: string;
}

export function NetBalanceCard({
  totalBalance,
  monthlyIncome,
  monthlyExpense,
  savingsRatePercent,
  currency = 'TRY',
}: NetBalanceCardProps) {
  const { t } = useTranslation();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { isDark } = useTheme();
  const isHousehold = activeWorkspace?.type === 'Household';
  const locale = getActiveLocale();

  const heroBg = isDark
    ? isHousehold
      ? '#3D2214'
      : '#0E4D3A'
    : activeTheme.primarySurface;
  const heroBorder = isDark
    ? isHousehold
      ? 'rgba(251, 191, 36, 0.28)'
      : 'rgba(52, 211, 153, 0.28)'
    : activeTheme.heroBorder;

  return (
    <View
      style={[
        styles.heroCard,
        {
          backgroundColor: heroBg,
          borderColor: heroBorder,
        },
      ]}
    >
      <View style={styles.topRow}>
        <Text style={[styles.overline, { color: activeTheme.heroMutedInk }]}>
          {t('dashboard.totalNetBalance')}
        </Text>

        <View
          style={[
            styles.modeBadge,
            {
              backgroundColor: activeTheme.heroPillBg,
              borderColor: activeTheme.heroBorder,
            },
          ]}
        >
          <View
            style={[
              styles.modeDot,
              { backgroundColor: activeTheme.accentHighlight },
            ]}
          />
          <Text style={[styles.modeText, { color: activeTheme.heroInk }]}>
            {isHousehold
              ? t('workspace.householdModeBadge')
              : t('workspace.personalModeBadge')}
          </Text>
        </View>
      </View>

      <View style={styles.balanceBlock}>
        <MoneyText
          amount={totalBalance}
          currency={currency}
          size="hero"
          color={activeTheme.heroInk}
          fractionOpacity={0.68}
        />
      </View>

      <View style={[styles.divider, { backgroundColor: activeTheme.heroBorder }]} />

      <View style={styles.metricsRow}>
        <View
          style={[
            styles.metricPill,
            {
              backgroundColor: activeTheme.heroPillBg,
              borderColor: activeTheme.heroBorder,
            },
          ]}
        >
          <View style={styles.metricHeader}>
            <ArrowDownLeft size={14} color="#6EE7B7" strokeWidth={1.75} />
            <Text style={[styles.metricLabel, { color: activeTheme.heroMutedInk }]}>
              {t('dashboard.monthlyIncome')}
            </Text>
          </View>
          <MoneyText
            amount={monthlyIncome}
            currency={currency}
            size="md"
            color={activeTheme.heroInk}
          />
        </View>

        <View
          style={[
            styles.metricPill,
            {
              backgroundColor: activeTheme.heroPillBg,
              borderColor: activeTheme.heroBorder,
            },
          ]}
        >
          <View style={styles.metricHeader}>
            <ArrowUpRight size={14} color="#FCA5A5" strokeWidth={1.75} />
            <Text style={[styles.metricLabel, { color: activeTheme.heroMutedInk }]}>
              {t('dashboard.monthlyExpense')}
            </Text>
          </View>
          <MoneyText
            amount={monthlyExpense}
            currency={currency}
            size="md"
            color={activeTheme.heroInk}
          />
        </View>
      </View>

      <View style={styles.savingsRow}>
        <View style={styles.savingsLabelRow}>
          <Sparkles size={13} color={activeTheme.accentHighlight} strokeWidth={1.75} />
          <Text style={[styles.savingsLabel, { color: activeTheme.heroMutedInk }]}>
            {t('dashboard.savingsRate')}
          </Text>
        </View>
        <Text
          style={[
            styles.savingsValue,
            typography.tabularNums,
            { color: activeTheme.heroInk },
          ]}
        >
          {formatPercent(savingsRatePercent, 1, locale)}
        </Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  heroCard: {
    borderRadius: radii.xl,
    borderWidth: 1,
    padding: spacing.xl,
  },
  topRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: spacing.sm,
  },
  overline: {
    fontSize: 12,
    fontWeight: '600',
    letterSpacing: 0.4,
    textTransform: 'uppercase',
  },
  modeBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: radii.full,
    borderWidth: 1,
    gap: 6,
  },
  modeDot: {
    width: 6,
    height: 6,
    borderRadius: radii.full,
  },
  modeText: {
    fontSize: 10,
    fontWeight: '700',
    letterSpacing: 0.8,
  },
  balanceBlock: {
    marginTop: spacing.xs,
    marginBottom: spacing.lg,
  },
  divider: {
    height: 1,
    marginBottom: spacing.base,
  },
  metricsRow: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  metricPill: {
    flex: 1,
    borderRadius: radii.md,
    borderWidth: 1,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.md,
    gap: 4,
  },
  metricHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
  },
  metricLabel: {
    fontSize: 12,
    fontWeight: '500',
  },
  savingsRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginTop: spacing.md,
    paddingTop: spacing.sm,
  },
  savingsLabelRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  savingsLabel: {
    fontSize: 12.5,
    fontWeight: '500',
  },
  savingsValue: {
    fontSize: 13.5,
    fontWeight: '700',
  },
});
