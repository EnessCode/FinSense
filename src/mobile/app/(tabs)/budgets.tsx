import { useFocusEffect, useRouter } from 'expo-router';
import {
  AlertCircle,
  Calendar,
  Link2,
  Pause,
  Play,
  Plus,
  Repeat,
  Trash2,
} from 'lucide-react-native';
import React, { useCallback, useRef, useState } from 'react';
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
import {
  deleteBudget,
  deleteSavingsGoal,
  getAnalyticsSummary,
  getBudgets,
  getCategories,
  getSavingsGoals,
  getWallets,
  recurringApi,
} from '@/api/endpoints';
import type {
  AnalyticsSummaryDto,
  BudgetDto,
  BudgetStatus,
  CategoryDto,
  RecurringTransactionsSummaryDto,
  SavingsGoalDto,
  WalletDto,
} from '@/api/types';
import { CreateSavingsGoalModal } from '@/components/budgets/CreateSavingsGoalModal';
import { UpsertBudgetModal } from '@/components/budgets/UpsertBudgetModal';
import {
  AppButton,
  CategoryIcon,
  MonthSelector,
  MoneyText,
  SurfaceCard,
  WorkspacePillSwitcher,
} from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { getActiveLocale } from '@/i18n';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import {
  formatDateShort,
  formatMoney,
  formatPercent,
  getMonthRange,
} from '@/utils/formatters';

type SegmentKey = 'budgets' | 'recurring' | 'goals' | 'analytics';
const SEGMENTS: SegmentKey[] = ['budgets', 'recurring', 'goals', 'analytics'];

function getBudgetBarColor(status: BudgetStatus, fallback: string): string {
  switch (status) {
    case 'Exceeded':
      return colors.expense;
    case 'Warning':
      return colors.warning;
    case 'Normal':
    default:
      return fallback;
  }
}

export default function BudgetsScreen() {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const router = useRouter();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();
  const locale = getActiveLocale();

  const workspaceId = activeWorkspace?.id;
  const isHousehold = activeWorkspace?.type === 'Household';
  const canManage =
    activeWorkspace?.currentUserRole === 'Owner' ||
    activeWorkspace?.currentUserRole === 'Admin';
  const canContribute = activeWorkspace?.currentUserRole !== 'Viewer';
  const currencyCode = activeWorkspace?.defaultCurrency ?? 'TRY';

  const now = new Date();
  const [selectedYear, setSelectedYear] = useState(now.getFullYear());
  const [selectedMonth, setSelectedMonth] = useState(now.getMonth() + 1);
  const monthBounds = getMonthRange(selectedYear, selectedMonth);
  const loadedWorkspaceRef = useRef<string | null>(null);

  const [activeSegment, setActiveSegment] = useState<SegmentKey>('budgets');
  const [budgets, setBudgets] = useState<BudgetDto[]>([]);
  const [recurringSummary, setRecurringSummary] =
    useState<RecurringTransactionsSummaryDto | null>(null);
  const [goals, setGoals] = useState<SavingsGoalDto[]>([]);
  const [analytics, setAnalytics] = useState<AnalyticsSummaryDto | null>(null);
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [wallets, setWallets] = useState<WalletDto[]>([]);

  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const [budgetModalOpen, setBudgetModalOpen] = useState(false);
  const [goalModalOpen, setGoalModalOpen] = useState(false);

  const loadAll = useCallback(
    async (mode: 'initial' | 'refresh' = 'initial') => {
      if (!workspaceId) {
        setIsLoading(false);
        return;
      }

      if (mode === 'initial') setIsLoading(true);
      if (mode === 'refresh') setIsRefreshing(true);
      setErrorMessage(null);

      try {
        const [
          budgetsRes,
          recurringRes,
          goalsRes,
          analyticsRes,
          categoriesRes,
          walletsRes,
        ] = await Promise.all([
          getBudgets(workspaceId, selectedYear, selectedMonth),
          recurringApi.list(workspaceId),
          getSavingsGoals(workspaceId),
          getAnalyticsSummary(
            workspaceId,
            monthBounds.dateFrom,
            monthBounds.dateTo,
            selectedYear,
            selectedMonth,
          ),
          getCategories(workspaceId),
          getWallets(workspaceId, false),
        ]);

        setBudgets(budgetsRes);
        setRecurringSummary(recurringRes);
        setGoals(goalsRes);
        setAnalytics(analyticsRes);
        setCategories(categoriesRes);
        setWallets(walletsRes);
        loadedWorkspaceRef.current = workspaceId;
      } catch (err) {
        setErrorMessage(toApiError(err).message);
      } finally {
        setIsLoading(false);
        setIsRefreshing(false);
      }
    },
    [workspaceId, selectedYear, selectedMonth, monthBounds.dateFrom, monthBounds.dateTo],
  );

  useFocusEffect(
    useCallback(() => {
      const isNewWorkspace = loadedWorkspaceRef.current !== workspaceId;
      void loadAll(isNewWorkspace ? 'initial' : 'refresh');
    }, [loadAll, workspaceId]),
  );

  const handleDeleteBudget = async (id: string) => {
    if (!workspaceId) return;
    try {
      await deleteBudget(workspaceId, id);
      setBudgets((prev) => prev.filter((b) => b.id !== id));
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    }
  };

  const handleDeleteGoal = async (id: string) => {
    if (!workspaceId) return;
    try {
      await deleteSavingsGoal(workspaceId, id);
      setGoals((prev) => prev.filter((g) => g.id !== id));
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    }
  };

  const handleToggleRecurring = async (id: string) => {
    if (!workspaceId) return;
    try {
      await recurringApi.toggle(workspaceId, id);
      const refreshed = await recurringApi.list(workspaceId);
      setRecurringSummary(refreshed);
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    }
  };

  const handleDeleteRecurring = async (id: string) => {
    if (!workspaceId) return;
    try {
      await recurringApi.remove(workspaceId, id);
      const refreshed = await recurringApi.list(workspaceId);
      setRecurringSummary(refreshed);
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    }
  };

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
          {t('budgets.screenTitle')}
        </Text>
        <WorkspacePillSwitcher />
      </View>

      {/* 4-Segment Control */}
      <View
        style={[
          styles.segmentedWrap,
          isDark && {
            backgroundColor: '#0E1420',
            borderColor: 'rgba(148, 163, 184, 0.14)',
            gap: 4,
          },
        ]}
      >
        {SEGMENTS.map((seg) => {
          const selected = activeSegment === seg;
          return (
            <Pressable
              key={seg}
              onPress={() => setActiveSegment(seg)}
              style={[
                styles.segmentButton,
                isDark &&
                  !selected && {
                    backgroundColor: '#131B29',
                    borderWidth: 1,
                    borderColor: 'rgba(148, 163, 184, 0.22)',
                  },
                selected && {
                  backgroundColor: isDark
                    ? '#10B981'
                    : activeTheme.primarySurface,
                },
              ]}
            >
              <Text
                numberOfLines={1}
                adjustsFontSizeToFit
                minimumFontScale={0.85}
                style={[
                  styles.segmentLabel,
                  isDark && !selected && { color: '#CBD5E1' },
                  selected && styles.segmentLabelSelected,
                  isDark && selected && { fontWeight: '700' },
                ]}
              >
                {t(`budgets.segments.${seg}`)}
              </Text>
            </Pressable>
          );
        })}
      </View>

      {/* Month Selector for Budgets & Analytics */}
      {activeSegment === 'budgets' || activeSegment === 'analytics' ? (
        <View style={styles.monthSelectorWrap}>
          <MonthSelector
            year={selectedYear}
            month={selectedMonth}
            onChange={({ year, month }) => {
              setSelectedYear(year);
              setSelectedMonth(month);
            }}
          />
        </View>
      ) : null}

      {errorMessage ? (
        <View style={styles.errorBanner}>
          <AlertCircle size={16} color={colors.expense} strokeWidth={1.75} />
          <Text style={styles.errorText}>{errorMessage}</Text>
          <Pressable onPress={() => void loadAll('initial')} hitSlop={8}>
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
              onRefresh={() => void loadAll('refresh')}
              tintColor={activeTheme.primarySurface}
            />
          }
        >
          {/* SEGMENT 1: BUDGETS */}
          {activeSegment === 'budgets' ? (
            <View style={styles.segmentBody}>
              {canManage ? (
                <AppButton
                  label={t('budgets.addBudgetButton')}
                  variant="secondary"
                  onPress={() => setBudgetModalOpen(true)}
                  leftIcon={
                    <Plus
                      size={17}
                      color={isDark ? '#34D399' : activeTheme.primarySurface}
                      strokeWidth={1.75}
                    />
                  }
                />
              ) : null}

              {budgets.length === 0 ? (
                <SurfaceCard padding="xl" style={styles.emptyCard}>
                  <Text
                    style={[styles.emptyTitle, isDark && { color: '#F8FAFC' }]}
                  >
                    {t('budgets.emptyBudgetsTitle')}
                  </Text>
                  <Text
                    style={[styles.emptyBody, isDark && { color: '#CBD5E1' }]}
                  >
                    {t('budgets.emptyBudgetsBody')}
                  </Text>
                </SurfaceCard>
              ) : (
                budgets.map((budget) => {
                  const barColor = isDark
                    ? budget.status === 'Exceeded'
                      ? '#F87171'
                      : budget.status === 'Warning'
                        ? '#FBBF24'
                        : '#10B981'
                    : getBudgetBarColor(budget.status, colors.income);
                  const clamped = Math.min(
                    Math.max(budget.utilizationPercent, 0),
                    100,
                  );
                  const isExceeded = budget.remainingAmount < 0;

                  return (
                    <SurfaceCard key={budget.id} padding="lg" style={styles.itemCard}>
                      <View style={styles.cardTopRow}>
                        <View style={styles.cardTitleGroup}>
                          {budget.categoryIcon ? (
                            <View
                              style={[
                                styles.catIconBox,
                                isDark && {
                                  backgroundColor: '#182234',
                                  borderColor: 'rgba(148, 163, 184, 0.22)',
                                },
                              ]}
                            >
                              <CategoryIcon
                                iconName={budget.categoryIcon}
                                size={16}
                                color={
                                  isDark
                                    ? '#34D399'
                                    : activeTheme.primarySurface
                                }
                              />
                            </View>
                          ) : null}
                          <Text
                            style={[
                              styles.cardTitle,
                              isDark && { color: '#F8FAFC' },
                            ]}
                            numberOfLines={1}
                          >
                            {budget.categoryName ??
                              t('dashboard.budgetOverallLabel')}
                          </Text>
                        </View>

                        <View style={styles.cardRightActions}>
                          <Text
                            style={[
                              styles.percentBadge,
                              typography.tabularNums,
                              { color: barColor },
                            ]}
                          >
                            {formatPercent(budget.utilizationPercent, 0, locale)}
                          </Text>

                          {canManage ? (
                            <Pressable
                              onPress={() => void handleDeleteBudget(budget.id)}
                              hitSlop={10}
                              style={styles.deleteIconBtn}
                            >
                              <Trash2
                                size={15}
                                color={isDark ? '#94A3B8' : colors.inkSubtle}
                                strokeWidth={1.75}
                              />
                            </Pressable>
                          ) : null}
                        </View>
                      </View>

                      <View
                        style={[
                          styles.progressTrack,
                          isDark && {
                            backgroundColor: 'rgba(148, 163, 184, 0.22)',
                          },
                        ]}
                      >
                        <View
                          style={[
                            styles.progressFill,
                            { width: `${clamped}%`, backgroundColor: barColor },
                          ]}
                        />
                      </View>

                      <View style={styles.cardBottomRow}>
                        <View style={styles.inlineMoney}>
                          <MoneyText
                            amount={budget.spentAmount}
                            currency={budget.currencyCode}
                            size="sm"
                            color={isDark ? '#F8FAFC' : colors.ink}
                          />
                          <Text
                            style={[
                              styles.slashSep,
                              isDark && { color: '#94A3B8' },
                            ]}
                          >
                            {' '}
                            /{' '}
                          </Text>
                          <MoneyText
                            amount={budget.limitAmount}
                            currency={budget.currencyCode}
                            size="sm"
                            color={isDark ? '#94A3B8' : colors.inkSecondary}
                          />
                        </View>

                        <Text
                          style={[
                            styles.remainingText,
                            typography.tabularNums,
                            isDark && { color: '#CBD5E1' },
                            isExceeded && {
                              color: isDark ? '#F87171' : colors.expense,
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
                    </SurfaceCard>
                  );
                })
              )}
            </View>
          ) : null}

          {/* SEGMENT 2: RECURRING (SABİTLER) */}
          {activeSegment === 'recurring' ? (
            <View style={styles.segmentBody}>
              {/* Summary Card */}
              <SurfaceCard padding="lg" style={styles.recurringSummaryCard}>
                <View style={styles.recurringSummaryRow}>
                  <View style={styles.recurringSummaryCol}>
                    <Text
                      style={[
                        styles.summaryOverline,
                        isDark && { color: '#94A3B8' },
                      ]}
                    >
                      {t('budgets.recurringSummaryIncome')}
                    </Text>
                    <MoneyText
                      amount={recurringSummary?.totalMonthlyFixedIncome ?? 0}
                      currency={currencyCode}
                      size="md"
                      color={isDark ? '#34D399' : colors.income}
                    />
                  </View>

                  <View
                    style={[
                      styles.recurringSummaryDivider,
                      isDark && { backgroundColor: themeColors.cardBorder },
                    ]}
                  />

                  <View style={styles.recurringSummaryCol}>
                    <Text
                      style={[
                        styles.summaryOverline,
                        isDark && { color: '#94A3B8' },
                      ]}
                    >
                      {t('budgets.recurringSummaryExpense')}
                    </Text>
                    <MoneyText
                      amount={recurringSummary?.totalMonthlyFixedExpense ?? 0}
                      currency={currencyCode}
                      size="md"
                      color={isDark ? '#F8FAFC' : colors.ink}
                    />
                  </View>
                </View>

                <View
                  style={[
                    styles.recurringNetRow,
                    isDark && { borderTopColor: themeColors.cardBorder },
                  ]}
                >
                  <Text
                    style={[
                      styles.recurringNetLabel,
                      isDark && { color: '#CBD5E1' },
                    ]}
                  >
                    {t('budgets.recurringSummaryNet')}
                  </Text>
                  <MoneyText
                    amount={recurringSummary?.netMonthlyFixedFlow ?? 0}
                    currency={currencyCode}
                    size="lg"
                    showSign={(recurringSummary?.netMonthlyFixedFlow ?? 0) > 0}
                    color={
                      (recurringSummary?.netMonthlyFixedFlow ?? 0) >= 0
                        ? isDark
                          ? '#34D399'
                          : colors.income
                        : isDark
                          ? '#F87171'
                          : colors.expense
                    }
                  />
                </View>
              </SurfaceCard>

              {canContribute ? (
                <AppButton
                  label={t('budgets.addRecurringButton')}
                  variant="secondary"
                  onPress={() =>
                    router.push({
                      pathname: '/(tabs)/add',
                      params: { recurring: '1' },
                    })
                  }
                  leftIcon={
                    <Plus
                      size={17}
                      color={isDark ? '#34D399' : activeTheme.primarySurface}
                      strokeWidth={1.75}
                    />
                  }
                />
              ) : null}

              {!recurringSummary || recurringSummary.items.length === 0 ? (
                <SurfaceCard padding="xl" style={styles.emptyCard}>
                  <Text
                    style={[styles.emptyTitle, isDark && { color: '#F8FAFC' }]}
                  >
                    {t('budgets.emptyRecurringTitle')}
                  </Text>
                  <Text
                    style={[styles.emptyBody, isDark && { color: '#CBD5E1' }]}
                  >
                    {t('budgets.emptyRecurringBody')}
                  </Text>
                </SurfaceCard>
              ) : (
                recurringSummary.items.map((item) => {
                  const isIncome = item.type === 'Income';
                  const hasFiniteTotal =
                    item.totalOccurrences !== null &&
                    item.totalOccurrences !== undefined &&
                    item.totalOccurrences > 0;
                  const progressPct = hasFiniteTotal
                    ? Math.min(
                        Math.max(
                          (item.completedOccurrences / item.totalOccurrences!) *
                            100,
                          0,
                        ),
                        100,
                      )
                    : 0;
                  const remainingMonths = hasFiniteTotal
                    ? (item.remainingOccurrences ??
                      Math.max(
                        0,
                        item.totalOccurrences! - item.completedOccurrences,
                      ))
                    : 0;

                  return (
                    <SurfaceCard
                      key={item.id}
                      padding="lg"
                      style={[
                        styles.itemCard,
                        !item.isActive && styles.pausedRecurringCard,
                      ]}
                    >
                      <View style={styles.cardTopRow}>
                        <View style={styles.recurringTitleCol}>
                          <View style={styles.recurringTitleInline}>
                            <Text
                              style={[
                                styles.cardTitle,
                                isDark && { color: '#F8FAFC' },
                              ]}
                              numberOfLines={1}
                            >
                              {item.title}
                            </Text>
                            {!item.isActive ? (
                              <View
                                style={[
                                  styles.pausedBadge,
                                  isDark && {
                                    backgroundColor: 'rgba(148, 163, 184, 0.16)',
                                    borderColor: 'rgba(148, 163, 184, 0.25)',
                                  },
                                ]}
                              >
                                <Text
                                  style={[
                                    styles.pausedBadgeText,
                                    isDark && { color: '#CBD5E1' },
                                  ]}
                                >
                                  {t('budgets.recurringPausedBadge')}
                                </Text>
                              </View>
                            ) : null}
                          </View>
                          <Text
                            style={[
                              styles.recurringMetaCaption,
                              isDark && { color: '#94A3B8' },
                            ]}
                            numberOfLines={1}
                          >
                            {item.categoryName} • {item.walletName}
                          </Text>
                        </View>

                        <MoneyText
                          amount={isIncome ? item.amount : -Math.abs(item.amount)}
                          currency={item.currency}
                          size="lg"
                          showSign={isIncome}
                          color={
                            isIncome
                              ? isDark
                                ? '#34D399'
                                : colors.income
                              : isDark
                                ? '#F8FAFC'
                                : colors.ink
                          }
                        />
                      </View>

                      {/* Cycle Badge & Progress */}
                      {hasFiniteTotal ? (
                        <View style={styles.cycleBlock}>
                          <View
                            style={[
                              styles.cycleBadge,
                              {
                                backgroundColor: isDark
                                  ? '#1E293B'
                                  : activeTheme.softTint,
                                borderColor: isDark
                                  ? 'rgba(148, 163, 184, 0.25)'
                                  : activeTheme.softTintBorder,
                              },
                            ]}
                          >
                            <Repeat
                              size={12}
                              color={
                                isDark ? '#E2E8F0' : activeTheme.primarySurface
                              }
                              strokeWidth={1.75}
                            />
                            <Text
                              style={[
                                styles.cycleBadgeText,
                                typography.tabularNums,
                                {
                                  color: isDark
                                    ? '#E2E8F0'
                                    : activeTheme.primarySurface,
                                },
                              ]}
                            >
                              {t('budgets.recurringProgressBadge', {
                                completed: item.completedOccurrences,
                                total: item.totalOccurrences,
                                remaining: remainingMonths,
                              })}
                            </Text>
                          </View>
                          <View
                            style={[
                              styles.progressTrack,
                              isDark && {
                                backgroundColor: 'rgba(148, 163, 184, 0.2)',
                              },
                            ]}
                          >
                            <View
                              style={[
                                styles.progressFill,
                                {
                                  width: `${progressPct}%`,
                                  backgroundColor: item.isActive
                                    ? isDark
                                      ? '#10B981'
                                      : activeTheme.primarySurface
                                    : isDark
                                      ? '#64748B'
                                      : colors.inkSubtle,
                                },
                              ]}
                            />
                          </View>
                        </View>
                      ) : (
                        <View
                          style={[
                            styles.cycleBadge,
                            {
                              backgroundColor: isDark
                                ? '#1E293B'
                                : colors.surfaceSubtle,
                              borderColor: isDark
                                ? 'rgba(148, 163, 184, 0.25)'
                                : colors.border,
                            },
                          ]}
                        >
                          <Repeat
                            size={12}
                            color={isDark ? '#E2E8F0' : colors.inkSecondary}
                            strokeWidth={1.75}
                          />
                          <Text
                            style={[
                              styles.indefiniteBadgeText,
                              isDark && { color: '#E2E8F0' },
                            ]}
                          >
                            {t('budgets.recurringIndefiniteBadge')}
                          </Text>
                        </View>
                      )}

                      <Text
                        style={[
                          styles.nextBillingText,
                          typography.tabularNums,
                          isDark && { color: '#94A3B8' },
                        ]}
                      >
                        {t('budgets.recurringNextBilling', {
                          day: item.dayOfMonth,
                          date: formatDateShort(item.nextExecutionDateUtc),
                        })}
                      </Text>

                      {canContribute ? (
                        <View style={styles.recurringActionsRow}>
                          <Pressable
                            onPress={() => void handleToggleRecurring(item.id)}
                            style={({ pressed }) => [
                              styles.recurringActionBtn,
                              isDark && {
                                backgroundColor: '#182234',
                                borderColor: 'rgba(148, 163, 184, 0.25)',
                              },
                              pressed && { opacity: 0.78 },
                            ]}
                          >
                            {item.isActive ? (
                              <Pause
                                size={14}
                                color={isDark ? '#E2E8F0' : colors.ink}
                                strokeWidth={1.75}
                              />
                            ) : (
                              <Play
                                size={14}
                                color={
                                  isDark
                                    ? '#34D399'
                                    : activeTheme.primarySurface
                                }
                                strokeWidth={1.75}
                              />
                            )}
                            <Text
                              style={[
                                styles.recurringActionText,
                                isDark && { color: '#E2E8F0' },
                                !item.isActive && {
                                  color: isDark
                                    ? '#34D399'
                                    : activeTheme.primarySurface,
                                },
                              ]}
                            >
                              {item.isActive
                                ? t('budgets.recurringPauseAction')
                                : t('budgets.recurringResumeAction')}
                            </Text>
                          </Pressable>

                          <Pressable
                            onPress={() => void handleDeleteRecurring(item.id)}
                            style={({ pressed }) => [
                              styles.recurringActionBtn,
                              styles.recurringDeleteBtn,
                              isDark && {
                                backgroundColor: 'rgba(239, 68, 68, 0.14)',
                                borderColor: 'rgba(239, 68, 68, 0.35)',
                              },
                              pressed && { opacity: 0.78 },
                            ]}
                          >
                            <Trash2
                              size={14}
                              color={isDark ? '#F87171' : colors.expense}
                              strokeWidth={1.75}
                            />
                            <Text
                              style={[
                                styles.recurringDeleteText,
                                isDark && { color: '#F87171' },
                              ]}
                            >
                              {t('common.delete')}
                            </Text>
                          </Pressable>
                        </View>
                      ) : null}
                    </SurfaceCard>
                  );
                })
              )}
            </View>
          ) : null}

          {/* SEGMENT 3: SAVINGS GOALS */}
          {activeSegment === 'goals' ? (
            <View style={styles.segmentBody}>
              {canManage ? (
                <AppButton
                  label={t('budgets.addGoalButton')}
                  variant="secondary"
                  onPress={() => setGoalModalOpen(true)}
                  leftIcon={
                    <Plus
                      size={17}
                      color={isDark ? '#34D399' : activeTheme.primarySurface}
                      strokeWidth={1.75}
                    />
                  }
                />
              ) : null}

              {goals.length === 0 ? (
                <SurfaceCard padding="xl" style={styles.emptyCard}>
                  <Text
                    style={[styles.emptyTitle, isDark && { color: '#F8FAFC' }]}
                  >
                    {t('budgets.emptyGoalsTitle')}
                  </Text>
                  <Text
                    style={[styles.emptyBody, isDark && { color: '#CBD5E1' }]}
                  >
                    {t('budgets.emptyGoalsBody')}
                  </Text>
                </SurfaceCard>
              ) : (
                goals.map((goal) => {
                  const clamped = Math.min(
                    Math.max(goal.completionPercent, 0),
                    100,
                  );

                  return (
                    <SurfaceCard key={goal.id} padding="lg" style={styles.itemCard}>
                      <View style={styles.cardTopRow}>
                        <Text
                          style={[
                            styles.cardTitle,
                            isDark && { color: '#F8FAFC' },
                          ]}
                          numberOfLines={1}
                        >
                          {goal.name}
                        </Text>

                        <View style={styles.cardRightActions}>
                          <Text
                            style={[
                              styles.percentBadge,
                              typography.tabularNums,
                              {
                                color: isDark
                                  ? '#34D399'
                                  : activeTheme.primarySurface,
                              },
                            ]}
                          >
                            {formatPercent(goal.completionPercent, 0, locale)}
                          </Text>

                          {canManage ? (
                            <Pressable
                              onPress={() => void handleDeleteGoal(goal.id)}
                              hitSlop={10}
                              style={styles.deleteIconBtn}
                            >
                              <Trash2
                                size={15}
                                color={isDark ? '#94A3B8' : colors.inkSubtle}
                                strokeWidth={1.75}
                              />
                            </Pressable>
                          ) : null}
                        </View>
                      </View>

                      <View
                        style={[
                          styles.progressTrack,
                          isDark && {
                            backgroundColor: 'rgba(148, 163, 184, 0.22)',
                          },
                        ]}
                      >
                        <View
                          style={[
                            styles.progressFill,
                            {
                              width: `${clamped}%`,
                              backgroundColor: isDark
                                ? '#10B981'
                                : activeTheme.primarySurface,
                            },
                          ]}
                        />
                      </View>

                      <View style={styles.cardBottomRow}>
                        <View style={styles.inlineMoney}>
                          <MoneyText
                            amount={goal.currentAmount}
                            currency={goal.currencyCode}
                            size="sm"
                            color={isDark ? '#F8FAFC' : colors.ink}
                          />
                          <Text
                            style={[
                              styles.slashSep,
                              isDark && { color: '#94A3B8' },
                            ]}
                          >
                            {' '}
                            /{' '}
                          </Text>
                          <MoneyText
                            amount={goal.targetAmount}
                            currency={goal.currencyCode}
                            size="sm"
                            color={isDark ? '#94A3B8' : colors.inkSecondary}
                          />
                        </View>

                        <Text style={[styles.remainingText, typography.tabularNums]}>
                          {t('budgets.goalRemaining', {
                            amount: formatMoney(
                              goal.remainingAmount,
                              goal.currencyCode,
                            ),
                          })}
                        </Text>
                      </View>

                      {goal.linkedWalletName || goal.targetDate ? (
                        <View style={styles.goalMetaRow}>
                          {goal.linkedWalletName ? (
                            <View style={styles.goalMetaTag}>
                              <Link2
                                size={12}
                                color={colors.inkSecondary}
                                strokeWidth={1.75}
                              />
                              <Text style={styles.goalMetaText}>
                                {t('budgets.goalLinkedWallet', {
                                  name: goal.linkedWalletName,
                                })}
                              </Text>
                            </View>
                          ) : null}
                          {goal.targetDate ? (
                            <View style={styles.goalMetaTag}>
                              <Calendar
                                size={12}
                                color={colors.inkSecondary}
                                strokeWidth={1.75}
                              />
                              <Text
                                style={[
                                  styles.goalMetaText,
                                  typography.tabularNums,
                                ]}
                              >
                                {formatDateShort(goal.targetDate)}
                              </Text>
                            </View>
                          ) : null}
                        </View>
                      ) : null}
                    </SurfaceCard>
                  );
                })
              )}
            </View>
          ) : null}

          {/* SEGMENT 3: DETERMINISTIC ANALYTICS */}
          {activeSegment === 'analytics' ? (
            <View style={styles.segmentBody}>
              {/* Net Cash Flow & Savings Rate */}
              <View style={styles.summaryPair}>
                <SurfaceCard padding="lg" style={styles.summaryBox}>
                  <Text
                    style={[
                      styles.summaryOverline,
                      isDark && { color: '#94A3B8' },
                    ]}
                  >
                    {t('budgets.analyticsNetCashFlow')}
                  </Text>
                  <MoneyText
                    amount={analytics?.netCashFlow ?? 0}
                    currency={analytics?.currencyCode ?? currencyCode}
                    size="lg"
                    color={
                      (analytics?.netCashFlow ?? 0) >= 0
                        ? isDark
                          ? '#34D399'
                          : colors.income
                        : isDark
                          ? '#F87171'
                          : colors.expense
                    }
                    showSign={(analytics?.netCashFlow ?? 0) > 0}
                  />
                </SurfaceCard>

                <SurfaceCard padding="lg" style={styles.summaryBox}>
                  <Text
                    style={[
                      styles.summaryOverline,
                      isDark && { color: '#94A3B8' },
                    ]}
                  >
                    {t('budgets.analyticsSavingsRate')}
                  </Text>
                  <Text
                    style={[
                      styles.savingsBig,
                      typography.tabularNums,
                      {
                        color: isDark
                          ? '#34D399'
                          : activeTheme.primarySurface,
                      },
                    ]}
                  >
                    {formatPercent(analytics?.savingsRatePercent ?? 0, 1, locale)}
                  </Text>
                </SurfaceCard>
              </View>

              {/* Category Breakdown */}
              <SurfaceCard padding="lg" style={styles.analyticsCard}>
                <Text
                  style={[
                    styles.analyticsSectionTitle,
                    isDark && { color: '#F8FAFC' },
                  ]}
                >
                  {t('budgets.analyticsCategoryTitle')}
                </Text>

                {!analytics || analytics.categoryBreakdown.length === 0 ? (
                  <Text
                    style={[styles.emptyBody, isDark && { color: '#CBD5E1' }]}
                  >
                    {t('budgets.analyticsCategoryEmpty')}
                  </Text>
                ) : (
                  <View style={styles.breakdownList}>
                    {analytics.categoryBreakdown.map((item) => {
                      const barPct = Math.min(Math.max(item.percentage, 0), 100);
                      return (
                        <View key={item.categoryId} style={styles.breakdownItem}>
                          <View style={styles.breakdownTop}>
                            <View style={styles.breakdownLeft}>
                              <CategoryIcon
                                iconName={item.icon}
                                size={15}
                                color={
                                  isDark
                                    ? '#34D399'
                                    : activeTheme.primarySurface
                                }
                              />
                              <Text
                                style={[
                                  styles.breakdownName,
                                  isDark && { color: '#F8FAFC' },
                                ]}
                                numberOfLines={1}
                              >
                                {item.categoryName}
                              </Text>
                              <Text
                                style={[
                                  styles.breakdownCount,
                                  typography.tabularNums,
                                  isDark && { color: '#94A3B8' },
                                ]}
                              >
                                ({t('budgets.analyticsTxCount', {
                                  count: item.transactionCount,
                                })})
                              </Text>
                            </View>

                            <View style={styles.breakdownRight}>
                              <MoneyText
                                amount={item.amount}
                                currency={analytics.currencyCode}
                                size="sm"
                                color={isDark ? '#F8FAFC' : colors.ink}
                              />
                              <Text
                                style={[
                                  styles.breakdownPct,
                                  typography.tabularNums,
                                  isDark && { color: '#94A3B8' },
                                ]}
                              >
                                {formatPercent(item.percentage, 1, locale)}
                              </Text>
                            </View>
                          </View>

                          <View
                            style={[
                              styles.progressTrack,
                              isDark && { backgroundColor: '#182234' },
                            ]}
                          >
                            <View
                              style={[
                                styles.progressFill,
                                {
                                  width: `${barPct}%`,
                                  backgroundColor: isDark
                                    ? '#10B981'
                                    : activeTheme.primarySurface,
                                },
                              ]}
                            />
                          </View>
                        </View>
                      );
                    })}
                  </View>
                )}
              </SurfaceCard>

              {/* Household Member Breakdown */}
              {isHousehold &&
              analytics &&
              analytics.memberBreakdown.length > 0 ? (
                <SurfaceCard padding="lg" style={styles.analyticsCard}>
                  <Text
                    style={[
                      styles.analyticsSectionTitle,
                      isDark && { color: '#F8FAFC' },
                    ]}
                  >
                    {t('budgets.analyticsMemberTitle')}
                  </Text>

                  <View style={styles.breakdownList}>
                    {analytics.memberBreakdown.map((member, idx) => {
                      const totalHouseholdExpense = analytics.totalExpense;
                      const sharePct =
                        totalHouseholdExpense > 0
                          ? (member.expenseAmount / totalHouseholdExpense) * 100
                          : 0;
                      const clampedPct = Math.min(Math.max(sharePct, 0), 100);

                      return (
                        <View
                          key={member.userId ?? `anon-${idx}`}
                          style={styles.breakdownItem}
                        >
                          <View style={styles.breakdownTop}>
                            <View style={styles.breakdownLeft}>
                              <Text style={styles.breakdownName} numberOfLines={1}>
                                {member.displayName}
                              </Text>
                              <Text
                                style={[
                                  styles.breakdownCount,
                                  typography.tabularNums,
                                ]}
                              >
                                ({t('budgets.analyticsMemberCount', {
                                  count: member.transactionCount,
                                })})
                              </Text>
                            </View>

                            <View style={styles.breakdownRight}>
                              <MoneyText
                                amount={member.expenseAmount}
                                currency={analytics.currencyCode}
                                size="sm"
                              />
                              <Text
                                style={[
                                  styles.breakdownPct,
                                  typography.tabularNums,
                                ]}
                              >
                                {formatPercent(sharePct, 1, locale)}
                              </Text>
                            </View>
                          </View>

                          <View style={styles.progressTrack}>
                            <View
                              style={[
                                styles.progressFill,
                                {
                                  width: `${clampedPct}%`,
                                  backgroundColor: activeTheme.accentHighlight,
                                },
                              ]}
                            />
                          </View>
                        </View>
                      );
                    })}
                  </View>
                </SurfaceCard>
              ) : null}
            </View>
          ) : null}
        </ScrollView>
      )}

      <UpsertBudgetModal
        visible={budgetModalOpen}
        year={selectedYear}
        month={selectedMonth}
        categories={categories}
        onClose={() => setBudgetModalOpen(false)}
        onSaved={() => void loadAll('refresh')}
      />

      <CreateSavingsGoalModal
        visible={goalModalOpen}
        wallets={wallets}
        onClose={() => setGoalModalOpen(false)}
        onCreated={() => void loadAll('refresh')}
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
  segmentedWrap: {
    flexDirection: 'row',
    marginHorizontal: spacing.xl,
    padding: 4,
    borderRadius: radii.md,
    backgroundColor: colors.surfaceSubtle,
    borderWidth: 1,
    borderColor: colors.border,
    marginBottom: spacing.base,
  },
  monthSelectorWrap: {
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.base,
  },
  segmentButton: {
    flex: 1,
    height: 36,
    paddingHorizontal: 4,
    borderRadius: radii.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  segmentLabel: {
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.inkSecondary,
  },
  segmentLabelSelected: {
    color: colors.inkOnDark,
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
  },
  segmentBody: {
    gap: spacing.md,
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
  itemCard: {
    gap: spacing.md,
  },
  cardTopRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  cardTitleGroup: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    flex: 1,
  },
  catIconBox: {
    width: 30,
    height: 30,
    borderRadius: radii.sm,
    backgroundColor: colors.surfaceSubtle,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  cardTitle: {
    ...typography.bodyMedium,
    flex: 1,
  },
  cardRightActions: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  percentBadge: {
    fontSize: 13.5,
    fontWeight: '700',
  },
  deleteIconBtn: {
    padding: 4,
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
  cardBottomRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  inlineMoney: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  slashSep: {
    fontSize: 12.5,
    color: colors.inkSubtle,
  },
  remainingText: {
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.inkSecondary,
  },
  goalMetaRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.md,
    paddingTop: spacing.xs,
    borderTopWidth: 1,
    borderTopColor: colors.cardBorder,
  },
  goalMetaTag: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
  },
  goalMetaText: {
    fontSize: 12,
    color: colors.inkSecondary,
  },
  summaryPair: {
    flexDirection: 'row',
    gap: spacing.md,
  },
  summaryBox: {
    flex: 1,
    gap: spacing.xs,
  },
  summaryOverline: {
    ...typography.overline,
  },
  savingsBig: {
    fontSize: 20,
    fontWeight: '700',
  },
  analyticsCard: {
    gap: spacing.base,
  },
  analyticsSectionTitle: {
    ...typography.h3,
  },
  breakdownList: {
    gap: spacing.base,
  },
  breakdownItem: {
    gap: spacing.xs + 2,
  },
  breakdownTop: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  breakdownLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    flex: 1,
  },
  breakdownName: {
    ...typography.bodyMedium,
    fontSize: 14,
    flexShrink: 1,
  },
  breakdownCount: {
    fontSize: 12,
    color: colors.inkSubtle,
  },
  breakdownRight: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  breakdownPct: {
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.inkSecondary,
  },
  recurringSummaryCard: {
    gap: spacing.base,
  },
  recurringSummaryRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  recurringSummaryCol: {
    flex: 1,
    gap: 4,
  },
  recurringSummaryDivider: {
    width: 1,
    height: 36,
    backgroundColor: colors.cardBorder,
  },
  recurringNetRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingTop: spacing.md,
    borderTopWidth: 1,
    borderTopColor: colors.cardBorder,
  },
  recurringNetLabel: {
    ...typography.caption,
    fontWeight: '600',
    color: colors.ink,
  },
  pausedRecurringCard: {
    opacity: 0.72,
  },
  recurringTitleCol: {
    flex: 1,
    gap: 3,
  },
  recurringTitleInline: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  pausedBadge: {
    paddingHorizontal: 7,
    paddingVertical: 2,
    borderRadius: radii.full,
    backgroundColor: colors.warningSoft,
    borderWidth: 1,
    borderColor: '#FDE68A',
  },
  pausedBadgeText: {
    fontSize: 10.5,
    fontWeight: '600',
    color: colors.warning,
  },
  recurringMetaCaption: {
    ...typography.caption,
    fontSize: 12.5,
  },
  cycleBlock: {
    gap: spacing.sm,
  },
  cycleBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-start',
    gap: 6,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  cycleBadgeText: {
    fontSize: 12,
    fontWeight: '600',
  },
  indefiniteBadgeText: {
    fontSize: 12,
    fontWeight: '600',
    color: colors.inkSecondary,
  },
  nextBillingText: {
    ...typography.caption,
    fontSize: 12,
  },
  recurringActionsRow: {
    flexDirection: 'row',
    gap: spacing.sm,
    paddingTop: spacing.xs,
    borderTopWidth: 1,
    borderTopColor: colors.cardBorder,
  },
  recurringActionBtn: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    height: 36,
    borderRadius: radii.sm,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
  },
  recurringActionText: {
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.ink,
  },
  recurringDeleteBtn: {
    backgroundColor: colors.expenseSoft,
    borderColor: '#FECACA',
  },
  recurringDeleteText: {
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.expense,
  },
});
