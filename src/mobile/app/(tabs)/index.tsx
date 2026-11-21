import { router, useFocusEffect } from 'expo-router';
import { AlertCircle, Settings, Trash2 } from 'lucide-react-native';
import React, { useCallback, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ActivityIndicator,
  Alert,
  Modal,
  Platform,
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
  analyticsApi,
  budgetsApi,
  transactionsApi,
  walletsApi,
} from '@/api/endpoints';
import type {
  AnalyticsSummaryDto,
  BudgetDto,
  TransactionDto,
  WalletDto,
} from '@/api/types';
import { BudgetStatusCard } from '@/components/dashboard/BudgetStatusCard';
import { NetBalanceCard } from '@/components/dashboard/NetBalanceCard';
import { RecentTransactionsCard } from '@/components/dashboard/RecentTransactionsCard';
import { WalletStrip } from '@/components/dashboard/WalletStrip';
import { MonthSelector, WorkspacePillSwitcher } from '@/components/ui';
import { CreateWalletModal } from '@/components/wallets/CreateWalletModal';
import { useAuth } from '@/context/AuthContext';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import { getMonthRange } from '@/utils/formatters';

export default function DashboardScreen() {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { user } = useAuth();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();

  const [wallets, setWallets] = useState<WalletDto[]>([]);
  const [analytics, setAnalytics] = useState<AnalyticsSummaryDto | null>(null);
  const [budgets, setBudgets] = useState<BudgetDto[]>([]);
  const [recentTransactions, setRecentTransactions] = useState<TransactionDto[]>([]);

  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [createWalletOpen, setCreateWalletOpen] = useState(false);
  const [walletToDelete, setWalletToDelete] = useState<WalletDto | null>(null);
  const [isDeletingWallet, setIsDeletingWallet] = useState(false);

  const initialDate = new Date();
  const [selectedYear, setSelectedYear] = useState(initialDate.getFullYear());
  const [selectedMonth, setSelectedMonth] = useState(initialDate.getMonth() + 1);

  const loadedWorkspaceRef = useRef<string | null>(null);
  const workspaceId = activeWorkspace?.id;
  const currencyCode = activeWorkspace?.defaultCurrency ?? 'TRY';

  const loadDashboardData = useCallback(
    async (
      mode: 'initial' | 'refresh' = 'initial',
      targetYear: number = selectedYear,
      targetMonth: number = selectedMonth,
    ) => {
      if (!workspaceId) {
        setIsLoading(false);
        return;
      }

      if (mode === 'initial') {
        setIsLoading(true);
      } else if (mode === 'refresh') {
        setIsRefreshing(true);
      }
      setErrorMessage(null);

      try {
        const monthBounds = getMonthRange(targetYear, targetMonth);
        const [walletsRes, analyticsRes, budgetsRes, txPageRes] = await Promise.all([
          walletsApi.list(workspaceId, false),
          analyticsApi.getSummary(
            workspaceId,
            monthBounds.dateFrom,
            monthBounds.dateTo,
            targetYear,
            targetMonth,
          ),
          budgetsApi.list(workspaceId, targetYear, targetMonth),
          transactionsApi.list(workspaceId, {
            dateFrom: monthBounds.dateFrom,
            dateTo: monthBounds.dateTo,
            pageNumber: 1,
            pageSize: 6,
          }),
        ]);

        setWallets(walletsRes);
        setAnalytics(analyticsRes);
        setBudgets(budgetsRes);
        setRecentTransactions(txPageRes.items);
        loadedWorkspaceRef.current = workspaceId;
      } catch (err) {
        setErrorMessage(toApiError(err).message);
      } finally {
        setIsLoading(false);
        setIsRefreshing(false);
      }
    },
    [workspaceId, selectedYear, selectedMonth],
  );

  useFocusEffect(
    useCallback(() => {
      const isNewWorkspace = loadedWorkspaceRef.current !== workspaceId;
      void loadDashboardData(isNewWorkspace ? 'initial' : 'refresh');
    }, [loadDashboardData, workspaceId]),
  );

  const handleMonthChange = useCallback(
    (next: { year: number; month: number }) => {
      setSelectedYear(next.year);
      setSelectedMonth(next.month);
    },
    [],
  );

  const totalNetBalance = wallets
    .filter((w) => !w.isArchived)
    .reduce((sum, w) => sum + w.currentBalance, 0);

  const handleConfirmDeleteWallet = useCallback(async () => {
    if (!workspaceId || !walletToDelete || isDeletingWallet) {
      return;
    }

    setIsDeletingWallet(true);
    try {
      await walletsApi.deleteWallet(workspaceId, walletToDelete.id);
      setWalletToDelete(null);
      await loadDashboardData('refresh');
    } catch (err) {
      const apiErr = toApiError(err);
      setWalletToDelete(null);
      if (Platform.OS === 'web') {
        window.alert(`${t('wallets.deleteErrorTitle')}\n\n${apiErr.message}`);
      } else {
        Alert.alert(t('wallets.deleteErrorTitle'), apiErr.message);
      }
    } finally {
      setIsDeletingWallet(false);
    }
  }, [isDeletingWallet, loadDashboardData, t, walletToDelete, workspaceId]);

  const firstName = user?.displayName.trim().split(/\s+/)[0] ?? '';

  return (
    <View style={[styles.screen, { backgroundColor: themeColors.canvas }]}>
      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={[
          styles.scrollContent,
          {
            paddingTop: Math.max(insets.top, spacing.base) + spacing.sm,
            paddingBottom: spacing.xxxl,
          },
        ]}
        refreshControl={
          <RefreshControl
            refreshing={isRefreshing}
            onRefresh={() => void loadDashboardData('refresh')}
            tintColor={isDark ? activeTheme.accentHighlight : activeTheme.primarySurface}
          />
        }
      >
        {/* Top Header: Workspace on Left, Single Settings Gear on Right */}
        <View style={styles.topHeader}>
          <WorkspacePillSwitcher />

          <Pressable
            onPress={() => router.push('/settings')}
            accessibilityRole="button"
            accessibilityLabel={t('tabs.settings')}
            style={({ pressed }) => [
              styles.settingsButton,
              {
                borderColor: themeColors.border,
                backgroundColor: themeColors.surface,
                opacity: pressed ? 0.8 : 1,
              },
            ]}
          >
            <Settings size={18} color={themeColors.ink} strokeWidth={1.75} />
          </Pressable>
        </View>

        {/* Quiet Editorial Greeting */}
        {firstName ? (
          <View style={styles.greetingRow}>
            <Text style={[styles.greetingText, { color: themeColors.ink }]}>
              {t('dashboard.greeting', { name: firstName })}
            </Text>
          </View>
        ) : null}

        {/* Network / API Error Banner */}
        {errorMessage ? (
          <View style={styles.errorContainer}>
            <View
              style={[
                styles.errorBanner,
                {
                  backgroundColor: themeColors.expenseSoft,
                  borderColor: isDark ? '#7F1D1D' : '#FECACA',
                },
              ]}
            >
              <AlertCircle size={17} color={themeColors.expense} strokeWidth={1.75} />
              <Text style={[styles.errorText, { color: themeColors.expense }]}>
                {errorMessage}
              </Text>
              <Pressable
                onPress={() => void loadDashboardData('initial')}
                hitSlop={8}
              >
                <Text style={[styles.retryText, { color: themeColors.expense }]}>
                  {t('common.retry')}
                </Text>
              </Pressable>
            </View>
          </View>
        ) : null}

        {isLoading && !isRefreshing ? (
          <View style={styles.loadingState}>
            <ActivityIndicator size="large" color={activeTheme.primarySurface} />
          </View>
        ) : (
          <View style={styles.sections}>
            {/* Month Selector + Hero Net Balance Card */}
            <View style={[styles.heroPadding, styles.heroStack]}>
              <MonthSelector
                year={selectedYear}
                month={selectedMonth}
                onChange={handleMonthChange}
              />
              <NetBalanceCard
                totalBalance={totalNetBalance}
                monthlyIncome={analytics?.totalIncome ?? 0}
                monthlyExpense={analytics?.totalExpense ?? 0}
                savingsRatePercent={analytics?.savingsRatePercent ?? 0}
                currency={analytics?.currencyCode ?? currencyCode}
              />
            </View>

            {/* Horizontal Wallets Strip */}
            <WalletStrip
              wallets={wallets}
              onAddWalletPress={() => setCreateWalletOpen(true)}
              onDeleteWalletPress={(wallet) => setWalletToDelete(wallet)}
              deletingWalletId={isDeletingWallet ? walletToDelete?.id : null}
            />

            {/* Compact Budget Progress Summary */}
            <BudgetStatusCard budgets={budgets} />

            {/* Recent Transactions Feed */}
            <RecentTransactionsCard
              transactions={recentTransactions}
              onViewAllPress={() => router.push('/transactions')}
            />
          </View>
        )}
      </ScrollView>

      {/* Create Wallet Modal */}
      <CreateWalletModal
        visible={createWalletOpen}
        onClose={() => setCreateWalletOpen(false)}
        onCreated={() => void loadDashboardData('refresh')}
      />

      {/* Delete Wallet Confirmation Modal */}
      <Modal
        visible={walletToDelete !== null}
        transparent
        animationType="fade"
        onRequestClose={() => {
          if (!isDeletingWallet) {
            setWalletToDelete(null);
          }
        }}
      >
        <Pressable
          style={styles.confirmBackdrop}
          onPress={() => {
            if (!isDeletingWallet) {
              setWalletToDelete(null);
            }
          }}
        >
          <Pressable
            onPress={() => {}}
            style={[
              styles.confirmCard,
              {
                backgroundColor: themeColors.surface,
                borderColor: themeColors.border,
              },
            ]}
          >
            <View
              style={[
                styles.confirmIconBadge,
                {
                  backgroundColor: themeColors.expenseSoft,
                  borderColor: isDark
                    ? 'rgba(248, 113, 113, 0.28)'
                    : '#FECACA',
                },
              ]}
            >
              <Trash2 size={20} color={themeColors.expense} strokeWidth={1.75} />
            </View>

            <Text style={[styles.confirmTitle, { color: themeColors.ink }]}>
              {t('wallets.deleteTitle')}
            </Text>

            <Text
              style={[
                styles.confirmBody,
                { color: themeColors.inkSecondary },
              ]}
            >
              {walletToDelete
                ? t('wallets.deleteConfirmMessage', {
                    name: walletToDelete.name,
                  })
                : ''}
            </Text>

            <View style={styles.confirmActions}>
              <Pressable
                onPress={() => setWalletToDelete(null)}
                disabled={isDeletingWallet}
                style={({ pressed }) => [
                  styles.confirmButton,
                  {
                    backgroundColor: themeColors.surfaceSubtle,
                    borderColor: themeColors.border,
                    opacity: pressed || isDeletingWallet ? 0.7 : 1,
                  },
                ]}
              >
                <Text
                  style={[
                    styles.confirmCancelText,
                    { color: themeColors.ink },
                  ]}
                >
                  {t('wallets.deleteCancel')}
                </Text>
              </Pressable>

              <Pressable
                onPress={() => void handleConfirmDeleteWallet()}
                disabled={isDeletingWallet}
                style={({ pressed }) => [
                  styles.confirmButton,
                  {
                    backgroundColor: themeColors.expense,
                    borderColor: themeColors.expense,
                    opacity: pressed || isDeletingWallet ? 0.8 : 1,
                  },
                ]}
              >
                {isDeletingWallet ? (
                  <ActivityIndicator size="small" color="#FFFFFF" />
                ) : (
                  <Text style={styles.confirmDeleteText}>
                    {t('wallets.deleteConfirm')}
                  </Text>
                )}
              </Pressable>
            </View>
          </Pressable>
        </Pressable>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: colors.canvas,
  },
  scrollContent: {
    flexGrow: 1,
  },
  topHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.base,
  },
  settingsButton: {
    width: 40,
    height: 40,
    borderRadius: radii.full,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  greetingRow: {
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.base,
  },
  greetingText: {
    ...typography.h2,
  },
  errorContainer: {
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.base,
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
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
  loadingState: {
    paddingVertical: spacing.xxxl * 2,
    alignItems: 'center',
    justifyContent: 'center',
  },
  sections: {
    gap: spacing.xl,
  },
  heroPadding: {
    paddingHorizontal: spacing.xl,
  },
  heroStack: {
    gap: spacing.md,
  },
  confirmBackdrop: {
    flex: 1,
    backgroundColor: 'rgba(11, 15, 23, 0.58)',
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.xl,
  },
  confirmCard: {
    width: '100%',
    maxWidth: 360,
    borderRadius: radii.xl,
    borderWidth: 1,
    padding: spacing.xl,
    alignItems: 'center',
    gap: spacing.sm,
  },
  confirmIconBadge: {
    width: 44,
    height: 44,
    borderRadius: radii.full,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.xs,
  },
  confirmTitle: {
    ...typography.h3,
    textAlign: 'center',
  },
  confirmBody: {
    ...typography.body,
    textAlign: 'center',
    marginBottom: spacing.md,
  },
  confirmActions: {
    flexDirection: 'row',
    gap: spacing.sm,
    width: '100%',
  },
  confirmButton: {
    flex: 1,
    height: 44,
    borderRadius: radii.md,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  confirmCancelText: {
    fontSize: 14,
    fontWeight: '600',
  },
  confirmDeleteText: {
    fontSize: 14,
    fontWeight: '600',
    color: '#FFFFFF',
  },
});
