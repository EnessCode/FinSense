import * as DocumentPicker from 'expo-document-picker';
import {
  AlertCircle,
  Check,
  FileSpreadsheet,
  FileText,
  Upload,
  X,
} from 'lucide-react-native';
import React, { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { toApiError } from '@/api/client';
import {
  confirmStatementImport,
  getCategories,
  previewStatementImport,
} from '@/api/endpoints';
import type {
  CategoryDto,
  ConfirmImportItemPayload,
  ImportPreviewItemDto,
  ImportPreviewResultDto,
  WalletDto,
} from '@/api/types';
import { AppButton, CategoryIcon, MoneyText } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { useKeyboardHeight } from '@/hooks/useKeyboardHeight';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import { formatDateShort, formatMoney } from '@/utils/formatters';

export interface StatementImportModalProps {
  visible: boolean;
  wallets: WalletDto[];
  defaultWalletId?: string | null;
  onClose: () => void;
  onImported: () => void;
}

interface EditableImportRow extends ImportPreviewItemDto {
  selected: boolean;
}

export function StatementImportModal({
  visible,
  wallets,
  defaultWalletId,
  onClose,
  onImported,
}: StatementImportModalProps) {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();
  const keyboardHeight = useKeyboardHeight();

  const workspaceId = activeWorkspace?.id;
  const activeWallets = useMemo(
    () => wallets.filter((w) => !w.isArchived),
    [wallets],
  );

  const [selectedWalletId, setSelectedWalletId] = useState<string | null>(null);
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [preview, setPreview] = useState<ImportPreviewResultDto | null>(null);
  const [rows, setRows] = useState<EditableImportRow[]>([]);
  const [editingCategoryRowIndex, setEditingCategoryRowIndex] = useState<
    number | null
  >(null);

  const [pasteOpen, setPasteOpen] = useState(false);
  const [pastedCsv, setPastedCsv] = useState('');
  const [isParsing, setIsParsing] = useState(false);
  const [isConfirming, setIsConfirming] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!visible) return;
    setPreview(null);
    setRows([]);
    setEditingCategoryRowIndex(null);
    setPasteOpen(false);
    setPastedCsv('');
    setErrorMessage(null);

    const initialWallet =
      (defaultWalletId &&
      activeWallets.some((w) => w.id === defaultWalletId)
        ? defaultWalletId
        : activeWallets[0]?.id) ?? null;
    setSelectedWalletId(initialWallet);

    if (workspaceId) {
      void getCategories(workspaceId)
        .then(setCategories)
        .catch((err) => {
          setErrorMessage(toApiError(err).message);
        });
    }
  }, [visible, defaultWalletId, activeWallets, workspaceId]);

  const accentPrimary = isDark ? '#10B981' : activeTheme.primarySurface;
  const accentText = isDark ? '#34D399' : activeTheme.primarySurface;

  const applyPreviewResult = (res: ImportPreviewResultDto) => {
    setPreview(res);
    setRows(
      res.items.map((item) => ({
        ...item,
        selected: !item.isDuplicate,
      })),
    );
    setEditingCategoryRowIndex(null);
  };

  const runPreviewWithFormData = async (formData: FormData) => {
    if (!workspaceId || !selectedWalletId) return;
    setErrorMessage(null);
    setIsParsing(true);
    try {
      const res = await previewStatementImport(workspaceId, formData);
      applyPreviewResult(res);
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    } finally {
      setIsParsing(false);
    }
  };

  const handleAnalyzePastedCsv = async () => {
    if (!workspaceId || !selectedWalletId || isParsing) return;
    const trimmed = pastedCsv.trim();
    if (!trimmed) return;
    const formData = new FormData();
    formData.append('walletId', selectedWalletId);
    if (Platform.OS === 'web' && typeof Blob !== 'undefined') {
      const blob = new Blob([trimmed], { type: 'text/csv;charset=utf-8' });
      formData.append('file', blob, 'yapistirilan-ekstre.csv');
    } else {
      formData.append('rawCsvText', trimmed);
      formData.append('fileName', 'yapistirilan-ekstre.csv');
    }
    await runPreviewWithFormData(formData);
  };

  const handlePickFile = async () => {
    if (!workspaceId || !selectedWalletId || isParsing) return;
    setErrorMessage(null);

    try {
      const result = await DocumentPicker.getDocumentAsync({
        type: [
          'application/pdf',
          'text/csv',
          'text/comma-separated-values',
          'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
          'application/vnd.ms-excel',
          '*/*',
        ],
        copyToCacheDirectory: true,
      });

      if (result.canceled || !result.assets?.[0]) {
        return;
      }

      const asset = result.assets[0];
      const formData = new FormData();
      formData.append('walletId', selectedWalletId);

      if (Platform.OS === 'web' && asset.file) {
        formData.append('file', asset.file, asset.name || 'statement.pdf');
      } else {
        formData.append('file', {
          uri: asset.uri,
          name: asset.name || 'statement.pdf',
          type: asset.mimeType || 'application/pdf',
        } as unknown as Blob);
      }

      await runPreviewWithFormData(formData);
    } catch (err) {
      const message =
        err instanceof Error && err.message
          ? err.message
          : t('statementImport.pickFileError');
      setErrorMessage(message);
    }
  };

  const toggleRow = (rowIndex: number) => {
    setRows((prev) =>
      prev.map((r) =>
        r.rowIndex === rowIndex ? { ...r, selected: !r.selected } : r,
      ),
    );
  };

  const assignCategoryToRow = (rowIndex: number, cat: CategoryDto) => {
    setRows((prev) =>
      prev.map((r) =>
        r.rowIndex === rowIndex
          ? {
              ...r,
              categoryId: cat.id,
              categoryName: cat.name,
              categoryIcon: cat.icon,
              categoryColorHex: cat.colorHex,
            }
          : r,
      ),
    );
    setEditingCategoryRowIndex(null);
  };

  const selectedRows = useMemo(
    () => rows.filter((r) => r.selected),
    [rows],
  );

  const selectedTotals = useMemo(() => {
    let income = 0;
    let expense = 0;
    for (const r of selectedRows) {
      if (r.type === 'Income') income += r.amount;
      else expense += r.amount;
    }
    return { income, expense };
  }, [selectedRows]);

  const handleConfirmImport = async () => {
    if (!workspaceId || !preview || selectedRows.length === 0 || isConfirming) {
      return;
    }

    const payloadItems: ConfirmImportItemPayload[] = [];
    for (const r of selectedRows) {
      const fallbackCat = categories.find((c) => c.type === r.type);
      const resolvedCatId = r.categoryId ?? fallbackCat?.id;
      if (!resolvedCatId) {
        setErrorMessage(t('addTransaction.validation.categoryRequired'));
        return;
      }
      payloadItems.push({
        transactionDate: r.transactionDate,
        description: r.description,
        type: r.type,
        amount: r.amount,
        categoryId: resolvedCatId,
      });
    }

    setErrorMessage(null);
    setIsConfirming(true);
    try {
      await confirmStatementImport(workspaceId, preview.walletId, payloadItems);
      onImported();
      onClose();
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    } finally {
      setIsConfirming(false);
    }
  };

  return (
    <Modal
      visible={visible}
      transparent
      animationType="slide"
      statusBarTranslucent
      onRequestClose={onClose}
    >
      <KeyboardAvoidingView
        style={styles.backdrop}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <Pressable style={StyleSheet.absoluteFill} onPress={onClose} />
        <View
          style={[
            styles.sheet,
            {
              backgroundColor: themeColors.canvas,
              borderColor: themeColors.border,
              paddingBottom:
                Platform.OS === 'android' && keyboardHeight > 0
                  ? keyboardHeight + spacing.md
                  : Math.max(insets.bottom, 24),
            },
          ]}
        >
          {/* Sheet Header */}
          <View
            style={[
              styles.sheetHeader,
              { borderBottomColor: themeColors.border },
            ]}
          >
            <View style={styles.sheetHeaderText}>
              <Text style={[styles.sheetTitle, { color: themeColors.ink }]}>
                {t('statementImport.title')}
              </Text>
              <Text
                style={[
                  styles.sheetSubtitle,
                  { color: themeColors.inkSecondary },
                ]}
              >
                {t('statementImport.subtitle')}
              </Text>
            </View>
            <Pressable
              onPress={onClose}
              hitSlop={10}
              style={[
                styles.closeButton,
                {
                  backgroundColor: themeColors.surface,
                  borderColor: themeColors.border,
                },
              ]}
            >
              <X size={18} color={themeColors.ink} strokeWidth={1.85} />
            </Pressable>
          </View>

          {errorMessage ? (
            <View
              style={[
                styles.errorBanner,
                {
                  backgroundColor: themeColors.expenseSoft,
                  borderColor: isDark ? '#7F1D1D' : '#FECACA',
                },
              ]}
            >
              <AlertCircle
                size={16}
                color={themeColors.expense}
                strokeWidth={1.75}
              />
              <Text style={[styles.errorText, { color: themeColors.expense }]}>
                {errorMessage}
              </Text>
            </View>
          ) : null}

          {!preview ? (
            <ScrollView
              showsVerticalScrollIndicator={false}
              keyboardShouldPersistTaps="handled"
              contentContainerStyle={styles.uploadContent}
            >
              {activeWallets.length === 0 ? (
                <Text
                  style={[
                    styles.emptyNotice,
                    { color: themeColors.inkSecondary },
                  ]}
                >
                  {t('statementImport.noWalletsNotice')}
                </Text>
              ) : (
                <>
                  {/* Wallet Selector */}
                  <View style={styles.sectionBlock}>
                    <Text
                      style={[
                        styles.sectionLabel,
                        { color: themeColors.inkSecondary },
                      ]}
                    >
                      {t('statementImport.walletLabel')}
                    </Text>
                    <ScrollView
                      horizontal
                      showsHorizontalScrollIndicator={false}
                      contentContainerStyle={styles.walletChipRow}
                    >
                      {activeWallets.map((wallet) => {
                        const selected = wallet.id === selectedWalletId;
                        return (
                          <Pressable
                            key={wallet.id}
                            onPress={() => setSelectedWalletId(wallet.id)}
                            style={[
                              styles.walletChip,
                              {
                                backgroundColor: selected
                                  ? isDark
                                    ? '#182234'
                                    : activeTheme.softTint
                                  : themeColors.surface,
                                borderColor: selected
                                  ? accentPrimary
                                  : themeColors.border,
                              },
                            ]}
                          >
                            <Text
                              style={[
                                styles.walletChipName,
                                {
                                  color: selected
                                    ? accentText
                                    : themeColors.ink,
                                },
                              ]}
                            >
                              {wallet.name}
                            </Text>
                            <Text
                              style={[
                                styles.walletChipBalance,
                                typography.tabularNums,
                                { color: themeColors.inkSecondary },
                              ]}
                            >
                              {formatMoney(
                                wallet.currentBalance,
                                wallet.currencyCode,
                              )}
                            </Text>
                          </Pressable>
                        );
                      })}
                    </ScrollView>
                  </View>

                  {/* File Picker Card */}
                  <Pressable
                    onPress={() => void handlePickFile()}
                    disabled={isParsing || !selectedWalletId}
                    style={({ pressed }) => [
                      styles.uploadDropzone,
                      {
                        backgroundColor: themeColors.surface,
                        borderColor: isDark
                          ? 'rgba(52, 211, 153, 0.34)'
                          : activeTheme.softTintBorder,
                        opacity: pressed || isParsing ? 0.78 : 1,
                      },
                    ]}
                  >
                    <View
                      style={[
                        styles.uploadIconWrap,
                        {
                          backgroundColor: isDark
                            ? 'rgba(16, 185, 129, 0.14)'
                            : activeTheme.softTint,
                        },
                      ]}
                    >
                      {isParsing ? (
                        <ActivityIndicator size="small" color={accentText} />
                      ) : (
                        <Upload size={22} color={accentText} strokeWidth={1.75} />
                      )}
                    </View>
                    <Text
                      style={[
                        styles.uploadPrimaryText,
                        { color: themeColors.ink },
                      ]}
                    >
                      {isParsing
                        ? t('statementImport.parsingLabel')
                        : t('statementImport.pickFileButton')}
                    </Text>
                    <Text
                      style={[
                        styles.uploadHintText,
                        { color: themeColors.inkSecondary },
                      ]}
                    >
                      {t('statementImport.pickFileHint')}
                    </Text>
                  </Pressable>

                  {/* Optional Paste CSV Text Toggle */}
                  <Pressable
                    onPress={() => setPasteOpen((prev) => !prev)}
                    style={styles.pasteToggleBtn}
                  >
                    <FileSpreadsheet
                      size={14}
                      color={themeColors.inkSecondary}
                      strokeWidth={1.75}
                    />
                    <Text
                      style={[
                        styles.pasteToggleText,
                        { color: themeColors.inkSecondary },
                      ]}
                    >
                      {t('statementImport.pasteToggle')}
                    </Text>
                  </Pressable>

                  {pasteOpen ? (
                    <View style={styles.pasteBox}>
                      <TextInput
                        value={pastedCsv}
                        onChangeText={setPastedCsv}
                        multiline
                        numberOfLines={4}
                        placeholder={t('statementImport.pastePlaceholder')}
                        placeholderTextColor={themeColors.inkSubtle}
                        style={[
                          styles.pasteInput,
                          {
                            backgroundColor: themeColors.surface,
                            borderColor: themeColors.border,
                            color: themeColors.ink,
                          },
                        ]}
                      />
                      <AppButton
                        label={t('statementImport.analyzeTextButton')}
                        onPress={() => void handleAnalyzePastedCsv()}
                        loading={isParsing}
                        disabled={!pastedCsv.trim()}
                        primaryColor={accentPrimary}
                      />
                    </View>
                  ) : null}
                </>
              )}
            </ScrollView>
          ) : (
            /* Preview Step */
            <View style={styles.previewContainer}>
              {/* Summary Strip */}
              <View style={styles.summaryRow}>
                <View
                  style={[
                    styles.summaryCard,
                    {
                      backgroundColor: themeColors.surface,
                      borderColor: themeColors.border,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.summaryLabel,
                      { color: themeColors.inkSecondary },
                    ]}
                  >
                    {t('statementImport.summaryIncome')}
                  </Text>
                  <MoneyText
                    amount={selectedTotals.income}
                    currency={preview.currencyCode}
                    size="md"
                    showSign
                    color={themeColors.income}
                  />
                </View>
                <View
                  style={[
                    styles.summaryCard,
                    {
                      backgroundColor: themeColors.surface,
                      borderColor: themeColors.border,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.summaryLabel,
                      { color: themeColors.inkSecondary },
                    ]}
                  >
                    {t('statementImport.summaryExpense')}
                  </Text>
                  <MoneyText
                    amount={-selectedTotals.expense}
                    currency={preview.currencyCode}
                    size="md"
                    color={themeColors.expense}
                  />
                </View>
              </View>

              {/* File Meta & Duplicate Notice */}
              <View style={styles.metaRow}>
                <View style={styles.metaFileBadge}>
                  <FileText
                    size={13}
                    color={accentText}
                    strokeWidth={1.85}
                  />
                  <Text
                    numberOfLines={1}
                    style={[
                      styles.metaFileName,
                      { color: themeColors.inkSecondary },
                    ]}
                  >
                    {preview.fileName} ({preview.detectedFormat}) •{' '}
                    {t('statementImport.summarySelected', {
                      selected: selectedRows.length,
                      total: rows.length,
                    })}
                  </Text>
                </View>
                <Pressable
                  onPress={() => {
                    setPreview(null);
                    setRows([]);
                  }}
                  hitSlop={8}
                >
                  <Text style={[styles.changeFileLink, { color: accentText }]}>
                    {t('statementImport.changeFileButton')}
                  </Text>
                </Pressable>
              </View>

              {preview.duplicateCount > 0 ? (
                <View
                  style={[
                    styles.dupNoticeBanner,
                    {
                      backgroundColor: themeColors.warningSoft,
                      borderColor: isDark
                        ? 'rgba(251, 191, 36, 0.28)'
                        : '#FDE68A',
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.dupNoticeText,
                      { color: themeColors.warning },
                    ]}
                  >
                    {t('statementImport.duplicateNotice', {
                      count: preview.duplicateCount,
                    })}
                  </Text>
                </View>
              ) : null}

              {/* Preview Rows List */}
              <ScrollView
                showsVerticalScrollIndicator={false}
                keyboardShouldPersistTaps="handled"
                contentContainerStyle={styles.previewList}
              >
                {rows.map((row) => {
                  const isEditingCat = editingCategoryRowIndex === row.rowIndex;
                  const matchingCategories = categories.filter(
                    (c) => c.type === row.type,
                  );

                  return (
                    <View
                      key={row.rowIndex}
                      style={[
                        styles.previewItemCard,
                        {
                          backgroundColor: themeColors.surface,
                          borderColor: row.selected
                            ? themeColors.border
                            : isDark
                              ? 'rgba(148, 163, 184, 0.08)'
                              : '#EEECE6',
                          opacity: row.selected ? 1 : 0.62,
                        },
                      ]}
                    >
                      <View style={styles.previewRowTop}>
                        <Pressable
                          onPress={() => toggleRow(row.rowIndex)}
                          hitSlop={8}
                          style={[
                            styles.checkbox,
                            {
                              backgroundColor: row.selected
                                ? accentPrimary
                                : 'transparent',
                              borderColor: row.selected
                                ? accentPrimary
                                : themeColors.inkSubtle,
                            },
                          ]}
                        >
                          {row.selected ? (
                            <Check size={13} color="#FFFFFF" strokeWidth={2.6} />
                          ) : null}
                        </Pressable>

                        <View style={styles.previewRowMain}>
                          <View style={styles.previewTitleLine}>
                            <Text
                              numberOfLines={1}
                              style={[
                                styles.previewDescription,
                                { color: themeColors.ink },
                              ]}
                            >
                              {row.description}
                            </Text>
                            {row.isDuplicate ? (
                              <View
                                style={[
                                  styles.dupBadge,
                                  { backgroundColor: themeColors.warningSoft },
                                ]}
                              >
                                <Text
                                  style={[
                                    styles.dupBadgeText,
                                    { color: themeColors.warning },
                                  ]}
                                >
                                  {t('statementImport.duplicateBadge')}
                                </Text>
                              </View>
                            ) : null}
                          </View>

                          <View style={styles.previewSubLine}>
                            <Text
                              style={[
                                styles.previewDate,
                                typography.tabularNums,
                                { color: themeColors.inkSecondary },
                              ]}
                            >
                              {formatDateShort(row.transactionDate)}
                            </Text>

                            <Pressable
                              onPress={() =>
                                setEditingCategoryRowIndex(
                                  isEditingCat ? null : row.rowIndex,
                                )
                              }
                              style={[
                                styles.categoryPill,
                                {
                                  backgroundColor: themeColors.surfaceSubtle,
                                  borderColor: isEditingCat
                                    ? accentPrimary
                                    : themeColors.border,
                                },
                              ]}
                            >
                              {row.categoryIcon ? (
                                <CategoryIcon
                                  iconName={row.categoryIcon}
                                  size={12}
                                  color={accentText}
                                />
                              ) : null}
                              <Text
                                numberOfLines={1}
                                style={[
                                  styles.categoryPillText,
                                  { color: themeColors.ink },
                                ]}
                              >
                                {row.categoryName ??
                                  t('statementImport.selectCategoryTitle')}
                              </Text>
                            </Pressable>
                          </View>
                        </View>

                        <MoneyText
                          amount={
                            row.type === 'Income' ? row.amount : -row.amount
                          }
                          currency={preview.currencyCode}
                          size="sm"
                          showSign={row.type === 'Income'}
                          color={
                            row.type === 'Income'
                              ? themeColors.income
                              : themeColors.expenseRowInk
                          }
                        />
                      </View>

                      {/* Inline Category Picker when user taps the pill */}
                      {isEditingCat ? (
                        <View
                          style={[
                            styles.inlineCatPicker,
                            { borderTopColor: themeColors.border },
                          ]}
                        >
                          {matchingCategories.map((cat) => {
                            const isCurrent = cat.id === row.categoryId;
                            return (
                              <Pressable
                                key={cat.id}
                                onPress={() =>
                                  assignCategoryToRow(row.rowIndex, cat)
                                }
                                style={[
                                  styles.inlineCatChip,
                                  {
                                    backgroundColor: isCurrent
                                      ? accentPrimary
                                      : themeColors.surfaceSubtle,
                                    borderColor: isCurrent
                                      ? accentPrimary
                                      : themeColors.border,
                                  },
                                ]}
                              >
                                <Text
                                  style={[
                                    styles.inlineCatChipText,
                                    {
                                      color: isCurrent
                                        ? '#FFFFFF'
                                        : themeColors.ink,
                                    },
                                  ]}
                                >
                                  {cat.name}
                                </Text>
                              </Pressable>
                            );
                          })}
                        </View>
                      ) : null}
                    </View>
                  );
                })}
              </ScrollView>

              {/* Confirm CTA */}
              <View
                style={[
                  styles.confirmFooter,
                  { borderTopColor: themeColors.border },
                ]}
              >
                <AppButton
                  label={t('statementImport.confirmButton', {
                    count: selectedRows.length,
                  })}
                  onPress={() => void handleConfirmImport()}
                  loading={isConfirming}
                  disabled={selectedRows.length === 0}
                  primaryColor={accentPrimary}
                />
              </View>
            </View>
          )}
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
    flex: 1,
    backgroundColor: 'rgba(11, 15, 23, 0.56)',
    justifyContent: 'flex-end',
  },
  sheet: {
    width: '100%',
    maxHeight: '90%',
    marginBottom: 0,
    borderTopLeftRadius: radii.xl,
    borderTopRightRadius: radii.xl,
    borderBottomLeftRadius: 0,
    borderBottomRightRadius: 0,
    borderWidth: 1,
    borderBottomWidth: 0,
    overflow: 'hidden',
  },
  sheetHeader: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.lg,
    paddingBottom: spacing.md,
    borderBottomWidth: 1,
    gap: spacing.md,
  },
  sheetHeaderText: {
    flex: 1,
    gap: 4,
  },
  sheetTitle: {
    ...typography.h3,
  },
  sheetSubtitle: {
    ...typography.caption,
    lineHeight: 18,
  },
  closeButton: {
    width: 34,
    height: 34,
    borderRadius: radii.full,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    marginHorizontal: spacing.xl,
    marginTop: spacing.md,
    padding: spacing.md,
    borderRadius: radii.md,
    borderWidth: 1,
  },
  errorText: {
    flex: 1,
    fontSize: 13,
    fontWeight: '500',
  },
  uploadContent: {
    padding: spacing.xl,
    gap: spacing.base,
  },
  emptyNotice: {
    ...typography.body,
    textAlign: 'center',
    paddingVertical: spacing.xl,
  },
  sectionBlock: {
    gap: spacing.sm,
  },
  sectionLabel: {
    ...typography.overline,
  },
  walletChipRow: {
    gap: spacing.sm,
  },
  walletChip: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radii.md,
    borderWidth: 1,
    gap: 2,
  },
  walletChipName: {
    fontSize: 13.5,
    fontWeight: '600',
  },
  walletChipBalance: {
    fontSize: 11.5,
  },
  uploadDropzone: {
    borderWidth: 1.5,
    borderStyle: 'dashed',
    borderRadius: radii.lg,
    paddingVertical: spacing.xl,
    paddingHorizontal: spacing.lg,
    alignItems: 'center',
    gap: spacing.sm,
  },
  uploadIconWrap: {
    width: 48,
    height: 48,
    borderRadius: radii.full,
    alignItems: 'center',
    justifyContent: 'center',
  },
  uploadPrimaryText: {
    fontSize: 14.5,
    fontWeight: '600',
    textAlign: 'center',
  },
  uploadHintText: {
    ...typography.caption,
    textAlign: 'center',
  },
  pasteToggleBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    paddingVertical: spacing.xs,
  },
  pasteToggleText: {
    fontSize: 12.5,
    fontWeight: '500',
  },
  pasteBox: {
    gap: spacing.sm,
  },
  pasteInput: {
    borderWidth: 1,
    borderRadius: radii.md,
    padding: spacing.md,
    minHeight: 96,
    textAlignVertical: 'top',
    fontSize: 13,
  },
  previewContainer: {
    flexShrink: 1,
  },
  summaryRow: {
    flexDirection: 'row',
    gap: spacing.sm,
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.md,
  },
  summaryCard: {
    flex: 1,
    borderWidth: 1,
    borderRadius: radii.md,
    padding: spacing.md,
    gap: 4,
  },
  summaryLabel: {
    fontSize: 11.5,
    fontWeight: '600',
  },
  metaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.md,
    gap: spacing.sm,
  },
  metaFileBadge: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  metaFileName: {
    fontSize: 12,
    fontWeight: '500',
    flexShrink: 1,
  },
  changeFileLink: {
    fontSize: 12.5,
    fontWeight: '600',
  },
  dupNoticeBanner: {
    marginHorizontal: spacing.xl,
    marginTop: spacing.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs + 2,
    borderRadius: radii.sm,
    borderWidth: 1,
  },
  dupNoticeText: {
    fontSize: 12,
    fontWeight: '500',
  },
  previewList: {
    paddingHorizontal: spacing.xl,
    paddingVertical: spacing.md,
    gap: spacing.sm,
  },
  previewItemCard: {
    borderWidth: 1,
    borderRadius: radii.md,
    padding: spacing.md,
    gap: spacing.sm,
  },
  previewRowTop: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  checkbox: {
    width: 22,
    height: 22,
    borderRadius: 6,
    borderWidth: 1.5,
    alignItems: 'center',
    justifyContent: 'center',
  },
  previewRowMain: {
    flex: 1,
    gap: 4,
  },
  previewTitleLine: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  previewDescription: {
    fontSize: 13.5,
    fontWeight: '600',
    flexShrink: 1,
  },
  dupBadge: {
    paddingHorizontal: 6,
    paddingVertical: 2,
    borderRadius: radii.full,
  },
  dupBadgeText: {
    fontSize: 10.5,
    fontWeight: '700',
  },
  previewSubLine: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  previewDate: {
    fontSize: 11.5,
  },
  categoryPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: radii.full,
    borderWidth: 1,
    maxWidth: 140,
  },
  categoryPillText: {
    fontSize: 11.5,
    fontWeight: '500',
  },
  inlineCatPicker: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: 6,
    paddingTop: spacing.sm,
    borderTopWidth: 1,
  },
  inlineCatChip: {
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  inlineCatChipText: {
    fontSize: 12,
    fontWeight: '600',
  },
  confirmFooter: {
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.md,
    borderTopWidth: 1,
  },
});
