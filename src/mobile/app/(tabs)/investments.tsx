import { useFocusEffect } from 'expo-router';
import {
  AlertCircle,
  Plus,
  RefreshCw,
  TrendingDown,
  TrendingUp,
} from 'lucide-react-native';
import React, { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ActivityIndicator,
  Pressable,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { toApiError } from '@/api/client';
import { investmentsApi } from '@/api/endpoints';
import type { InvestmentPortfolioDto } from '@/api/types';
import { AddInvestmentModal } from '@/components/investments/AddInvestmentModal';
import { HoldingCard } from '@/components/investments/HoldingCard';
import { CATEGORY_COLORS } from '@/components/investments/investmentTheme';
import {
  AppButton,
  MoneyText,
  SurfaceCard,
  WorkspacePillSwitcher,
} from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { getActiveLocale } from '@/i18n';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import {
  formatMoney,
  formatPercent,
  formatSignedPercent,
} from '@/utils/formatters';

type FilterKey = 'all' | 'Gold' | 'Bist' | 'Crypto' | 'ForexAndFund';
const FILTER_KEYS: FilterKey[] = [
  'all',
  'Gold',
  'Bist',
  'Crypto',
  'ForexAndFund',
];

export default function InvestmentsScreen() {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();
  const locale = getActiveLocale();

  const workspaceId = activeWorkspace?.id;
  const currencyCode = activeWorkspace?.defaultCurrency ?? 'TRY';
  const canContribute = activeWorkspace?.currentUserRole !== 'Viewer';

  const [portfolio, setPortfolio] = useState<InvestmentPortfolioDto | null>(null);
  const [filter, setFilter] = useState<FilterKey>('all');
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [addModalOpen, setAddModalOpen] = useState(false);

  const loadPortfolio = useCallback(
    async (mode: 'initial' | 'refresh' = 'initial') => {
      if (!workspaceId) {
        setIsLoading(false);
        return;
      }

      if (mode === 'initial') setIsLoading(true);
      if (mode === 'refresh') setIsRefreshing(true);
      setErrorMessage(null);

      try {
        const res = await investmentsApi.getPortfolio(workspaceId);
        setPortfolio(res);
      } catch (err) {
        setErrorMessage(toApiError(err).message);
      } finally {
        setIsLoading(false);
        setIsRefreshing(false);
      }
    },
    [workspaceId],
  );

  useFocusEffect(
    useCallback(() => {
      void loadPortfolio('initial');
    }, [loadPortfolio]),
  );

  const handleDeleteLot = async (lotId: string) => {
    if (!workspaceId) return;
    setErrorMessage(null);
    try {
      await investmentsApi.deleteLot(workspaceId, lotId);
      await loadPortfolio('refresh');
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    }
  };

  const filteredHoldings = useMemo(() => {
    const all = portfolio?.holdings ?? [];
    if (filter === 'all') return all;
    if (filter === 'ForexAndFund') {
      return all.filter((h) => h.category === 'Forex' || h.category === 'Fund');
    }
    return all.filter((h) => h.category === filter);
  }, [portfolio?.holdings, filter]);

  const totalValue = portfolio?.totalCurrentValue ?? 0;
  const totalCost = portfolio?.totalInvestedCost ?? 0;
  const totalPnl = portfolio?.totalProfitLossAmount ?? 0;
  const totalPnlPct = portfolio?.totalProfitLossPercent ?? 0;
  const isNetProfit = totalPnl >= 0;

  return (
    <View
      style={[
        styles.screen,
        {
          backgroundColor: themeColors.canvas,
          paddingTop: Math.max(insets.top, spacing.base) + spacing.xs,
        },
      ]}
    >
      {/* Top Header */}
      <View style={styles.topHeader}>
        <Text style={[styles.screenTitle, { color: themeColors.ink }]}>
          {t('investments.screenTitle')}
        </Text>
        <WorkspacePillSwitcher />
      </View>

      {errorMessage ? (
        <View style={styles.errorBanner}>
          <AlertCircle size={16} color={colors.expense} strokeWidth={1.75} />
          <Text style={styles.errorText}>{errorMessage}</Text>
          <Pressable onPress={() => void loadPortfolio('initial')} hitSlop={8}>
            <Text style={styles.retryText}>{t('common.retry')}</Text>
          </Pressable>
        </View>
      ) : null}

      {isLoading && !isRefreshing ? (
        <View style={styles.loadingWrap}>
          <ActivityIndicator size="large" color={activeTheme.primarySurface} />
        </View>
      ) : (
        <ScrollView
          showsVerticalScrollIndicator={false}
          contentContainerStyle={styles.scrollContent}
          refreshControl={
            <RefreshControl
              refreshing={isRefreshing}
              onRefresh={() => void loadPortfolio('refresh')}
              tintColor={activeTheme.primarySurface}
            />
          }
        >
          {/* Hero Wealth Vault Card */}
          <View
            style={[
              styles.vaultCard,
              {
                backgroundColor: isDark
                  ? activeTheme.mode === 'Household'
                    ? '#3D2214'
                    : '#0E4D3A'
                  : activeTheme.primarySurface,
                borderColor: isDark
                  ? activeTheme.mode === 'Household'
                    ? 'rgba(251, 191, 36, 0.28)'
                    : 'rgba(52, 211, 153, 0.28)'
                  : activeTheme.heroBorder,
              },
            ]}
          >
            <View style={styles.vaultTopRow}>
              <Text
                numberOfLines={1}
                style={[styles.vaultOverline, { color: activeTheme.heroMutedInk }]}
              >
                {t('investments.totalValueLabel')}
              </Text>

              <View style={styles.vaultTopActions}>
                <Pressable
                  onPress={() => void loadPortfolio('refresh')}
                  disabled={isRefreshing}
                  hitSlop={6}
                  style={({ pressed }) => [
                    styles.vaultRefreshPill,
                    {
                      backgroundColor: activeTheme.heroPillBg,
                      borderColor: activeTheme.heroBorder,
                      opacity: pressed || isRefreshing ? 0.72 : 1,
                    },
                  ]}
                >
                  <RefreshCw
                    size={11}
                    color={activeTheme.heroInk}
                    strokeWidth={1.75}
                  />
                  <Text
                    style={[
                      styles.vaultRefreshText,
                      { color: activeTheme.heroInk },
                    ]}
                  >
                    {isRefreshing
                      ? t('investments.refreshingLabel')
                      : t('investments.refreshButton')}
                  </Text>
                </Pressable>

                <View
                  style={[
                    styles.vaultBadge,
                    {
                      backgroundColor: activeTheme.heroPillBg,
                      borderColor: activeTheme.heroBorder,
                    },
                  ]}
                >
                  <View
                    style={[
                      styles.vaultDot,
                      { backgroundColor: activeTheme.accentHighlight },
                    ]}
                  />
                  <Text style={[styles.vaultBadgeText, { color: activeTheme.heroInk }]}>
                    {t('investments.vaultBadge')}
                  </Text>
                </View>
              </View>
            </View>

            <MoneyText
              amount={totalValue}
              currency={currencyCode}
              size="hero"
              color={activeTheme.heroInk}
              fractionOpacity={0.68}
            />

            <View
              style={[
                styles.vaultDivider,
                { backgroundColor: activeTheme.heroBorder },
              ]}
            />

            <View style={styles.vaultMetricsRow}>
              <View style={styles.vaultMetricCol}>
                <Text style={[styles.vaultMetricLabel, { color: activeTheme.heroMutedInk }]}>
                  {t('investments.totalCostLabel')}
                </Text>
                <MoneyText
                  amount={totalCost}
                  currency={currencyCode}
                  size="md"
                  color={activeTheme.heroInk}
                />
              </View>

              <View style={[styles.vaultMetricCol, styles.vaultMetricRight]}>
                <Text style={[styles.vaultMetricLabel, { color: activeTheme.heroMutedInk }]}>
                  {t('investments.netProfitLossLabel')}
                </Text>
                <View
                  style={[
                    styles.heroPnlBadge,
                    {
                      backgroundColor: isNetProfit
                        ? colors.incomeSoft
                        : colors.expenseSoft,
                    },
                  ]}
                >
                  {isNetProfit ? (
                    <TrendingUp size={13} color={colors.income} strokeWidth={1.75} />
                  ) : (
                    <TrendingDown size={13} color={colors.expense} strokeWidth={1.75} />
                  )}
                  <Text
                    style={[
                      styles.heroPnlText,
                      typography.tabularNums,
                      { color: isNetProfit ? colors.income : colors.expense },
                    ]}
                  >
                    {formatMoney(totalPnl, currencyCode, { showSign: true })} (
                    {formatSignedPercent(totalPnlPct, 2, locale)})
                  </Text>
                </View>
              </View>
            </View>

            {/* Category Allocation Stacked Bar + Legend */}
            {portfolio && portfolio.categoryAllocation.length > 0 ? (
              <View style={styles.allocationBlock}>
                <Text
                  style={[
                    styles.allocationTitle,
                    { color: activeTheme.heroMutedInk },
                  ]}
                >
                  {t('investments.allocationTitle')}
                </Text>

                <View style={styles.stackedTrack}>
                  {portfolio.categoryAllocation.map((alloc) => {
                    const widthPct = Math.max(alloc.sharePercent, 2);
                    return (
                      <View
                        key={alloc.category}
                        style={[
                          styles.stackedSegment,
                          {
                            width: `${widthPct}%`,
                            backgroundColor: CATEGORY_COLORS[alloc.category],
                          },
                        ]}
                      />
                    );
                  })}
                </View>

                <View style={styles.legendWrap}>
                  {portfolio.categoryAllocation.map((alloc) => (
                    <View key={alloc.category} style={styles.legendItem}>
                      <View
                        style={[
                          styles.legendDot,
                          { backgroundColor: CATEGORY_COLORS[alloc.category] },
                        ]}
                      />
                      <Text
                        style={[
                          styles.legendText,
                          typography.tabularNums,
                          { color: activeTheme.heroInk },
                        ]}
                      >
                        {t(`investments.categories.${alloc.category}`)}{' '}
                        {formatPercent(alloc.sharePercent, 1, locale)}
                      </Text>
                    </View>
                  ))}
                </View>
              </View>
            ) : null}
          </View>

          {canContribute ? (
            <AppButton
              label={t('investments.addButton')}
              onPress={() => setAddModalOpen(true)}
              primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
              leftIcon={<Plus size={18} color="#FFFFFF" strokeWidth={1.75} />}
            />
          ) : null}

          {/* Category Filter Pills */}
          <ScrollView
            horizontal
            showsHorizontalScrollIndicator={false}
            style={styles.filterScroll}
            contentContainerStyle={styles.filterRow}
          >
            {FILTER_KEYS.map((key) => {
              const selected = filter === key;
              return (
                <Pressable
                  key={key}
                  onPress={() => setFilter(key)}
                  style={[
                    styles.filterChip,
                    isDark &&
                      !selected && {
                        backgroundColor: '#131B29',
                        borderColor: 'rgba(148, 163, 184, 0.22)',
                      },
                    selected && {
                      backgroundColor: isDark
                        ? '#10B981'
                        : activeTheme.primarySurface,
                      borderColor: isDark
                        ? '#10B981'
                        : activeTheme.primarySurface,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.filterChipText,
                      isDark && !selected && { color: '#CBD5E1' },
                      selected && styles.filterChipTextSelected,
                      isDark && selected && { fontWeight: '700' },
                    ]}
                  >
                    {t(`investments.filters.${key}`)}
                  </Text>
                </Pressable>
              );
            })}
          </ScrollView>

          {/* Holdings List */}
          {!portfolio || portfolio.holdings.length === 0 ? (
            <SurfaceCard padding="xl" style={styles.emptyCard}>
              <Text
                style={[styles.emptyTitle, isDark && { color: '#F8FAFC' }]}
              >
                {t('investments.emptyTitle')}
              </Text>
              <Text
                style={[styles.emptyBody, isDark && { color: '#CBD5E1' }]}
              >
                {t('investments.emptyBody')}
              </Text>
            </SurfaceCard>
          ) : filteredHoldings.length === 0 ? (
            <SurfaceCard padding="xl" style={styles.emptyCard}>
              <Text
                style={[styles.emptyTitle, isDark && { color: '#F8FAFC' }]}
              >
                {t('investments.emptyFilterTitle')}
              </Text>
              <Text
                style={[styles.emptyBody, isDark && { color: '#CBD5E1' }]}
              >
                {t('investments.emptyFilterBody')}
              </Text>
            </SurfaceCard>
          ) : (
            <View style={styles.holdingsList}>
              {filteredHoldings.map((holding) => (
                <HoldingCard
                  key={`${holding.category}-${holding.symbol}`}
                  holding={holding}
                  canContribute={canContribute}
                  onDeleteLot={handleDeleteLot}
                />
              ))}
            </View>
          )}
        </ScrollView>
      )}

      <AddInvestmentModal
        visible={addModalOpen}
        onClose={() => setAddModalOpen(false)}
        onAdded={() => void loadPortfolio('refresh')}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: colors.canvas,
  },
  topHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.md,
    gap: spacing.sm,
  },
  screenTitle: {
    ...typography.h2,
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginHorizontal: spacing.xl,
    marginBottom: spacing.md,
    padding: spacing.md,
    borderRadius: radii.md,
    backgroundColor: colors.expenseSoft,
    borderWidth: 1,
    borderColor: '#FECACA',
  },
  errorText: {
    flex: 1,
    fontSize: 13,
    color: colors.expense,
    fontWeight: '500',
  },
  retryText: {
    fontSize: 13,
    fontWeight: '700',
    color: colors.expense,
  },
  loadingWrap: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  scrollContent: {
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.xxxl,
    gap: spacing.base,
  },
  vaultCard: {
    borderRadius: radii.xl,
    borderWidth: 1,
    padding: spacing.xl,
    gap: spacing.sm,
  },
  vaultTopRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.xs + 2,
    rowGap: spacing.xs + 2,
  },
  vaultTopActions: {
    flexDirection: 'row',
    alignItems: 'center',
    flexShrink: 0,
    gap: spacing.xs,
  },
  vaultRefreshPill: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: radii.full,
    borderWidth: 1,
    gap: 4,
  },
  vaultRefreshText: {
    fontSize: 10,
    fontWeight: '600',
  },
  vaultOverline: {
    flexShrink: 1,
    fontSize: 11,
    fontWeight: '600',
    letterSpacing: 0.4,
    textTransform: 'uppercase',
  },
  vaultBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: radii.full,
    borderWidth: 1,
    gap: 5,
  },
  vaultDot: {
    width: 6,
    height: 6,
    borderRadius: radii.full,
  },
  vaultBadgeText: {
    fontSize: 10,
    fontWeight: '700',
    letterSpacing: 0.8,
  },
  vaultDivider: {
    height: 1,
    marginVertical: spacing.sm,
  },
  vaultMetricsRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  vaultMetricCol: {
    gap: 4,
  },
  vaultMetricRight: {
    alignItems: 'flex-end',
    flex: 1,
  },
  vaultMetricLabel: {
    fontSize: 12,
    fontWeight: '500',
  },
  heroPnlBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
    paddingHorizontal: 9,
    paddingVertical: 4,
    borderRadius: radii.full,
  },
  heroPnlText: {
    fontSize: 12,
    fontWeight: '700',
  },
  allocationBlock: {
    marginTop: spacing.md,
    paddingTop: spacing.md,
    borderTopWidth: 1,
    borderTopColor: 'rgba(255, 255, 255, 0.14)',
    gap: spacing.sm,
  },
  allocationTitle: {
    fontSize: 11,
    fontWeight: '600',
    letterSpacing: 0.6,
    textTransform: 'uppercase',
  },
  stackedTrack: {
    flexDirection: 'row',
    height: 8,
    borderRadius: radii.full,
    overflow: 'hidden',
    backgroundColor: 'rgba(255, 255, 255, 0.12)',
    gap: 2,
  },
  stackedSegment: {
    height: '100%',
  },
  legendWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.md,
  },
  legendItem: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  legendDot: {
    width: 7,
    height: 7,
    borderRadius: radii.full,
  },
  legendText: {
    fontSize: 12,
    fontWeight: '500',
  },
  filterScroll: {
    marginHorizontal: -spacing.xl,
  },
  filterRow: {
    gap: spacing.sm,
    paddingLeft: spacing.xl,
    paddingRight: spacing.xl + 16,
  },
  filterChip: {
    paddingHorizontal: spacing.base,
    paddingVertical: 8,
    borderRadius: radii.full,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
  },
  filterChipText: {
    fontSize: 13,
    fontWeight: '500',
    color: colors.inkSecondary,
  },
  filterChipTextSelected: {
    color: '#FFFFFF',
    fontWeight: '600',
  },
  emptyCard: {
    gap: spacing.xs,
  },
  emptyTitle: {
    ...typography.bodyMedium,
  },
  emptyBody: {
    ...typography.caption,
  },
  holdingsList: {
    gap: spacing.md,
  },
});
