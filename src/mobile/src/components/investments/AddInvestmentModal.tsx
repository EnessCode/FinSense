import * as Haptics from 'expo-haptics';
import { AlertCircle, X } from 'lucide-react-native';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { toApiError } from '@/api/client';
import { investmentsApi } from '@/api/endpoints';
import type { InvestmentAssetCategory, InvestmentLotDto } from '@/api/types';
import { AppButton, AppInput, MoneyText } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { useKeyboardHeight } from '@/hooks/useKeyboardHeight';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import { toIsoCalendarDate } from '@/utils/formatters';
import {
  CATEGORY_PRESETS,
  INVESTMENT_CATEGORIES,
} from './investmentTheme';

export interface AddInvestmentModalProps {
  visible: boolean;
  onClose: () => void;
  onAdded?: (lot: InvestmentLotDto) => void;
}

export function AddInvestmentModal({
  visible,
  onClose,
  onAdded,
}: AddInvestmentModalProps) {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { isDark } = useTheme();
  const keyboardHeight = useKeyboardHeight();
  const currencyCode = activeWorkspace?.defaultCurrency ?? 'TRY';

  const [category, setCategory] = useState<InvestmentAssetCategory>('Gold');
  const [symbol, setSymbol] = useState('GAU');
  const [assetName, setAssetName] = useState('Gram Altın');
  const [purchaseDate, setPurchaseDate] = useState(toIsoCalendarDate(new Date()));
  const [quantity, setQuantity] = useState('');
  const [unitPrice, setUnitPrice] = useState('');
  const [notesInput, setNotesInput] = useState('');

  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const computedTotalCost =
    Math.round(
      ((parseFloat(quantity.replace(',', '.')) || 0) *
        (parseFloat(unitPrice.replace(',', '.')) || 0)) *
        100,
    ) / 100;

  useEffect(() => {
    if (visible) {
      const defaultPreset = CATEGORY_PRESETS.Gold[0]!;
      setCategory('Gold');
      setSymbol(defaultPreset.symbol);
      setAssetName(defaultPreset.assetName);
      setPurchaseDate(toIsoCalendarDate(new Date()));
      setQuantity('');
      setUnitPrice('');
      setNotesInput('');
      setErrorMessage(null);
    }
  }, [visible]);

  const handleCategoryChange = (nextCategory: InvestmentAssetCategory) => {
    setCategory(nextCategory);
    const firstPreset = CATEGORY_PRESETS[nextCategory][0];
    if (firstPreset) {
      setSymbol(firstPreset.symbol);
      setAssetName(firstPreset.assetName);
    }
  };

  const handleSubmit = async () => {
    if (!activeWorkspace?.id || isSubmitting) return;
    setErrorMessage(null);

    const trimmedSymbol = symbol.trim().toUpperCase();
    const trimmedName = assetName.trim();
    const trimmedDate = purchaseDate.trim();
    const parsedQty = parseFloat(quantity.replace(',', '.')) || 0;
    const parsedUnitPrice = parseFloat(unitPrice.replace(',', '.')) || 0;

    if (!trimmedSymbol) {
      setErrorMessage(t('investments.modal.validation.symbolRequired'));
      return;
    }
    if (!trimmedName) {
      setErrorMessage(t('investments.modal.validation.assetNameRequired'));
      return;
    }
    if (!/^\d{4}-\d{2}-\d{2}$/.test(trimmedDate)) {
      setErrorMessage(t('investments.modal.validation.dateInvalid'));
      return;
    }
    if (parsedQty <= 0) {
      setErrorMessage(t('investments.modal.validation.quantityRequired'));
      return;
    }
    if (parsedUnitPrice <= 0) {
      setErrorMessage(t('investments.modal.validation.unitPriceRequired'));
      return;
    }

    setIsSubmitting(true);
    try {
      const created = await investmentsApi.addLot(activeWorkspace.id, {
        category,
        symbol: trimmedSymbol,
        assetName: trimmedName,
        quantity: parsedQty,
        unitPrice: parsedUnitPrice,
        totalCost: computedTotalCost > 0 ? computedTotalCost : null,
        purchaseDateUtc: `${trimmedDate}T12:00:00Z`,
        notes: notesInput.trim() || null,
      });

      if (Platform.OS !== 'web') {
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      }

      onAdded?.(created);
      onClose();
    } catch (err) {
      setErrorMessage(toApiError(err).message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const presets = CATEGORY_PRESETS[category];

  return (
    <Modal
      visible={visible}
      transparent
      statusBarTranslucent
      navigationBarTranslucent
      animationType="fade"
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
            isDark && {
              backgroundColor: '#131B29',
              borderColor: 'rgba(255, 255, 255, 0.1)',
            },
            {
              paddingBottom:
                Platform.OS === 'android' && keyboardHeight > 0
                  ? keyboardHeight + spacing.md
                  : Math.max(insets.bottom, 20) + spacing.sm,
            },
          ]}
        >
          <View
            style={[
              styles.handle,
              isDark && { backgroundColor: 'rgba(148, 163, 184, 0.3)' },
            ]}
          />

          <View style={styles.header}>
            <View style={styles.headerText}>
              <Text style={[styles.title, isDark && { color: '#F8FAFC' }]}>
                {t('investments.modal.title')}
              </Text>
              <Text style={[styles.subtitle, isDark && { color: '#94A3B8' }]}>
                {t('investments.modal.subtitle')}
              </Text>
            </View>
            <Pressable
              onPress={onClose}
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

          <ScrollView
            bounces={false}
            keyboardShouldPersistTaps="handled"
            showsVerticalScrollIndicator={false}
            contentContainerStyle={styles.formBody}
          >
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
                <Text
                  style={[
                    styles.errorBannerText,
                    isDark && { color: '#F87171' },
                  ]}
                >
                  {errorMessage}
                </Text>
              </View>
            ) : null}

            {/* Category Selector */}
            <View style={styles.group}>
              <Text style={[styles.groupLabel, isDark && { color: '#CBD5E1' }]}>
                {t('investments.modal.categoryLabel')}
              </Text>
              <ScrollView
                horizontal
                showsHorizontalScrollIndicator={false}
                contentContainerStyle={styles.chipRow}
              >
                {INVESTMENT_CATEGORIES.map((cat) => {
                  const selected = category === cat;
                  return (
                    <Pressable
                      key={cat}
                      onPress={() => handleCategoryChange(cat)}
                      style={[
                        styles.catChip,
                        isDark &&
                          !selected && {
                            backgroundColor: '#182234',
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
                          styles.catChipText,
                          isDark && !selected && { color: '#E2E8F0' },
                          selected && styles.catChipTextSelected,
                        ]}
                      >
                        {t(`investments.categories.${cat}`)}
                      </Text>
                    </Pressable>
                  );
                })}
              </ScrollView>
            </View>

            {/* Popular Presets */}
            <View style={styles.group}>
              <Text style={[styles.groupLabel, isDark && { color: '#CBD5E1' }]}>
                {t('investments.modal.presetsLabel')}
              </Text>
              <View style={styles.presetsWrap}>
                {presets.map((preset) => {
                  const isActive =
                    symbol.trim().toUpperCase() === preset.symbol;
                  return (
                    <Pressable
                      key={preset.symbol}
                      onPress={() => {
                        setSymbol(preset.symbol);
                        setAssetName(preset.assetName);
                      }}
                      style={[
                        styles.presetChip,
                        isDark &&
                          !isActive && {
                            backgroundColor: '#182234',
                            borderColor: 'rgba(148, 163, 184, 0.22)',
                          },
                        isActive && {
                          backgroundColor: isDark
                            ? '#10B981'
                            : activeTheme.softTint,
                          borderColor: isDark
                            ? '#10B981'
                            : activeTheme.primarySurface,
                        },
                      ]}
                    >
                      <Text
                        style={[
                          styles.presetSymbol,
                          isDark && !isActive && { color: '#F8FAFC' },
                          isActive && {
                            color: isDark
                              ? '#FFFFFF'
                              : activeTheme.primarySurface,
                          },
                        ]}
                      >
                        {preset.symbol}
                      </Text>
                      <Text
                        style={[
                          styles.presetName,
                          isDark && !isActive && { color: '#CBD5E1' },
                          isActive && {
                            color: isDark
                              ? '#FFFFFF'
                              : activeTheme.primarySurface,
                          },
                        ]}
                      >
                        {preset.assetName}
                      </Text>
                    </Pressable>
                  );
                })}
              </View>
            </View>

            {/* Symbol & Asset Name */}
            <View style={styles.twoColRow}>
              <View style={styles.colSmall}>
                <AppInput
                  label={t('investments.modal.symbolLabel')}
                  placeholder={t('investments.modal.symbolPlaceholder')}
                  value={symbol}
                  onChangeText={(txt) => setSymbol(txt.toUpperCase())}
                  autoCapitalize="characters"
                  accentColor={activeTheme.primarySurface}
                />
              </View>
              <View style={styles.colLarge}>
                <AppInput
                  label={t('investments.modal.assetNameLabel')}
                  placeholder={t('investments.modal.assetNamePlaceholder')}
                  value={assetName}
                  onChangeText={setAssetName}
                  accentColor={activeTheme.primarySurface}
                />
              </View>
            </View>

            {/* Purchase Date */}
            <AppInput
              label={t('investments.modal.purchaseDateLabel')}
              placeholder="YYYY-MM-DD"
              value={purchaseDate}
              onChangeText={setPurchaseDate}
              autoCapitalize="none"
              accentColor={activeTheme.primarySurface}
            />

            {/* Quantity & Unit Price */}
            <View style={styles.twoColRow}>
              <View style={styles.colEqual}>
                <AppInput
                  label={t('investments.modal.quantityLabel')}
                  placeholder={t('investments.modal.quantityPlaceholder')}
                  value={quantity}
                  onChangeText={setQuantity}
                  keyboardType="decimal-pad"
                  accentColor={activeTheme.primarySurface}
                />
              </View>
              <View style={styles.colEqual}>
                <AppInput
                  label={t('investments.modal.unitPriceLabel')}
                  placeholder={t('investments.modal.unitPricePlaceholder')}
                  value={unitPrice}
                  onChangeText={setUnitPrice}
                  keyboardType="decimal-pad"
                  accentColor={activeTheme.primarySurface}
                />
              </View>
            </View>

            <AppInput
              label={`${t('investments.modal.notesLabel')} (${t('common.optional')})`}
              placeholder={t('investments.modal.notesPlaceholder')}
              value={notesInput}
              onChangeText={setNotesInput}
              accentColor={activeTheme.primarySurface}
            />

            {/* Read-Only Toplam Maliyet Summary Card */}
            <View
              style={[
                styles.totalCostCard,
                isDark && {
                  backgroundColor: '#182234',
                  borderColor: 'rgba(148, 163, 184, 0.22)',
                },
              ]}
            >
              <View style={styles.totalCostLeft}>
                <Text
                  style={[
                    styles.totalCostLabel,
                    isDark && { color: '#CBD5E1' },
                  ]}
                >
                  {t('investments.modal.totalCostLabel')}
                </Text>
                <Text
                  style={[
                    styles.totalCostHint,
                    isDark && { color: '#94A3B8' },
                  ]}
                >
                  {t('investments.modal.totalCostHint')}
                </Text>
              </View>
              <MoneyText
                amount={computedTotalCost}
                currency={currencyCode}
                size="lg"
                color={isDark ? '#F8FAFC' : colors.ink}
              />
            </View>

            <AppButton
              label={t('common.save')}
              onPress={() => void handleSubmit()}
              loading={isSubmitting}
              primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
              style={styles.submitBtn}
            />
          </ScrollView>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: {
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
    maxHeight: '90%',
  },
  handle: {
    width: 38,
    height: 4,
    borderRadius: radii.full,
    backgroundColor: colors.border,
    alignSelf: 'center',
    marginBottom: spacing.lg,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    marginBottom: spacing.base,
  },
  headerText: {
    flex: 1,
    paddingRight: spacing.base,
  },
  title: {
    ...typography.h2,
    marginBottom: spacing.xs,
  },
  subtitle: {
    ...typography.caption,
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
  formBody: {
    gap: spacing.md,
    paddingBottom: spacing.md,
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
  errorBannerText: {
    flex: 1,
    fontSize: 13,
    color: colors.expense,
    fontWeight: '500',
  },
  group: {
    gap: spacing.xs + 2,
  },
  groupLabel: {
    ...typography.caption,
    fontWeight: '600',
    color: colors.ink,
  },
  chipRow: {
    gap: spacing.sm,
  },
  catChip: {
    paddingHorizontal: spacing.base,
    paddingVertical: 8,
    borderRadius: radii.full,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
  },
  catChipText: {
    fontSize: 13,
    fontWeight: '500',
    color: colors.inkSecondary,
  },
  catChipTextSelected: {
    color: '#FFFFFF',
    fontWeight: '600',
  },
  presetsWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.xs + 2,
  },
  presetChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
  },
  presetSymbol: {
    fontSize: 12,
    fontWeight: '700',
    color: colors.ink,
  },
  presetName: {
    fontSize: 12,
    color: colors.inkSecondary,
  },
  twoColRow: {
    flexDirection: 'row',
    gap: spacing.sm + 2,
  },
  colSmall: {
    flex: 2,
  },
  colLarge: {
    flex: 3,
  },
  colEqual: {
    flex: 1,
  },
  totalCostCard: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.base,
    paddingVertical: spacing.md,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.cardBorder,
    backgroundColor: colors.surfaceSubtle,
    gap: spacing.md,
  },
  totalCostLeft: {
    flex: 1,
    gap: 2,
  },
  totalCostLabel: {
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.ink,
  },
  totalCostHint: {
    fontSize: 11,
    color: colors.inkSubtle,
  },
  submitBtn: {
    marginTop: spacing.xs,
  },
});
