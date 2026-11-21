import * as Haptics from 'expo-haptics';
import { useFocusEffect, useLocalSearchParams, useRouter } from 'expo-router';
import { AlertCircle, ArrowRight, Delete, FileUp, Repeat } from 'lucide-react-native';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Switch,
  Text,
  TextInput,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { toApiError } from '@/api/client';
import {
  createTransaction,
  getCategories,
  getWallets,
  recurringApi,
} from '@/api/endpoints';
import type {
  CategoryDto,
  TransactionType,
  WalletDto,
} from '@/api/types';
import {
  AppButton,
  CategoryIcon,
  MoneyText,
  SurfaceCard,
} from '@/components/ui';
import { StatementImportModal } from '@/components/transactions/StatementImportModal';
import { CreateWalletModal } from '@/components/wallets/CreateWalletModal';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import {
  formatMoney,
  parseMoneyInput,
  toIsoCalendarDate,
} from '@/utils/formatters';
import { generateUuidV4 } from '@/utils/uuid';

const NUMPAD_ROWS = [
  ['1', '2', '3'],
  ['4', '5', '6'],
  ['7', '8', '9'],
  [',', '0', 'backspace'],
] as const;

const MODES: TransactionType[] = ['Expense', 'Income', 'Transfer'];
const INSTALLMENT_OPTIONS = [1, 2, 3, 6, 9, 12] as const;

function triggerLightHaptic() {
  if (Platform.OS !== 'web') {
    void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
  }
}

type DurationChoice = '12' | '6' | '24' | 'indefinite' | 'custom';

export default function AddTransactionScreen() {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { colors: themeColors, isDark } = useTheme();
  const router = useRouter();
  const params = useLocalSearchParams<{ recurring?: string }>();
  const { activeWorkspace, activeTheme } = useWorkspace();

  const todayDay = new Date().getDate();

  const [mode, setMode] = useState<TransactionType>('Expense');
  const [rawAmount, setRawAmount] = useState('0');
  const [noteInput, setNoteInput] = useState('');
  const [installmentCount, setInstallmentCount] = useState<number>(1);

  // Recurring monthly cycle state (only for Expense / Income)
  const [isRecurring, setIsRecurring] = useState(params.recurring === '1');
  const [durationChoice, setDurationChoice] = useState<DurationChoice>('12');
  const [customMonthsInput, setCustomMonthsInput] = useState('');
  const [dayOfMonthInput, setDayOfMonthInput] = useState(String(todayDay));
  const [executeFirstImmediately, setExecuteFirstImmediately] = useState(true);

  useEffect(() => {
    if (params.recurring === '1') {
      setIsRecurring(true);
      setMode((prev) => (prev === 'Transfer' ? 'Expense' : prev));
    }
  }, [params.recurring]);

  const [wallets, setWallets] = useState<WalletDto[]>([]);
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [selectedWalletId, setSelectedWalletId] = useState<string | null>(null);
  const [selectedTargetWalletId, setSelectedTargetWalletId] = useState<string | null>(null);
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);

  const [isLoadingMeta, setIsLoadingMeta] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [createWalletOpen, setCreateWalletOpen] = useState(false);
  const [importModalOpen, setImportModalOpen] = useState(false);

  const workspaceId = activeWorkspace?.id;
  const currencyCode = activeWorkspace?.defaultCurrency ?? 'TRY';

  const loadMetadata = useCallback(async () => {
    if (!workspaceId) {
      setIsLoadingMeta(false);
      return;
    }

    setIsLoadingMeta(true);
    try {
      const [walletsRes, categoriesRes] = await Promise.all([
        getWallets(workspaceId, false),
        getCategories(workspaceId),
      ]);

      const activeWallets = walletsRes.filter((w) => !w.isArchived);
      setWallets(activeWallets);
      setCategories(categoriesRes);

      setSelectedWalletId((prev) =>
        prev && activeWallets.some((w) => w.id === prev)
          ? prev
          : (activeWallets[0]?.id ?? null),
      );
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    } finally {
      setIsLoadingMeta(false);
    }
  }, [workspaceId]);

  useFocusEffect(
    useCallback(() => {
      void loadMetadata();
    }, [loadMetadata]),
  );

  const filteredCategories = useMemo(() => {
    if (mode === 'Transfer') return [];
    return categories.filter((c) => c.type === mode);
  }, [categories, mode]);

  const targetWallets = useMemo(
    () => wallets.filter((w) => w.id !== selectedWalletId),
    [wallets, selectedWalletId],
  );

  const effectiveCategoryId = useMemo(() => {
    if (mode === 'Transfer') return null;
    if (
      selectedCategoryId &&
      filteredCategories.some((c) => c.id === selectedCategoryId)
    ) {
      return selectedCategoryId;
    }
    return filteredCategories[0]?.id ?? null;
  }, [mode, selectedCategoryId, filteredCategories]);

  const effectiveTargetWalletId = useMemo(() => {
    if (mode !== 'Transfer') return null;
    if (
      selectedTargetWalletId &&
      targetWallets.some((w) => w.id === selectedTargetWalletId)
    ) {
      return selectedTargetWalletId;
    }
    return targetWallets[0]?.id ?? null;
  }, [mode, selectedTargetWalletId, targetWallets]);

  const numericAmount = useMemo(() => parseMoneyInput(rawAmount), [rawAmount]);

  const sourceWallet = useMemo(
    () => wallets.find((w) => w.id === selectedWalletId) ?? null,
    [wallets, selectedWalletId],
  );

  const transferMaxAmount = useMemo(() => {
    if (!sourceWallet) return 0;
    return Math.max(0, Math.round(sourceWallet.currentBalance * 100) / 100);
  }, [sourceWallet]);

  const transferExceedsBalance =
    mode === 'Transfer' &&
    sourceWallet !== null &&
    numericAmount > transferMaxAmount;

  const transferLimitMessage =
    transferExceedsBalance && sourceWallet
      ? t('addTransaction.validation.transferExceedsBalance', {
          amount: formatMoney(transferMaxAmount, sourceWallet.currencyCode),
        })
      : null;

  const displayedError = transferLimitMessage ?? errorMessage;

  const handleModeChange = (nextMode: TransactionType) => {
    triggerLightHaptic();
    setMode(nextMode);
    if (nextMode === 'Transfer') {
      setIsRecurring(false);
    }
    if (nextMode !== 'Expense') {
      setInstallmentCount(1);
    }
    setErrorMessage(null);
  };

  const handleSetMaxTransfer = () => {
    if (transferMaxAmount <= 0) return;
    triggerLightHaptic();
    setErrorMessage(null);
    const formattedRaw = Number.isInteger(transferMaxAmount)
      ? String(transferMaxAmount)
      : transferMaxAmount.toFixed(2).replace('.', ',');
    setRawAmount(formattedRaw);
  };

  const handleNumPadKey = (key: string) => {
    triggerLightHaptic();
    setErrorMessage(null);

    if (key === 'backspace') {
      setRawAmount((prev) => {
        if (prev.length <= 1) return '0';
        return prev.slice(0, -1);
      });
      return;
    }

    if (key === ',') {
      setRawAmount((prev) => {
        if (prev.includes(',')) return prev;
        return `${prev},`;
      });
      return;
    }

    setRawAmount((prev) => {
      const [intPart = '0', decPart] = prev.split(',');
      if (decPart !== undefined) {
        if (decPart.length >= 2) return prev;
        return `${intPart},${decPart}${key}`;
      }

      if (intPart === '0') {
        return key;
      }
      if (intPart.length >= 9) {
        return prev;
      }
      return `${intPart}${key}`;
    });
  };

  const handleSubmit = async () => {
    if (!workspaceId) return;
    setErrorMessage(null);

    if (numericAmount <= 0) {
      setErrorMessage(t('addTransaction.validation.amountRequired'));
      return;
    }

    if (!selectedWalletId) {
      setErrorMessage(t('addTransaction.validation.walletRequired'));
      return;
    }

    if (mode === 'Transfer') {
      if (!effectiveTargetWalletId || effectiveTargetWalletId === selectedWalletId) {
        setErrorMessage(t('addTransaction.validation.targetWalletRequired'));
        return;
      }
      if (sourceWallet && numericAmount > transferMaxAmount) {
        setErrorMessage(
          t('addTransaction.validation.transferExceedsBalance', {
            amount: formatMoney(transferMaxAmount, sourceWallet.currencyCode),
          }),
        );
        return;
      }
    } else if (!effectiveCategoryId) {
      setErrorMessage(t('addTransaction.validation.categoryRequired'));
      return;
    }

    const isRecurringSubmit = isRecurring && mode !== 'Transfer';
    let resolvedTotalOccurrences: number | null = null;
    let resolvedDayOfMonth = todayDay;

    if (isRecurringSubmit) {
      resolvedDayOfMonth = Number.parseInt(dayOfMonthInput.trim(), 10);
      if (
        !Number.isInteger(resolvedDayOfMonth) ||
        resolvedDayOfMonth < 1 ||
        resolvedDayOfMonth > 31
      ) {
        setErrorMessage(t('addTransaction.validation.recurringDayInvalid'));
        return;
      }

      if (durationChoice === 'indefinite') {
        resolvedTotalOccurrences = null;
      } else {
        const rawMonths =
          durationChoice === 'custom'
            ? Number.parseInt(customMonthsInput.trim(), 10)
            : Number.parseInt(durationChoice, 10);

        if (!Number.isInteger(rawMonths) || rawMonths < 1 || rawMonths > 120) {
          setErrorMessage(t('addTransaction.validation.recurringOccurrencesInvalid'));
          return;
        }
        resolvedTotalOccurrences = rawMonths;
      }
    }

    setIsSubmitting(true);
    try {
      const trimmedNote = noteInput.trim();
      if (isRecurringSubmit && (mode === 'Expense' || mode === 'Income')) {
        const selectedCategory = filteredCategories.find(
          (c) => c.id === effectiveCategoryId,
        );
        const resolvedTitle = trimmedNote || selectedCategory?.name || '';
        if (!resolvedTitle) {
          setErrorMessage(t('addTransaction.validation.recurringTitleRequired'));
          setIsSubmitting(false);
          return;
        }

        await recurringApi.create(workspaceId, {
          walletId: selectedWalletId,
          categoryId: effectiveCategoryId!,
          type: mode,
          amount: numericAmount,
          title: resolvedTitle,
          dayOfMonth: resolvedDayOfMonth,
          totalOccurrences: resolvedTotalOccurrences,
          executeFirstImmediately,
        });
      } else {
        await createTransaction(
          workspaceId,
          {
            type: mode,
            walletId: selectedWalletId,
            counterpartyWalletId:
              mode === 'Transfer' ? effectiveTargetWalletId : null,
            categoryId: mode === 'Transfer' ? null : effectiveCategoryId,
            amount: numericAmount,
            currencyCode,
            transactionDate: toIsoCalendarDate(new Date()),
            merchantName: trimmedNote || null,
            notes: null,
            installmentCount: mode === 'Expense' ? installmentCount : 1,
          },
          generateUuidV4(),
        );
      }

      if (Platform.OS !== 'web') {
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      }

      setRawAmount('0');
      setNoteInput('');
      setIsRecurring(false);
      setInstallmentCount(1);
      router.navigate('/(tabs)');
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const resolvedDurationPreview =
    durationChoice === 'indefinite'
      ? null
      : durationChoice === 'custom'
        ? Number.parseInt(customMonthsInput.trim(), 10) || null
        : Number.parseInt(durationChoice, 10);

  const submitLabel =
    isRecurring && mode !== 'Transfer'
      ? t('addTransaction.submitRecurring')
      : mode === 'Income'
        ? t('addTransaction.submitIncome')
        : mode === 'Transfer'
          ? t('addTransaction.submitTransfer')
          : t('addTransaction.submitExpense');

  const isDecimalPending = rawAmount.includes(',');

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      style={[
        styles.screen,
        {
          backgroundColor: themeColors.canvas,
          paddingTop: Math.max(insets.top, spacing.base) + spacing.xs,
        },
      ]}
    >
      {/* Header & Statement Import Action */}
      <View style={styles.header}>
        <Text style={[styles.title, { color: themeColors.ink }]}>
          {t('addTransaction.title')}
        </Text>
        <Pressable
          onPress={() => setImportModalOpen(true)}
          hitSlop={6}
          style={({ pressed }) => [
            styles.workspaceTag,
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
            numberOfLines={1}
            style={[
              styles.workspaceTagText,
              { color: isDark ? '#34D399' : activeTheme.primarySurface },
            ]}
          >
            {t('transactions.importButton')}
          </Text>
        </Pressable>
      </View>

      {/* Segmented Mode Switcher */}
      <View
        style={[
          styles.segmentedWrap,
          isDark && {
            backgroundColor: '#131B29',
            borderColor: 'rgba(148, 163, 184, 0.16)',
          },
        ]}
      >
        {MODES.map((m) => {
          const selected = mode === m;
          return (
            <Pressable
              key={m}
              onPress={() => handleModeChange(m)}
              style={[
                styles.segmentButton,
                selected && {
                  backgroundColor: isDark
                    ? '#10B981'
                    : activeTheme.primarySurface,
                },
              ]}
            >
              <Text
                style={[
                  styles.segmentLabel,
                  isDark && !selected && { color: '#E2E8F0' },
                  selected && styles.segmentLabelSelected,
                ]}
              >
                {t(`addTransaction.modes.${m}`)}
              </Text>
            </Pressable>
          );
        })}
      </View>

      <ScrollView
        showsVerticalScrollIndicator={false}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={styles.scrollBody}
      >
        {/* Hero Amount Display */}
        <View style={styles.amountDisplayWrap}>
          <MoneyText
            amount={numericAmount}
            currency={currencyCode}
            size="hero"
            color={
              transferExceedsBalance
                ? isDark
                  ? '#F87171'
                  : colors.expense
                : mode === 'Income'
                  ? isDark
                    ? '#34D399'
                    : colors.income
                  : mode === 'Transfer'
                    ? isDark
                      ? '#94A3B8'
                      : colors.transfer
                    : isDark
                      ? '#F8FAFC'
                      : colors.ink
            }
          />
          {mode === 'Transfer' && sourceWallet ? (
            <View style={styles.transferBalanceRow}>
              <Text
                style={[
                  styles.transferBalanceText,
                  typography.tabularNums,
                  isDark && { color: '#94A3B8' },
                ]}
              >
                {t('addTransaction.transferAvailable', {
                  amount: formatMoney(
                    transferMaxAmount,
                    sourceWallet.currencyCode,
                  ),
                })}
              </Text>
              {transferMaxAmount > 0 ? (
                <Pressable
                  onPress={handleSetMaxTransfer}
                  hitSlop={6}
                  style={({ pressed }) => [
                    styles.maxPill,
                    {
                      backgroundColor: isDark
                        ? 'rgba(16, 185, 129, 0.14)'
                        : activeTheme.softTint,
                      borderColor: isDark
                        ? 'rgba(52, 211, 153, 0.28)'
                        : activeTheme.softTintBorder,
                      opacity: pressed ? 0.78 : 1,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.maxPillText,
                      { color: isDark ? '#34D399' : activeTheme.primarySurface },
                    ]}
                  >
                    {t('addTransaction.transferMaxButton')}
                  </Text>
                </Pressable>
              ) : null}
            </View>
          ) : (
            <Text
              style={[
                styles.rawTypingHint,
                typography.tabularNums,
                isDark && { color: '#94A3B8' },
              ]}
            >
              {isDecimalPending ? rawAmount : ' '}
            </Text>
          )}
        </View>

        {displayedError ? (
          <View
            style={[
              styles.errorBanner,
              isDark && {
                backgroundColor: themeColors.expenseSoft,
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
              {displayedError}
            </Text>
          </View>
        ) : null}

        {isLoadingMeta ? (
          <View style={styles.loadingWrap}>
            <ActivityIndicator
              size="small"
              color={isDark ? '#34D399' : activeTheme.primarySurface}
            />
          </View>
        ) : wallets.length === 0 ? (
          <SurfaceCard padding="lg" style={styles.noWalletsCard}>
            <Text
              style={[styles.noWalletsText, isDark && { color: '#CBD5E1' }]}
            >
              {t('addTransaction.noWalletsMessage')}
            </Text>
            <AppButton
              label={t('wallets.addWalletButton')}
              onPress={() => setCreateWalletOpen(true)}
              primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
            />
          </SurfaceCard>
        ) : (
          <View style={styles.selectorsBlock}>
            {/* Source Wallet Strip */}
            <View style={styles.selectorGroup}>
              <Text
                style={[styles.selectorLabel, isDark && { color: '#94A3B8' }]}
              >
                {t('addTransaction.walletLabel')}
              </Text>
              <ScrollView
                horizontal
                showsHorizontalScrollIndicator={false}
                contentContainerStyle={styles.chipRow}
              >
                {wallets.map((wallet) => {
                  const selected = wallet.id === selectedWalletId;
                  const isNegative = wallet.currentBalance < 0;
                  return (
                    <Pressable
                      key={wallet.id}
                      onPress={() => {
                        triggerLightHaptic();
                        setSelectedWalletId(wallet.id);
                      }}
                      style={[
                        styles.pillChip,
                        isDark &&
                          !selected && {
                            backgroundColor: '#131B29',
                            borderColor: 'rgba(148, 163, 184, 0.16)',
                          },
                        selected && {
                          backgroundColor: isDark
                            ? '#182234'
                            : activeTheme.softTint,
                          borderColor: isDark
                            ? '#10B981'
                            : activeTheme.primarySurface,
                        },
                      ]}
                    >
                      <Text
                        style={[
                          styles.pillChipTitle,
                          isDark && { color: '#F8FAFC' },
                          selected && {
                            color: isDark
                              ? '#F8FAFC'
                              : activeTheme.primarySurface,
                            fontWeight: '600',
                          },
                        ]}
                      >
                        {wallet.name}
                      </Text>
                      <Text
                        style={[
                          styles.pillChipSub,
                          typography.tabularNums,
                          isDark && {
                            color: isNegative
                              ? '#F87171'
                              : selected
                                ? '#34D399'
                                : '#94A3B8',
                          },
                          !isDark &&
                            selected && { color: activeTheme.primarySurface },
                        ]}
                      >
                        {formatMoney(wallet.currentBalance, wallet.currencyCode)}
                      </Text>
                    </Pressable>
                  );
                })}
              </ScrollView>
            </View>

            {/* Category Strip OR Target Wallet Strip (Transfer) */}
            {mode === 'Transfer' ? (
              <View style={styles.selectorGroup}>
                <View style={styles.transferLabelRow}>
                  <ArrowRight
                    size={13}
                    color={isDark ? '#94A3B8' : colors.inkSecondary}
                    strokeWidth={1.75}
                  />
                  <Text
                    style={[
                      styles.selectorLabel,
                      isDark && { color: '#94A3B8' },
                    ]}
                  >
                    {t('addTransaction.targetWalletLabel')}
                  </Text>
                </View>
                <ScrollView
                  horizontal
                  showsHorizontalScrollIndicator={false}
                  contentContainerStyle={styles.chipRow}
                >
                  {targetWallets.map((wallet) => {
                    const selected = wallet.id === effectiveTargetWalletId;
                    const isNegative = wallet.currentBalance < 0;
                    return (
                      <Pressable
                        key={wallet.id}
                        onPress={() => {
                          triggerLightHaptic();
                          setSelectedTargetWalletId(wallet.id);
                        }}
                        style={[
                          styles.pillChip,
                          isDark &&
                            !selected && {
                              backgroundColor: '#131B29',
                              borderColor: 'rgba(148, 163, 184, 0.16)',
                            },
                          selected && {
                            backgroundColor: isDark
                              ? '#182234'
                              : activeTheme.softTint,
                            borderColor: isDark
                              ? '#10B981'
                              : activeTheme.primarySurface,
                          },
                        ]}
                      >
                        <Text
                          style={[
                            styles.pillChipTitle,
                            isDark && { color: '#F8FAFC' },
                            selected && {
                              color: isDark
                                ? '#F8FAFC'
                                : activeTheme.primarySurface,
                              fontWeight: '600',
                            },
                          ]}
                        >
                          {wallet.name}
                        </Text>
                        <Text
                          style={[
                            styles.pillChipSub,
                            typography.tabularNums,
                            isDark && {
                              color: isNegative
                                ? '#F87171'
                                : selected
                                  ? '#34D399'
                                  : '#94A3B8',
                            },
                            !isDark &&
                              selected && { color: activeTheme.primarySurface },
                          ]}
                        >
                          {formatMoney(wallet.currentBalance, wallet.currencyCode)}
                        </Text>
                      </Pressable>
                    );
                  })}
                </ScrollView>
              </View>
            ) : (
              <View style={styles.selectorGroup}>
                <Text
                  style={[styles.selectorLabel, isDark && { color: '#94A3B8' }]}
                >
                  {t('addTransaction.categoryLabel')}
                </Text>
                <View style={styles.categoryChipWrap}>
                  {filteredCategories.map((cat) => {
                    const selected = cat.id === effectiveCategoryId;
                    return (
                      <Pressable
                        key={cat.id}
                        onPress={() => {
                          triggerLightHaptic();
                          setSelectedCategoryId(cat.id);
                        }}
                        style={[
                          styles.categoryChip,
                          isDark &&
                            !selected && {
                              backgroundColor: '#131B29',
                              borderColor: 'rgba(148, 163, 184, 0.16)',
                            },
                          selected && {
                            backgroundColor: isDark
                              ? '#10B981'
                              : activeTheme.softTint,
                            borderColor: isDark
                              ? '#10B981'
                              : activeTheme.primarySurface,
                          },
                        ]}
                      >
                        <CategoryIcon
                          iconName={cat.icon}
                          size={15}
                          color={
                            selected
                              ? isDark
                                ? '#FFFFFF'
                                : activeTheme.primarySurface
                              : isDark
                                ? '#E2E8F0'
                                : colors.inkSecondary
                          }
                        />
                        <Text
                          style={[
                            styles.categoryChipText,
                            isDark && !selected && { color: '#E2E8F0' },
                            selected && {
                              color: isDark
                                ? '#FFFFFF'
                                : activeTheme.primarySurface,
                              fontWeight: '600',
                            },
                          ]}
                        >
                          {cat.name}
                        </Text>
                      </Pressable>
                    );
                  })}
                </View>
              </View>
            )}

            {/* Installment Selector (Expense Mode Only) */}
            {mode === 'Expense' && !isRecurring ? (
              <View style={styles.selectorGroup}>
                <Text
                  style={[styles.selectorLabel, isDark && { color: '#94A3B8' }]}
                >
                  {t('addTransaction.installmentLabel')}
                </Text>
                <ScrollView
                  horizontal
                  showsHorizontalScrollIndicator={false}
                  contentContainerStyle={styles.chipRow}
                >
                  {INSTALLMENT_OPTIONS.map((count) => {
                    const selected = installmentCount === count;
                    const chipLabel =
                      count === 1
                        ? t('addTransaction.installmentCash')
                        : String(count);
                    return (
                      <Pressable
                        key={count}
                        onPress={() => {
                          triggerLightHaptic();
                          setInstallmentCount(count);
                        }}
                        style={[
                          styles.installmentChip,
                          isDark &&
                            !selected && {
                              backgroundColor: '#131B29',
                              borderColor: 'rgba(148, 163, 184, 0.16)',
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
                            styles.installmentChipText,
                            typography.tabularNums,
                            isDark && !selected && { color: '#E2E8F0' },
                            selected && styles.installmentChipTextSelected,
                          ]}
                        >
                          {chipLabel}
                        </Text>
                      </Pressable>
                    );
                  })}
                </ScrollView>
                {installmentCount > 1 && numericAmount > 0 ? (
                  <Text
                    style={[
                      styles.installmentPreviewText,
                      typography.tabularNums,
                      { color: isDark ? '#34D399' : activeTheme.primarySurface },
                    ]}
                  >
                    {t('addTransaction.installmentPreview', {
                      count: installmentCount,
                      amount: formatMoney(
                        numericAmount / installmentCount,
                        sourceWallet?.currencyCode ?? currencyCode,
                      ),
                    })}
                  </Text>
                ) : null}
              </View>
            ) : null}

            {/* Minimalist Single-Line Note / Merchant / Recurring Title Input */}
            <View style={styles.noteInputWrap}>
              <TextInput
                value={noteInput}
                onChangeText={setNoteInput}
                placeholder={
                  isRecurring && mode !== 'Transfer'
                    ? t('addTransaction.recurringTitlePlaceholder')
                    : t('addTransaction.notePlaceholder')
                }
                placeholderTextColor={isDark ? '#94A3B8' : colors.inkSubtle}
                selectionColor={isDark ? '#34D399' : activeTheme.primarySurface}
                returnKeyType="done"
                style={[
                  styles.noteInput,
                  isDark && {
                    backgroundColor: '#131B29',
                    borderColor: 'rgba(148, 163, 184, 0.15)',
                    color: '#F8FAFC',
                  },
                ]}
              />
            </View>
          </View>
        )}

        {/* Tactile 3x4 NumPad */}
        <View style={styles.numPad}>
          {NUMPAD_ROWS.map((row, rowIndex) => (
            <View key={rowIndex} style={styles.numPadRow}>
              {row.map((key) => {
                const isBackspace = key === 'backspace';
                return (
                  <Pressable
                    key={key}
                    onPress={() => handleNumPadKey(key)}
                    accessibilityRole="button"
                    accessibilityLabel={isBackspace ? 'Backspace' : key}
                    style={({ pressed }) => [
                      styles.numKey,
                      isDark && {
                        backgroundColor: pressed ? '#182234' : '#131B29',
                        borderColor: 'rgba(148, 163, 184, 0.15)',
                      },
                      !isDark && pressed && styles.numKeyPressed,
                    ]}
                  >
                    {isBackspace ? (
                      <Delete
                        size={21}
                        color={isDark ? '#F8FAFC' : colors.ink}
                        strokeWidth={1.75}
                      />
                    ) : (
                      <Text
                        style={[
                          styles.numKeyText,
                          typography.tabularNums,
                          isDark && { color: '#F8FAFC' },
                        ]}
                      >
                        {key}
                      </Text>
                    )}
                  </Pressable>
                );
              })}
            </View>
          ))}
        </View>

        {/* Recurring Monthly Cycle Toggle (Expense / Income Only) */}
        {mode !== 'Transfer' && wallets.length > 0 ? (
          <View style={styles.recurringSection}>
            <Pressable
              onPress={() => {
                triggerLightHaptic();
                setIsRecurring((prev) => {
                  const next = !prev;
                  if (next) {
                    setInstallmentCount(1);
                  }
                  return next;
                });
              }}
              style={({ pressed }) => [
                styles.recurringToggleRow,
                isDark &&
                  !isRecurring && {
                    backgroundColor: '#131B29',
                    borderColor: 'rgba(148, 163, 184, 0.15)',
                  },
                isRecurring && {
                  backgroundColor: isDark ? '#182234' : activeTheme.softTint,
                  borderColor: isDark ? '#10B981' : activeTheme.softTintBorder,
                },
                pressed && { opacity: 0.9 },
              ]}
            >
              <View
                style={[
                  styles.recurringIconBox,
                  {
                    backgroundColor: isRecurring
                      ? isDark
                        ? '#10B981'
                        : activeTheme.primarySurface
                      : isDark
                        ? '#182234'
                        : colors.surfaceSubtle,
                  },
                ]}
              >
                <Repeat
                  size={16}
                  color={
                    isRecurring
                      ? '#FFFFFF'
                      : isDark
                        ? '#94A3B8'
                        : colors.inkSecondary
                  }
                  strokeWidth={1.75}
                />
              </View>
              <View style={styles.recurringToggleTextGroup}>
                <Text
                  style={[
                    styles.recurringToggleTitle,
                    isDark && { color: '#F8FAFC' },
                  ]}
                >
                  {t('addTransaction.recurringToggleTitle')}
                </Text>
                <Text
                  style={[
                    styles.recurringToggleSubtitle,
                    isDark && { color: '#94A3B8' },
                  ]}
                >
                  {t('addTransaction.recurringToggleSubtitle')}
                </Text>
              </View>
              <Switch
                value={isRecurring}
                onValueChange={(val) => {
                  triggerLightHaptic();
                  setIsRecurring(val);
                }}
                trackColor={{
                  false: isDark ? '#2B3340' : colors.border,
                  true: isDark ? '#10B981' : activeTheme.primarySurface,
                }}
                thumbColor="#FFFFFF"
              />
            </Pressable>

            {isRecurring ? (
              <SurfaceCard
                variant="subtle"
                padding="lg"
                style={styles.recurringConfigCard}
              >
                {/* 1. Duration Pills */}
                <View style={styles.configBlock}>
                  <Text
                    style={[
                      styles.configLabel,
                      isDark && { color: '#94A3B8' },
                    ]}
                  >
                    {t('addTransaction.recurringDurationLabel')}
                  </Text>
                  <View style={styles.configPillsWrap}>
                    {(
                      [
                        { key: '12', label: t('addTransaction.recurringDurations.m12') },
                        { key: '6', label: t('addTransaction.recurringDurations.m6') },
                        { key: '24', label: t('addTransaction.recurringDurations.m24') },
                        {
                          key: 'indefinite',
                          label: t('addTransaction.recurringDurations.indefinite'),
                        },
                      ] as const
                    ).map((opt) => {
                      const selected = durationChoice === opt.key;
                      return (
                        <Pressable
                          key={opt.key}
                          onPress={() => {
                            triggerLightHaptic();
                            setDurationChoice(opt.key);
                          }}
                          style={[
                            styles.configPill,
                            isDark &&
                              !selected && {
                                backgroundColor: '#131B29',
                                borderColor: 'rgba(148, 163, 184, 0.16)',
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
                              styles.configPillText,
                              isDark && !selected && { color: '#E2E8F0' },
                              selected && styles.configPillTextSelected,
                            ]}
                          >
                            {opt.label}
                          </Text>
                        </Pressable>
                      );
                    })}

                    <TextInput
                      value={customMonthsInput}
                      onChangeText={(txt) => {
                        setCustomMonthsInput(txt.replace(/[^0-9]/g, ''));
                        setDurationChoice('custom');
                      }}
                      onFocus={() => setDurationChoice('custom')}
                      placeholder={t('addTransaction.recurringCustomMonthsPlaceholder')}
                      placeholderTextColor={isDark ? '#94A3B8' : colors.inkSubtle}
                      keyboardType="number-pad"
                      maxLength={3}
                      style={[
                        styles.compactInput,
                        typography.tabularNums,
                        isDark && {
                          backgroundColor: '#131B29',
                          borderColor: 'rgba(148, 163, 184, 0.16)',
                          color: '#F8FAFC',
                        },
                        durationChoice === 'custom' && {
                          borderColor: isDark
                            ? '#10B981'
                            : activeTheme.primarySurface,
                          backgroundColor: isDark ? '#182234' : colors.surface,
                        },
                      ]}
                    />
                  </View>
                </View>

                {/* 2. Day of Month */}
                <View style={styles.configBlock}>
                  <Text
                    style={[
                      styles.configLabel,
                      isDark && { color: '#94A3B8' },
                    ]}
                  >
                    {t('addTransaction.recurringDayLabel')}
                  </Text>
                  <View style={styles.configPillsWrap}>
                    {(
                      [
                        { value: '1', label: '1' },
                        { value: '5', label: '5' },
                        { value: '15', label: '15' },
                        {
                          value: String(todayDay),
                          label: t('addTransaction.recurringTodayPill', {
                            day: todayDay,
                          }),
                        },
                        {
                          value: '31',
                          label: t('addTransaction.recurringLastDayPill'),
                        },
                      ] as const
                    ).map((dayOpt) => {
                      const selected = dayOfMonthInput === dayOpt.value;
                      return (
                        <Pressable
                          key={`${dayOpt.label}-${dayOpt.value}`}
                          onPress={() => {
                            triggerLightHaptic();
                            setDayOfMonthInput(dayOpt.value);
                          }}
                          style={[
                            styles.configPill,
                            isDark &&
                              !selected && {
                                backgroundColor: '#131B29',
                                borderColor: 'rgba(148, 163, 184, 0.16)',
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
                              styles.configPillText,
                              typography.tabularNums,
                              isDark && !selected && { color: '#E2E8F0' },
                              selected && styles.configPillTextSelected,
                            ]}
                          >
                            {dayOpt.label}
                          </Text>
                        </Pressable>
                      );
                    })}

                    <TextInput
                      value={dayOfMonthInput}
                      onChangeText={(txt) =>
                        setDayOfMonthInput(txt.replace(/[^0-9]/g, ''))
                      }
                      placeholder={t('addTransaction.recurringDayPlaceholder')}
                      placeholderTextColor={isDark ? '#94A3B8' : colors.inkSubtle}
                      keyboardType="number-pad"
                      maxLength={2}
                      style={[
                        styles.compactInput,
                        typography.tabularNums,
                        isDark && {
                          backgroundColor: '#131B29',
                          borderColor: 'rgba(148, 163, 184, 0.16)',
                          color: '#F8FAFC',
                        },
                      ]}
                    />
                  </View>
                </View>

                {/* 3. Execute First Immediately */}
                <View style={styles.executeNowRow}>
                  <View style={styles.executeNowTextCol}>
                    <Text
                      style={[
                        styles.executeNowTitle,
                        isDark && { color: '#F8FAFC' },
                      ]}
                    >
                      {t('addTransaction.recurringExecuteNowTitle')}
                    </Text>
                    <Text
                      style={[
                        styles.executeNowHint,
                        isDark && { color: '#94A3B8' },
                      ]}
                    >
                      {!executeFirstImmediately
                        ? t('addTransaction.recurringDeferredHint')
                        : resolvedDurationPreview && resolvedDurationPreview > 1
                          ? t('addTransaction.recurringExecuteNowFiniteHint', {
                              amount: formatMoney(numericAmount, currencyCode),
                              total: resolvedDurationPreview,
                              remaining: resolvedDurationPreview - 1,
                            })
                          : t('addTransaction.recurringExecuteNowIndefiniteHint', {
                              amount: formatMoney(numericAmount, currencyCode),
                            })}
                    </Text>
                  </View>
                  <Switch
                    value={executeFirstImmediately}
                    onValueChange={(val) => {
                      triggerLightHaptic();
                      setExecuteFirstImmediately(val);
                    }}
                    trackColor={{
                      false: isDark ? '#2B3340' : colors.border,
                      true: isDark ? '#10B981' : activeTheme.primarySurface,
                    }}
                    thumbColor="#FFFFFF"
                  />
                </View>
              </SurfaceCard>
            ) : null}
          </View>
        ) : null}

        <View style={styles.submitWrap}>
          <AppButton
            label={submitLabel}
            onPress={() => void handleSubmit()}
            loading={isSubmitting}
            disabled={wallets.length === 0 || transferExceedsBalance}
            primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
          />
        </View>
      </ScrollView>

      <CreateWalletModal
        visible={createWalletOpen}
        onClose={() => setCreateWalletOpen(false)}
        onCreated={(created) => {
          setSelectedWalletId(created.id);
          void loadMetadata();
        }}
      />

      <StatementImportModal
        visible={importModalOpen}
        wallets={wallets}
        defaultWalletId={selectedWalletId}
        onClose={() => setImportModalOpen(false)}
        onImported={() => {
          void loadMetadata();
          router.navigate('/(tabs)');
        }}
      />
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: colors.canvas,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.md,
  },
  title: {
    ...typography.h2,
  },
  workspaceTag: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: radii.full,
    borderWidth: 1,
    maxWidth: 170,
  },
  workspaceDot: {
    width: 6,
    height: 6,
    borderRadius: radii.full,
  },
  workspaceTagText: {
    fontSize: 12,
    fontWeight: '600',
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
  segmentButton: {
    flex: 1,
    height: 36,
    borderRadius: radii.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  segmentLabel: {
    fontSize: 13.5,
    fontWeight: '600',
    color: colors.inkSecondary,
  },
  segmentLabelSelected: {
    color: colors.inkOnDark,
  },
  scrollBody: {
    paddingBottom: spacing.xl,
  },
  amountDisplayWrap: {
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.xl,
    paddingVertical: spacing.sm,
  },
  rawTypingHint: {
    fontSize: 12,
    color: colors.inkSubtle,
    marginTop: 2,
    height: 16,
  },
  transferBalanceRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    marginTop: spacing.xs + 2,
  },
  transferBalanceText: {
    fontSize: 12.5,
    fontWeight: '500',
    color: colors.inkSecondary,
  },
  maxPill: {
    paddingHorizontal: 10,
    paddingVertical: 3,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  maxPillText: {
    fontSize: 11.5,
    fontWeight: '700',
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
  loadingWrap: {
    paddingVertical: spacing.xl,
    alignItems: 'center',
  },
  noWalletsCard: {
    marginHorizontal: spacing.xl,
    marginBottom: spacing.base,
    gap: spacing.md,
  },
  noWalletsText: {
    ...typography.body,
    color: colors.inkSecondary,
  },
  selectorsBlock: {
    gap: spacing.md,
    marginBottom: spacing.base,
  },
  selectorGroup: {
    gap: spacing.xs + 2,
  },
  selectorLabel: {
    ...typography.overline,
    paddingHorizontal: spacing.xl,
  },
  transferLabelRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: spacing.xl,
    gap: 4,
  },
  chipRow: {
    paddingHorizontal: spacing.xl,
    gap: spacing.sm,
  },
  categoryChipWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    paddingHorizontal: spacing.xl,
    gap: spacing.sm,
  },
  installmentChip: {
    minWidth: 48,
    paddingHorizontal: spacing.md,
    paddingVertical: 7,
    borderRadius: radii.full,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  installmentChipText: {
    fontSize: 13,
    fontWeight: '600',
    color: colors.inkSecondary,
  },
  installmentChipTextSelected: {
    color: '#FFFFFF',
    fontWeight: '700',
  },
  installmentPreviewText: {
    paddingHorizontal: spacing.xl,
    fontSize: 12.5,
    fontWeight: '600',
    marginTop: 2,
  },
  pillChip: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
    gap: 2,
  },
  pillChipTitle: {
    fontSize: 13.5,
    fontWeight: '500',
    color: colors.ink,
  },
  pillChipSub: {
    fontSize: 11.5,
    color: colors.inkSecondary,
  },
  categoryChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    paddingHorizontal: spacing.md,
    paddingVertical: 9,
    borderRadius: radii.full,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
  },
  categoryChipText: {
    fontSize: 13.5,
    fontWeight: '500',
    color: colors.ink,
  },
  noteInputWrap: {
    paddingHorizontal: spacing.xl,
  },
  noteInput: {
    height: 44,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
    paddingHorizontal: spacing.base,
    fontSize: 14,
    color: colors.ink,
  },
  numPad: {
    paddingHorizontal: spacing.xl,
    gap: spacing.sm,
    marginBottom: spacing.base,
  },
  numPadRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  numKey: {
    flex: 1,
    height: 52,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.cardBorder,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  numKeyPressed: {
    backgroundColor: colors.surfaceSubtle,
  },
  numKeyText: {
    fontSize: 21,
    fontWeight: '600',
    color: colors.ink,
  },
  submitWrap: {
    paddingHorizontal: spacing.xl,
  },
  recurringSection: {
    paddingHorizontal: spacing.xl,
    marginBottom: spacing.base,
    gap: spacing.sm,
  },
  recurringToggleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingHorizontal: spacing.base,
    paddingVertical: spacing.md,
    borderRadius: radii.lg,
    borderWidth: 1,
    borderColor: colors.cardBorder,
    backgroundColor: colors.surface,
  },
  recurringIconBox: {
    width: 34,
    height: 34,
    borderRadius: radii.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  recurringToggleTextGroup: {
    flex: 1,
    gap: 2,
  },
  recurringToggleTitle: {
    fontSize: 13.5,
    fontWeight: '600',
    color: colors.ink,
  },
  recurringToggleSubtitle: {
    fontSize: 11.5,
    color: colors.inkSecondary,
  },
  recurringConfigCard: {
    gap: spacing.base,
  },
  configBlock: {
    gap: spacing.sm,
  },
  configLabel: {
    ...typography.overline,
  },
  configPillsWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs + 2,
    alignItems: 'center',
  },
  configPill: {
    paddingHorizontal: 11,
    paddingVertical: 7,
    borderRadius: radii.full,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
  },
  configPillText: {
    fontSize: 12.5,
    fontWeight: '500',
    color: colors.ink,
  },
  configPillTextSelected: {
    color: '#FFFFFF',
    fontWeight: '600',
  },
  compactInput: {
    width: 72,
    height: 33,
    borderRadius: radii.full,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surface,
    paddingHorizontal: 10,
    fontSize: 12.5,
    color: colors.ink,
    textAlign: 'center',
  },
  executeNowRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
    paddingTop: spacing.sm,
    borderTopWidth: 1,
    borderTopColor: colors.border,
  },
  executeNowTextCol: {
    flex: 1,
    gap: 3,
  },
  executeNowTitle: {
    fontSize: 13.5,
    fontWeight: '600',
    color: colors.ink,
  },
  executeNowHint: {
    fontSize: 12,
    lineHeight: 17,
    color: colors.inkSecondary,
  },
});
