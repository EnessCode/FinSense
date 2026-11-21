import React from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, Text, View } from 'react-native';
import type { BudgetDto, BudgetStatus } from '@/api/types';
import { SurfaceCard } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { getActiveLocale } from '@/i18n';
import { colors, radii, spacing, typography, type ColorPalette } from '@/theme/tokens';
import { formatMoney, formatPercent } from '@/utils/formatters';

export interface BudgetStatusCardProps {
  budgets: BudgetDto[];
}

function getStatusColor(
  status: BudgetStatus,
  fallbackAccent: string,
  palette: ColorPalette,
): string {
  switch (status) {
    case 'Exceeded':
      return palette.expense;
    case 'Warning':
      return palette.warning;
    case 'Normal':
    default:
      return fallbackAccent;
  }
}

export function BudgetStatusCard({ budgets }: BudgetStatusCardProps) {
  const { t } = useTranslation();
  const { activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();
  const locale = getActiveLocale();

  // Prioritize the overall workspace budget, plus up to 2 highest-utilization category budgets.
  const overall = budgets.find((b) => b.categoryId === null);
  const categoryBudgets = budgets
    .filter((b) => b.categoryId !== null)
    .sort((a, b) => b.utilizationPercent - a.utilizationPercent);

  const visibleBudgets = overall
    ? [overall, ...categoryBudgets.slice(0, 1)]
    : categoryBudgets.slice(0, 2);

  const normalAccent = isDark
    ? activeTheme.mode === 'Household'
      ? '#FBBF24'
      : '#10B981'
    : activeTheme.primarySurface;

  return (
    <View style={styles.section}>
      <View style={styles.headerRow}>
        <Text style={[styles.sectionTitle, { color: themeColors.ink }]}>
          {t('dashboard.budgetSummaryTitle')}
        </Text>
      </View>

      <SurfaceCard
        padding="lg"
        variant={visibleBudgets.length === 0 && isDark ? 'subtle' : 'default'}
      >
        {visibleBudgets.length === 0 ? (
          <Text style={[styles.emptyText, { color: themeColors.inkSecondary }]}>
            {t('dashboard.budgetSummaryEmpty')}
          </Text>
        ) : (
          <View style={styles.list}>
            {visibleBudgets.map((budget, index) => {
              const clampedWidth = Math.min(Math.max(budget.utilizationPercent, 0), 100);
              const barColor = getStatusColor(
                budget.status,
                normalAccent,
                themeColors,
              );
              const label = budget.categoryName ?? t('dashboard.budgetOverallLabel');
              const isExceeded = budget.remainingAmount < 0;

              return (
                <View
                  key={budget.id}
                  style={[
                    styles.budgetItem,
                    index > 0 && [
                      styles.budgetItemBorder,
                      { borderTopColor: themeColors.cardBorder },
                    ],
                  ]}
                >
                  <View style={styles.budgetTopRow}>
                    <Text
                      style={[styles.budgetName, { color: themeColors.ink }]}
                      numberOfLines={1}
                    >
                      {label}
                    </Text>
                    <Text
                      style={[
                        styles.budgetPercent,
                        typography.tabularNums,
                        { color: barColor },
                      ]}
                    >
                      {formatPercent(budget.utilizationPercent, 0, locale)}
                    </Text>
                  </View>

                  <View
                    style={[
                      styles.progressTrack,
                      { backgroundColor: themeColors.surfaceSubtle },
                    ]}
                  >
                    <View
                      style={[
                        styles.progressFill,
                        {
                          width: `${clampedWidth}%`,
                          backgroundColor: barColor,
                        },
                      ]}
                    />
                  </View>

                  <View style={styles.budgetBottomRow}>
                    <Text
                      style={[
                        styles.budgetMeta,
                        typography.tabularNums,
                        { color: themeColors.inkSecondary },
                      ]}
                    >
                      {formatMoney(budget.spentAmount, budget.currencyCode)} /{' '}
                      {formatMoney(budget.limitAmount, budget.currencyCode)}
                    </Text>

                    <Text
                      style={[
                        styles.budgetRemaining,
                        typography.tabularNums,
                        {
                          color: isExceeded
                            ? themeColors.expense
                            : themeColors.ink,
                        },
                      ]}
                    >
                      {isExceeded
                        ? t('dashboard.budgetExceeded', {
                            amount: formatMoney(
                              Math.abs(budget.remainingAmount),
                              budget.currencyCode,
                            ),
                          })
                        : t('dashboard.budgetRemaining', {
                            amount: formatMoney(
                              budget.remainingAmount,
                              budget.currencyCode,
                            ),
                          })}
                    </Text>
                  </View>
                </View>
              );
            })}
          </View>
        )}
      </SurfaceCard>
    </View>
  );
}

const styles = StyleSheet.create({
  section: {
    paddingHorizontal: spacing.xl,
    gap: spacing.md,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  sectionTitle: {
    ...typography.h3,
  },
  emptyText: {
    ...typography.caption,
  },
  list: {
    gap: spacing.base,
  },
  budgetItem: {
    gap: spacing.sm,
  },
  budgetItemBorder: {
    paddingTop: spacing.base,
    borderTopWidth: 1,
    borderTopColor: colors.cardBorder,
  },
  budgetTopRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  budgetName: {
    ...typography.bodyMedium,
    flex: 1,
  },
  budgetPercent: {
    fontSize: 13.5,
    fontWeight: '700',
  },
  progressTrack: {
    height: 7,
    borderRadius: radii.full,
    backgroundColor: colors.surfaceSubtle,
    overflow: 'hidden',
  },
  progressFill: {
    height: '100%',
    borderRadius: radii.full,
  },
  budgetBottomRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  budgetMeta: {
    ...typography.caption,
    fontSize: 12.5,
  },
  budgetRemaining: {
    ...typography.caption,
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.ink,
  },
});
