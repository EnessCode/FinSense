import * as Haptics from 'expo-haptics';
import { router, useFocusEffect } from 'expo-router';
import {
  AlertCircle,
  ArrowLeft,
  FileUp,
  Search,
  Trash2,
  X,
} from 'lucide-react-native';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ActivityIndicator,
  Modal,
  Platform,
  Pressable,
  RefreshControl,
  ScrollView,
  SectionList,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { toApiError } from '@/api/client';
import {
  deleteTransaction,
  getTransactions,
  getWallets,
} from '@/api/endpoints';
import type {
  TransactionDto,
  TransactionType,
  WalletDto,
} from '@/api/types';
import {
  AppButton,
  CategoryIcon,
  MoneyText,
  SurfaceCard,
  WorkspacePillSwitcher,
} from '@/components/ui';
import { StatementImportModal } from '@/components/transactions/StatementImportModal';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { getActiveLocale } from '@/i18n';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import { formatDateShort, formatRelativeDate } from '@/utils/formatters';

type TypeFilter = 'All' | TransactionType;

const TYPE_FILTERS: TypeFilter[] = ['All', 'Expense', 'Income', 'Transfer'];

interface TransactionSection {
  title: string;
  isoDate: string;
  data: TransactionDto[];
}

export default function TransactionsScreen() {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();
  const locale = getActiveLocale();

  const workspaceId = activeWorkspace?.id;
  const isHousehold = activeWorkspace?.type === 'Household';

  const [searchInput, setSearchInput] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [typeFilter, setTypeFilter] = useState<TypeFilter>('All');
  const [walletFilterId, setWalletFilterId] = useState<string | null>(null);

  const [wallets, setWallets] = useState<WalletDto[]>([]);
  const [transactions, setTransactions] = useState<TransactionDto[]>([]);
  const [pageNumber, setPageNumber] = useState(1);
  const [hasNextPage, setHasNextPage] = useState(false);

  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const [selectedTx, setSelectedTx] = useState<TransactionDto | null>(null);
  const [isDeleting, setIsDeleting] = useState(false);
  const [importModalOpen, setImportModalOpen] = useState(false);

  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(searchInput.trim());
    }, 300);
    return () => clearTimeout(timer);
  }, [searchInput]);

  useEffect(() => {
    setWalletFilterId(null);
  }, [workspaceId]);

  const fetchFeed = useCallback(
    async (page: number, mode: 'initial' | 'refresh' | 'more') => {
      if (!workspaceId) {
        setIsLoading(false);
        return;
      }

      if (mode === 'initial') setIsLoading(true);
      if (mode === 'refresh') setIsRefreshing(true);
      if (mode === 'more') setIsLoadingMore(true);
      setErrorMessage(null);

      try {
        const [txRes, walletsRes] = await Promise.all([
          getTransactions(workspaceId, {
            pageNumber: page,
            pageSize: 25,
            type: typeFilter === 'All' ? undefined : typeFilter,
            walletId: walletFilterId ?? undefined,
            search: debouncedSearch || undefined,
          }),
          page === 1 ? getWallets(workspaceId, false) : Promise.resolve(null),
        ]);

        if (walletsRes) {
          setWallets(walletsRes.filter((w) => !w.isArchived));
        }

        setTransactions((prev) =>
          page === 1 ? txRes.items : [...prev, ...txRes.items],
        );
        setPageNumber(txRes.pageNumber);
        setHasNextPage(txRes.hasNextPage);
      } catch (err) {
        setErrorMessage(toApiError(err).message);
      } finally {
        setIsLoading(false);
        setIsRefreshing(false);
        setIsLoadingMore(false);
      }
    },
    [workspaceId, typeFilter, walletFilterId, debouncedSearch],
  );

  useFocusEffect(
    useCallback(() => {
      void fetchFeed(1, 'initial');
    }, [fetchFeed]),
  );

  const sections = useMemo<TransactionSection[]>(() => {
    const groups = new Map<string, TransactionDto[]>();

    for (const tx of transactions) {
      const dateKey = tx.transactionDate.slice(0, 10);
      const bucket = groups.get(dateKey);
      if (bucket) {
        bucket.push(tx);
      } else {
        groups.set(dateKey, [tx]);
      }
    }

    return Array.from(groups.entries()).map(([isoDate, data]) => {
      const rel = formatRelativeDate(isoDate, locale);
      const shortDate = formatDateShort(isoDate);
      const title =
        rel === 'Bugün' || rel === 'Dün' || rel === 'Today' || rel === 'Yesterday'
          ? `${rel} • ${shortDate}`
          : shortDate;
      return { title, isoDate, data };
    });
  }, [transactions, locale]);

  const handleLoadMore = () => {
    if (!hasNextPage || isLoadingMore || isLoading) return;
    void fetchFeed(pageNumber + 1, 'more');
  };

  const handleDeleteSelected = async () => {
    if (!workspaceId || !selectedTx) return;
    setIsDeleting(true);
    try {
      await deleteTransaction(workspaceId, selectedTx.id);
      if (Platform.OS !== 'web') {
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      }
      setTransactions((prev) => prev.filter((item) => item.id !== selectedTx.id));
      setSelectedTx(null);
    } catch (err) {
      setErrorMessage(toApiError(err).message);
      setSelectedTx(null);
    } finally {
      setIsDeleting(false);
    }
  };

  return (
    <View
      style={[
        styles.screen,
        {
          backgroundColor: themeColors.canvas,
          paddingTop: spacing.xs,
        },
      ]}
    >
      {/* Workspace Bar + Statement Import Button */}
      <View style={styles.topHeader}>
        <WorkspacePillSwitcher />
        <Pressable
          onPress={() => setImportModalOpen(true)}
          hitSlop={6}
          style={({ pressed }) => [
            styles.importPill,
            {
              backgroundColor: isDark
                ? 'rgba(16, 185, 129, 0.14)'
                : activeTheme.softTint,
              borderColor: isDark
                ? 'rgba(52, 211, 153, 0.28)'
                : activeTheme.softTintBorder,
              opacity: pressed ? 0.8 : 1,
            },
          ]}
        >
          <FileUp
            size={14}
            color={isDark ? '#34D399' : activeTheme.primarySurface}
            strokeWidth={1.85}
          />
          <Text
            style={[
              styles.importPillText,
              { color: isDark ? '#34D399' : activeTheme.primarySurface },
            ]}
          >
            {t('transactions.importButton')}
          </Text>
        </Pressable>
      </View>

      {/* Search Bar */}
      <View style={styles.searchWrap}>
        <View
          style={[
            styles.searchBox,
            isDark && {
              backgroundColor: '#182234',
              borderColor: 'rgba(148, 163, 184, 0.22)',
            },
          ]}
        >
          <Search
            size={17}
            color={isDark ? '#94A3B8' : colors.inkSubtle}
            strokeWidth={1.75}
          />
          <TextInput
            value={searchInput}
            onChangeText={setSearchInput}
            placeholder={t('transactions.searchPlaceholder')}
            placeholderTextColor={isDark ? '#94A3B8' : colors.inkSubtle}
            selectionColor={isDark ? '#34D399' : activeTheme.primarySurface}
            returnKeyType="search"
            style={[styles.searchInput, isDark && { color: '#F8FAFC' }]}
          />
          {searchInput.length > 0 ? (
            <Pressable onPress={() => setSearchInput('')} hitSlop={8}>
              <X
                size={16}
                color={isDark ? '#94A3B8' : colors.inkSecondary}
                strokeWidth={1.75}
              />
            </Pressable>
          ) : null}
        </View>
      </View>

      {/* Type Filter Chips */}
      <View style={styles.filtersGroup}>
        <ScrollView
          horizontal
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={styles.filterRow}
        >
          {TYPE_FILTERS.map((filter) => {
            const selected = typeFilter === filter;
            const label =
              filter === 'All'
                ? t('common.all')
                : t(`transactions.types.${filter}`);
            return (
              <Pressable
                key={filter}
                onPress={() => setTypeFilter(filter)}
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
                  ]}
                >
                  {label}
                </Text>
              </Pressable>
            );
          })}
        </ScrollView>

        {/* Wallet Filter Pills */}
        {wallets.length > 0 ? (
          <ScrollView
            horizontal
            showsHorizontalScrollIndicator={false}
            contentContainerStyle={styles.filterRow}
          >
            <Pressable
              onPress={() => setWalletFilterId(null)}
              style={[
                styles.walletPill,
                isDark &&
                  walletFilterId !== null && {
                    backgroundColor: '#182234',
                    borderColor: 'rgba(148, 163, 184, 0.22)',
                  },
                walletFilterId === null && {
                  backgroundColor: isDark
                    ? 'rgba(16, 185, 129, 0.16)'
                    : activeTheme.softTint,
                  borderColor: isDark
                    ? '#10B981'
                    : activeTheme.primarySurface,
                },
              ]}
            >
              <Text
                style={[
                  styles.walletPillText,
                  isDark && walletFilterId !== null && { color: '#CBD5E1' },
                  walletFilterId === null && {
                    color: isDark ? '#34D399' : activeTheme.primarySurface,
                    fontWeight: '600',
                  },
                ]}
              >
                {t('transactions.filterAllWallets')}
              </Text>
            </Pressable>

            {wallets.map((w) => {
              const selected = walletFilterId === w.id;
              return (
                <Pressable
                  key={w.id}
                  onPress={() => setWalletFilterId(w.id)}
                  style={[
                    styles.walletPill,
                    isDark &&
                      !selected && {
                        backgroundColor: '#182234',
                        borderColor: 'rgba(148, 163, 184, 0.22)',
                      },
                    selected && {
                      backgroundColor: isDark
                        ? 'rgba(16, 185, 129, 0.16)'
                        : activeTheme.softTint,
                      borderColor: isDark
                        ? '#10B981'
                        : activeTheme.primarySurface,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.walletPillText,
                      isDark && !selected && { color: '#CBD5E1' },
                      selected && {
                        color: isDark ? '#34D399' : activeTheme.primarySurface,
                        fontWeight: '600',
                      },
                    ]}
                  >
                    {w.name}
                  </Text>
                </Pressable>
              );
            })}
          </ScrollView>
        ) : null}
      </View>

      {errorMessage ? (
        <View
          style={[
            styles.errorBanner,
            isDark && {
              backgroundColor: 'rgba(248, 113, 113, 0.14)',
              borderColor: 'rgba(248, 113, 113, 0.3)',
            },
          ]}
        >
          <AlertCircle
            size={16}
            color={isDark ? '#F87171' : colors.expense}
            strokeWidth={1.75}
          />
          <Text style={[styles.errorText, isDark && { color: '#F87171' }]}>
            {errorMessage}
          </Text>
          <Pressable onPress={() => void fetchFeed(1, 'initial')} hitSlop={8}>
            <Text style={[styles.retryText, isDark && { color: '#F87171' }]}>
              {t('common.retry')}
            </Text>
          </Pressable>
        </View>
      ) : null}

      {isLoading ? (
        <View style={styles.loadingWrap}>
          <ActivityIndicator
            size="large"
            color={isDark ? '#34D399' : activeTheme.primarySurface}
          />
        </View>
      ) : (
        <SectionList
          sections={sections}
          keyExtractor={(item) => item.id}
          stickySectionHeadersEnabled={false}
          showsVerticalScrollIndicator={false}
          contentContainerStyle={styles.listContent}
          refreshControl={
            <RefreshControl
              refreshing={isRefreshing}
              onRefresh={() => void fetchFeed(1, 'refresh')}
              tintColor={isDark ? '#34D399' : activeTheme.primarySurface}
            />
          }
          onEndReached={handleLoadMore}
          onEndReachedThreshold={0.35}
          ListEmptyComponent={
            <SurfaceCard padding="xl" style={styles.emptyCard}>
              <Text
                style={[styles.emptyTitle, isDark && { color: '#F8FAFC' }]}
              >
                {t('transactions.emptyFilteredTitle')}
              </Text>
              <Text
                style={[styles.emptyBody, isDark && { color: '#CBD5E1' }]}
              >
                {t('transactions.emptyFilteredBody')}
              </Text>
            </SurfaceCard>
          }
          renderSectionHeader={({ section }) => (
            <Text
              style={[
                styles.sectionHeader,
                typography.tabularNums,
                isDark && { color: '#94A3B8' },
              ]}
            >
              {section.title}
            </Text>
          )}
          renderItem={({ item, index, section }) => {
            const isFirst = index === 0;
            const isLast = index === section.data.length - 1;
            const isIncome = item.type === 'Income';
            const isTransfer = item.type === 'Transfer';

            const title =
              item.merchantName?.trim() ||
              (isTransfer
                ? t('transactions.transferFallback')
                : item.categoryName || t('transactions.uncategorized'));

            const memberAttribution =
              item.paidByDisplayName || item.createdByDisplayName;

            const iconBg = isIncome
              ? themeColors.incomeSoft
              : isTransfer
                ? themeColors.transferSoft
                : themeColors.surfaceSubtle;

            const iconColor = isIncome
              ? themeColors.income
              : isTransfer
                ? themeColors.transfer
                : themeColors.ink;

            const amountColor = isIncome
              ? themeColors.income
              : isTransfer
                ? themeColors.transfer
                : themeColors.expenseRowInk;

            const displayAmount =
              item.type === 'Expense' ? -Math.abs(item.amount) : item.amount;

            const walletLine =
              isTransfer && item.counterpartyWalletName
                ? `${item.walletName} → ${item.counterpartyWalletName}`
                : item.categoryName && item.merchantName
                  ? `${item.categoryName} • ${item.walletName}`
                  : item.walletName;

            return (
              <Pressable
                onPress={() => setSelectedTx(item)}
                style={({ pressed }) => [
                  styles.txRow,
                  isDark && {
                    backgroundColor: '#131B29',
                    borderColor: 'rgba(255, 255, 255, 0.08)',
                  },
                  isFirst && styles.txRowFirst,
                  isLast && styles.txRowLast,
                  !isFirst && [
                    styles.txRowDivider,
                    isDark && { borderTopColor: 'rgba(255, 255, 255, 0.08)' },
                  ],
                  pressed &&
                    (isDark
                      ? { backgroundColor: '#182234' }
                      : styles.txRowPressed),
                ]}
              >
                <View
                  style={[
                    styles.iconBox,
                    {
                      backgroundColor: iconBg,
                      borderColor: themeColors.border,
                    },
                  ]}
                >
                  <CategoryIcon
                    iconName={item.categoryIcon}
                    transactionType={item.type}
                    size={18}
                    color={iconColor}
                  />
                </View>

                <View style={styles.txCenter}>
                  <Text
                    style={[styles.txTitle, isDark && { color: '#F8FAFC' }]}
                    numberOfLines={1}
                  >
                    {title}
                  </Text>
                  <View style={styles.txSubRow}>
                    <Text
                      style={[
                        styles.txSubText,
                        isDark && { color: '#94A3B8' },
                      ]}
                      numberOfLines={1}
                    >
                      {walletLine}
                    </Text>
                    {isHousehold && memberAttribution ? (
                      <View
                        style={[
                          styles.memberBadge,
                          {
                            backgroundColor: isDark
                              ? 'rgba(251, 191, 36, 0.14)'
                              : activeTheme.softTint,
                            borderColor: isDark
                              ? 'rgba(251, 191, 36, 0.28)'
                              : activeTheme.softTintBorder,
                          },
                        ]}
                      >
                        <Text
                          numberOfLines={1}
                          style={[
                            styles.memberBadgeText,
                            {
                              color: isDark
                                ? '#FBBF24'
                                : activeTheme.primarySurface,
                            },
                          ]}
                        >
                          {memberAttribution}
                        </Text>
                      </View>
                    ) : null}
                  </View>
                </View>

                <View style={styles.txAmountCol}>
                  <View style={styles.amountInline}>
                    {isTransfer ? (
                      <Text
                        style={[
                          styles.transferPrefix,
                          isDark && { color: '#94A3B8' },
                        ]}
                      >
                        ↔{' '}
                      </Text>
                    ) : null}
                    <MoneyText
                      amount={displayAmount}
                      currency={item.currencyCode}
                      size="md"
                      color={amountColor}
                      showSign={isIncome}
                    />
                  </View>
                </View>
              </Pressable>
            );
          }}
          ListFooterComponent={
            isLoadingMore ? (
              <View style={styles.footerSpinner}>
                <ActivityIndicator
                  size="small"
                  color={isDark ? '#34D399' : activeTheme.primarySurface}
                />
              </View>
            ) : undefined
          }
        />
      )}

      {/* Transaction Detail & Delete Modal */}
      <Modal
        visible={selectedTx !== null}
        transparent
        statusBarTranslucent
        navigationBarTranslucent
        animationType="fade"
        onRequestClose={() => setSelectedTx(null)}
      >
        <View style={styles.modalBackdrop}>
          <Pressable
            style={StyleSheet.absoluteFill}
            onPress={() => setSelectedTx(null)}
          />
          {selectedTx ? (
            <View
              style={[
                styles.sheet,
                isDark && {
                  backgroundColor: '#131B29',
                  borderColor: 'rgba(255, 255, 255, 0.1)',
                },
                { paddingBottom: Math.max(insets.bottom, 20) + spacing.sm },
              ]}
            >
              <View
                style={[
                  styles.sheetHandle,
                  isDark && { backgroundColor: 'rgba(148, 163, 184, 0.3)' },
                ]}
              />

              <View style={styles.sheetHeader}>
                <Text
                  style={[styles.sheetTitle, isDark && { color: '#F8FAFC' }]}
                >
                  {t('transactions.detailTitle')}
                </Text>
                <Pressable
                  onPress={() => setSelectedTx(null)}
                  hitSlop={10}
                  style={[
                    styles.closeButton,
                    isDark && {
                      backgroundColor: '#1E293B',
                      borderColor: 'rgba(148, 163, 184, 0.22)',
                    },
                  ]}
                >
                  <X
                    size={18}
                    color={isDark ? '#E2E8F0' : colors.inkSecondary}
                    strokeWidth={1.75}
                  />
                </Pressable>
              </View>

              <View style={styles.detailHero}>
                <Text
                  style={[
                    styles.detailTypeBadge,
                    isDark && { color: '#94A3B8' },
                  ]}
                >
                  {t(`transactions.types.${selectedTx.type}`)}
                </Text>
                <MoneyText
                  amount={
                    selectedTx.type === 'Expense'
                      ? -Math.abs(selectedTx.amount)
                      : selectedTx.amount
                  }
                  currency={selectedTx.currencyCode}
                  size="xl"
                  showSign={selectedTx.type === 'Income'}
                  color={
                    selectedTx.type === 'Income'
                      ? themeColors.income
                      : selectedTx.type === 'Transfer'
                        ? themeColors.transfer
                        : isDark
                          ? '#F87171'
                          : colors.ink
                  }
                />
                {selectedTx.merchantName ? (
                  <Text
                    style={[
                      styles.detailMerchant,
                      isDark && { color: '#CBD5E1' },
                    ]}
                  >
                    {selectedTx.merchantName}
                  </Text>
                ) : null}
              </View>

              <SurfaceCard padding="lg" variant="subtle" style={styles.detailMetaCard}>
                <View style={styles.detailMetaRow}>
                  <Text
                    style={[
                      styles.detailMetaLabel,
                      isDark && { color: '#94A3B8' },
                    ]}
                  >
                    {t('transactions.detailWallet')}
                  </Text>
                  <Text
                    style={[
                      styles.detailMetaValue,
                      isDark && { color: '#F8FAFC' },
                    ]}
                  >
                    {selectedTx.walletName}
                  </Text>
                </View>

                {selectedTx.counterpartyWalletName ? (
                  <View style={styles.detailMetaRow}>
                    <Text
                      style={[
                        styles.detailMetaLabel,
                        isDark && { color: '#94A3B8' },
                      ]}
                    >
                      {t('transactions.detailTargetWallet')}
                    </Text>
                    <Text
                      style={[
                        styles.detailMetaValue,
                        isDark && { color: '#F8FAFC' },
                      ]}
                    >
                      {selectedTx.counterpartyWalletName}
                    </Text>
                  </View>
                ) : null}

                {selectedTx.categoryName ? (
                  <View style={styles.detailMetaRow}>
                    <Text
                      style={[
                        styles.detailMetaLabel,
                        isDark && { color: '#94A3B8' },
                      ]}
                    >
                      {t('transactions.detailCategory')}
                    </Text>
                    <Text
                      style={[
                        styles.detailMetaValue,
                        isDark && { color: '#F8FAFC' },
                      ]}
                    >
                      {selectedTx.categoryName}
                    </Text>
                  </View>
                ) : null}

                <View style={styles.detailMetaRow}>
                  <Text
                    style={[
                      styles.detailMetaLabel,
                      isDark && { color: '#94A3B8' },
                    ]}
                  >
                    {t('transactions.detailDate')}
                  </Text>
                  <Text
                    style={[
                      styles.detailMetaValue,
                      typography.tabularNums,
                      isDark && { color: '#F8FAFC' },
                    ]}
                  >
                    {formatDateShort(selectedTx.transactionDate)}
                  </Text>
                </View>

                {isHousehold &&
                (selectedTx.paidByDisplayName || selectedTx.createdByDisplayName) ? (
                  <View style={styles.detailMetaRow}>
                    <Text
                      style={[
                        styles.detailMetaLabel,
                        isDark && { color: '#94A3B8' },
                      ]}
                    >
                      {t('transactions.detailPaidBy')}
                    </Text>
                    <Text
                      style={[
                        styles.detailMetaValue,
                        isDark && { color: '#F8FAFC' },
                      ]}
                    >
                      {selectedTx.paidByDisplayName || selectedTx.createdByDisplayName}
                    </Text>
                  </View>
                ) : null}

                {selectedTx.notes ? (
                  <View style={styles.detailMetaRow}>
                    <Text
                      style={[
                        styles.detailMetaLabel,
                        isDark && { color: '#94A3B8' },
                      ]}
                    >
                      {t('transactions.detailNotes')}
                    </Text>
                    <Text
                      style={[
                        styles.detailMetaValue,
                        isDark && { color: '#F8FAFC' },
                      ]}
                    >
                      {selectedTx.notes}
                    </Text>
                  </View>
                ) : null}
              </SurfaceCard>

              <Text style={[styles.deleteHint, isDark && { color: '#94A3B8' }]}>
                {t('transactions.deleteHint')}
              </Text>

              <AppButton
                label={t('transactions.deleteTransaction')}
                variant="danger"
                loading={isDeleting}
                onPress={() => void handleDeleteSelected()}
                leftIcon={
                  <Trash2
                    size={17}
                    color={themeColors.expense}
                    strokeWidth={1.75}
                  />
                }
              />
            </View>
          ) : null}
        </View>
      </Modal>

      <StatementImportModal
        visible={importModalOpen}
        wallets={wallets}
        defaultWalletId={walletFilterId}
        onClose={() => setImportModalOpen(false)}
        onImported={() => void fetchFeed(1, 'refresh')}
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
  importPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
    paddingHorizontal: 11,
    paddingVertical: 6,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  importPillText: {
    fontSize: 12,
    fontWeight: '600',
  },
  titleWithBack: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  backButton: {
    padding: 4,
    marginLeft: -4,
  },
  screenTitle: {
    ...typography.h2,
  },
  searchWrap: {
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.md,
  },
  searchBox: {
    flexDirection: 'row',
    alignItems: 'center',
    height: 46,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
    paddingHorizontal: spacing.base,
    gap: spacing.sm,
  },
  searchInput: {
    flex: 1,
    fontSize: 14.5,
    color: colors.ink,
    paddingVertical: 0,
  },
  filtersGroup: {
    gap: spacing.sm,
    marginBottom: spacing.md,
  },
  filterRow: {
    paddingHorizontal: spacing.xl,
    gap: spacing.sm,
  },
  filterChip: {
    paddingHorizontal: spacing.base,
    paddingVertical: 7,
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
    color: colors.inkOnDark,
    fontWeight: '600',
  },
  walletPill: {
    paddingHorizontal: spacing.md,
    paddingVertical: 6,
    borderRadius: radii.sm,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
  },
  walletPillText: {
    fontSize: 12.5,
    fontWeight: '500',
    color: colors.inkSecondary,
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
  listContent: {
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.xxxl,
  },
  sectionHeader: {
    ...typography.overline,
    marginTop: spacing.base,
    marginBottom: spacing.sm,
  },
  txRow: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: colors.surface,
    borderLeftWidth: 1,
    borderRightWidth: 1,
    borderColor: colors.cardBorder,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.base,
    gap: spacing.md,
  },
  txRowFirst: {
    borderTopWidth: 1,
    borderTopLeftRadius: radii.lg,
    borderTopRightRadius: radii.lg,
  },
  txRowLast: {
    borderBottomWidth: 1,
    borderBottomLeftRadius: radii.lg,
    borderBottomRightRadius: radii.lg,
  },
  txRowDivider: {
    borderTopWidth: 1,
    borderTopColor: colors.cardBorder,
  },
  txRowPressed: {
    backgroundColor: colors.surfaceSubtle,
  },
  iconBox: {
    width: 40,
    height: 40,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  txCenter: {
    flex: 1,
    gap: 3,
  },
  txTitle: {
    ...typography.bodyMedium,
    letterSpacing: -0.1,
  },
  txSubRow: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: 6,
  },
  txSubText: {
    ...typography.caption,
    fontSize: 12.5,
  },
  memberBadge: {
    paddingHorizontal: 7,
    paddingVertical: 2,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  memberBadgeText: {
    fontSize: 11,
    fontWeight: '600',
  },
  txAmountCol: {
    alignItems: 'flex-end',
  },
  amountInline: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  transferPrefix: {
    fontSize: 14,
    fontWeight: '600',
    color: colors.transfer,
  },
  emptyCard: {
    marginTop: spacing.lg,
    gap: spacing.xs,
  },
  emptyTitle: {
    ...typography.bodyMedium,
  },
  emptyBody: {
    ...typography.caption,
  },
  footerSpinner: {
    paddingVertical: spacing.lg,
    alignItems: 'center',
  },
  modalBackdrop: {
    flex: 1,
    backgroundColor: 'rgba(17, 19, 21, 0.42)',
    justifyContent: 'flex-end',
  },
  sheet: {
    width: '100%',
    marginBottom: 0,
    backgroundColor: colors.surface,
    borderTopLeftRadius: radii.xl,
    borderTopRightRadius: radii.xl,
    borderBottomLeftRadius: 0,
    borderBottomRightRadius: 0,
    borderTopWidth: 1,
    borderColor: colors.border,
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.md,
    gap: spacing.base,
  },
  sheetHandle: {
    width: 38,
    height: 4,
    borderRadius: radii.full,
    backgroundColor: colors.border,
    alignSelf: 'center',
  },
  sheetHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  sheetTitle: {
    ...typography.h2,
  },
  closeButton: {
    width: 34,
    height: 34,
    borderRadius: radii.full,
    backgroundColor: colors.surfaceSubtle,
    borderWidth: 1,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  detailHero: {
    alignItems: 'center',
    gap: 6,
    paddingVertical: spacing.sm,
  },
  detailTypeBadge: {
    ...typography.overline,
    color: colors.inkSecondary,
  },
  detailMerchant: {
    ...typography.bodyMedium,
    color: colors.inkSecondary,
  },
  detailMetaCard: {
    gap: spacing.md,
  },
  detailMetaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.base,
  },
  detailMetaLabel: {
    ...typography.caption,
  },
  detailMetaValue: {
    ...typography.bodyMedium,
    fontSize: 14,
    flexShrink: 1,
    textAlign: 'right',
  },
  deleteHint: {
    ...typography.caption,
    textAlign: 'center',
    fontSize: 12,
  },
});
