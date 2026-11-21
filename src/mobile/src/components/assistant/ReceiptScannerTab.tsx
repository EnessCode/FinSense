import * as Haptics from 'expo-haptics';
import * as ImagePicker from 'expo-image-picker';
import { useRouter } from 'expo-router';
import {
  AlertCircle,
  AlertTriangle,
  Camera,
  CheckCircle2,
  FileText,
  Image as ImageIcon,
  Receipt,
  RotateCcw,
} from 'lucide-react-native';
import React, { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ActivityIndicator,
  Alert,
  Image,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { toApiError } from '@/api/client';
import { aiApi, createTransaction } from '@/api/endpoints';
import type {
  CategoryDto,
  ReceiptScanResultDto,
  WalletDto,
} from '@/api/types';
import {
  AppButton,
  AppInput,
  CategoryIcon,
  MoneyText,
  SurfaceCard,
} from '@/components/ui';
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

const SAMPLE_RECEIPTS = [
  {
    key: 'migros',
    fileName: 'migros-fis.txt',
    rawOcrText: [
      'MİGROS TİCARET A.Ş.',
      'TARİH: 11.11.2026 SAAT: 14:22',
      'FİŞ NO: 0184',
      'TAM YAĞLI PEYNİR 345,00',
      'ZEYTİNYAĞI 1L 495,50',
      'EKMEK VE SÜT 180,25',
      'TOPLAM KDV: 92,80',
      'GENEL TOPLAM: ₺1.020,75',
    ].join('\n'),
  },
  {
    key: 'starbucks',
    fileName: 'starbucks-fis.txt',
    rawOcrText: [
      'STARBUCKS COFFEE TÜRKİYE',
      'TARİH: 11.11.2026 SAAT: 09:40',
      '2x CAFFE LATTE GRANDE 290,00',
      '1x HİNDİ FÜME SANDVİÇ 175,00',
      'KDV %10: 42,27',
      'TOPLAM: ₺465,00',
    ].join('\n'),
  },
  {
    key: 'shell',
    fileName: 'shell-fis.txt',
    rawOcrText: [
      'SHELL & TURCAS PETROL A.Ş.',
      'TARİH: 10.11.2026 SAAT: 18:15',
      'V-POWER KURŞUNSUZ 95 28,50 LT',
      'TOPLAM KDV %20: 215,00',
      'ÖDENECEK TUTAR: ₺1.290,00',
    ].join('\n'),
  },
] as const;

function normalizeDateToIso(raw?: string | null): string {
  if (!raw) return toIsoCalendarDate(new Date());
  const trimmed = raw.trim();

  if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
    return trimmed;
  }

  const trMatch = /^(\d{2})[./-](\d{2})[./-](\d{4})$/.exec(trimmed);
  if (trMatch) {
    return `${trMatch[3]}-${trMatch[2]}-${trMatch[1]}`;
  }

  return toIsoCalendarDate(new Date());
}

function formatEditableAmount(value?: number | null): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return '';
  }
  return value.toFixed(2).replace('.', ',');
}

export interface ReceiptScannerTabProps {
  workspaceId?: string;
  currencyCode: string;
  wallets: WalletDto[];
  expenseCategories: CategoryDto[];
  onQuotaUpdate: (remaining: number) => void;
  onWalletCreated: () => void;
}

export function ReceiptScannerTab({
  workspaceId,
  currencyCode,
  wallets,
  expenseCategories,
  onQuotaUpdate,
  onWalletCreated,
}: ReceiptScannerTabProps) {
  const { t } = useTranslation();
  const router = useRouter();
  const { activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();

  const [showPasteBox, setShowPasteBox] = useState(false);
  const [rawReceiptText, setRawReceiptText] = useState('');
  const [previewUri, setPreviewUri] = useState<string | null>(null);
  const [isScanning, setIsScanning] = useState(false);
  const [scanError, setScanError] = useState<string | null>(null);
  const [scanResult, setScanResult] = useState<ReceiptScanResultDto | null>(null);

  // Editable review fields
  const [merchantInput, setMerchantInput] = useState('');
  const [amountInput, setAmountInput] = useState('');
  const [dateInput, setDateInput] = useState(toIsoCalendarDate(new Date()));
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);
  const [selectedWalletId, setSelectedWalletId] = useState<string | null>(null);

  const [reviewError, setReviewError] = useState<string | null>(null);
  const [isSavingTx, setIsSavingTx] = useState(false);
  const [createWalletOpen, setCreateWalletOpen] = useState(false);

  const clearPreviewUri = (nextUri: string | null = null) => {
    setPreviewUri((prev) => {
      if (Platform.OS === 'web' && prev && prev.startsWith('blob:') && typeof URL !== 'undefined') {
        URL.revokeObjectURL(prev);
      }
      return nextUri;
    });
  };

  // Clear review state when workspace changes
  useEffect(() => {
    setScanResult(null);
    setScanError(null);
    setReviewError(null);
    setRawReceiptText('');
    clearPreviewUri(null);
  }, [workspaceId]);

  useEffect(() => {
    return () => {
      clearPreviewUri(null);
    };
  }, []);

  useEffect(() => {
    if (wallets.length > 0 && (!selectedWalletId || !wallets.some((w) => w.id === selectedWalletId))) {
      setSelectedWalletId(wallets[0]?.id ?? null);
    }
  }, [wallets, selectedWalletId]);

  const applyScanResult = (result: ReceiptScanResultDto) => {
    onQuotaUpdate(result.remainingDailyQuota);
    setScanResult(result);
    setReviewError(null);

    if (result.isValidReceipt) {
      setMerchantInput(result.merchantName?.trim() ?? '');
      setAmountInput(formatEditableAmount(result.totalAmount));
      setDateInput(normalizeDateToIso(result.transactionDate));

      const matchedById = result.suggestedCategoryId
        ? expenseCategories.find((c) => c.id === result.suggestedCategoryId)
        : undefined;

      const matchedByName = !matchedById && result.suggestedCategoryName
        ? expenseCategories.find(
            (c) =>
              c.name.toLowerCase() === result.suggestedCategoryName!.toLowerCase() ||
              c.nameTr.toLowerCase() === result.suggestedCategoryName!.toLowerCase(),
          )
        : undefined;

      setSelectedCategoryId(
        matchedById?.id ?? matchedByName?.id ?? expenseCategories[0]?.id ?? null,
      );
      setSelectedWalletId((prev) =>
        prev && wallets.some((w) => w.id === prev)
          ? prev
          : (wallets[0]?.id ?? null),
      );
    }
  };

  const handleScanText = async (textToScan: string, fileName?: string) => {
    if (!workspaceId || isScanning) return;
    const trimmed = textToScan.trim();
    if (!trimmed) {
      setScanError(t('assistant.scanner.validation.textRequired'));
      return;
    }

    clearPreviewUri(null);
    setScanError(null);
    setIsScanning(true);
    try {
      const result = await aiApi.scanReceipt(workspaceId, {
        rawOcrText: trimmed,
        fileName: fileName ?? null,
      });
      applyScanResult(result);
    } catch (err) {
      setScanError(toApiError(err).message);
    } finally {
      setIsScanning(false);
    }
  };

  const handleNativeImageAsset = async (asset: ImagePicker.ImagePickerAsset) => {
    if (!workspaceId || isScanning) return;

    clearPreviewUri(asset.uri);
    setScanError(null);
    setIsScanning(true);

    try {
      let result: ReceiptScanResultDto;
      if (asset.base64) {
        result = await aiApi.scanReceipt(workspaceId, {
          imageBase64: asset.base64,
          fileName: asset.fileName ?? 'receipt.jpg',
          mimeType: asset.mimeType ?? 'image/jpeg',
        });
      } else {
        const formData = new FormData();
        formData.append('file', {
          uri: asset.uri,
          name: asset.fileName ?? 'receipt.jpg',
          type: asset.mimeType ?? 'image/jpeg',
        } as unknown as Blob);
        result = await aiApi.scanReceiptFile(workspaceId, formData);
      }

      applyScanResult(result);
    } catch (err) {
      clearPreviewUri(null);
      setScanError(toApiError(err).message);
    } finally {
      setIsScanning(false);
    }
  };

  const handleTakeCameraPhoto = async () => {
    if (!workspaceId || isScanning) return;

    const permission = await ImagePicker.requestCameraPermissionsAsync();
    if (!permission.granted) {
      Alert.alert(
        t('assistant.scanner.cameraPermissionTitle'),
        t('assistant.scanner.cameraPermissionBody'),
      );
      return;
    }

    const result = await ImagePicker.launchCameraAsync({
      mediaTypes: ['images'],
      quality: 0.75,
      base64: true,
    });

    const asset = !result.canceled ? result.assets[0] : undefined;
    if (asset) {
      await handleNativeImageAsset(asset);
    }
  };

  const handlePickFromGallery = async () => {
    if (!workspaceId || isScanning) return;

    const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (!permission.granted) {
      Alert.alert(
        t('assistant.scanner.galleryPermissionTitle'),
        t('assistant.scanner.galleryPermissionBody'),
      );
      return;
    }

    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ['images'],
      quality: 0.75,
      base64: true,
    });

    const asset = !result.canceled ? result.assets[0] : undefined;
    if (asset) {
      await handleNativeImageAsset(asset);
    }
  };

  const handleWebFileUpload = () => {
    if (Platform.OS !== 'web' || !workspaceId || isScanning) return;
    if (typeof document === 'undefined') return;

    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/*';
    input.onchange = async () => {
      const file = input.files?.[0];
      if (!file) return;

      const localUrl =
        typeof URL !== 'undefined' ? URL.createObjectURL(file) : null;
      clearPreviewUri(localUrl);

      const formData = new FormData();
      formData.append('file', file);

      setScanError(null);
      setIsScanning(true);
      try {
        const result = await aiApi.scanReceiptFile(workspaceId, formData);
        applyScanResult(result);
      } catch (err) {
        clearPreviewUri(null);
        setScanError(toApiError(err).message);
      } finally {
        setIsScanning(false);
      }
    };
    input.click();
  };

  const handleSamplePress = (sample: (typeof SAMPLE_RECEIPTS)[number]) => {
    setRawReceiptText(sample.rawOcrText);
    void handleScanText(sample.rawOcrText, sample.fileName);
  };

  const handleResetScan = () => {
    setScanResult(null);
    setScanError(null);
    setReviewError(null);
    clearPreviewUri(null);
  };

  const parsedReviewAmount = useMemo(
    () => parseMoneyInput(amountInput),
    [amountInput],
  );

  const handleSaveTransaction = async () => {
    if (!workspaceId || isSavingTx) return;
    setReviewError(null);

    const trimmedMerchant = merchantInput.trim();
    if (!trimmedMerchant) {
      setReviewError(t('assistant.scanner.validation.merchantRequired'));
      return;
    }

    if (parsedReviewAmount <= 0) {
      setReviewError(t('assistant.scanner.validation.amountRequired'));
      return;
    }

    const trimmedDate = dateInput.trim();
    if (!/^\d{4}-\d{2}-\d{2}$/.test(trimmedDate)) {
      setReviewError(t('assistant.scanner.validation.dateInvalid'));
      return;
    }

    if (!selectedWalletId) {
      setReviewError(t('assistant.scanner.validation.walletRequired'));
      return;
    }

    if (!selectedCategoryId) {
      setReviewError(t('assistant.scanner.validation.categoryRequired'));
      return;
    }

    setIsSavingTx(true);
    try {
      const taxNote =
        scanResult?.taxAmount !== null && scanResult?.taxAmount !== undefined
          ? t('assistant.scanner.taxCaption', {
              amount: formatMoney(
                scanResult.taxAmount,
                scanResult.currency || currencyCode,
              ),
            })
          : null;

      await createTransaction(
        workspaceId,
        {
          type: 'Expense',
          walletId: selectedWalletId,
          categoryId: selectedCategoryId,
          amount: parsedReviewAmount,
          currencyCode: scanResult?.currency || currencyCode,
          transactionDate: trimmedDate,
          merchantName: trimmedMerchant,
          notes: taxNote,
        },
        generateUuidV4(),
      );

      if (Platform.OS !== 'web') {
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      }

      setScanResult(null);
      setRawReceiptText('');
      clearPreviewUri(null);
      router.navigate('/(tabs)');
    } catch (err) {
      setReviewError(toApiError(err).message);
    } finally {
      setIsSavingTx(false);
    }
  };

  const previewCard = previewUri ? (
    <SurfaceCard padding="md" style={styles.previewCard}>
      <Text
        style={[styles.previewLabel, isDark && { color: '#94A3B8' }]}
      >
        {t('assistant.scanner.receiptPreviewLabel')}
      </Text>
      <Image
        source={{ uri: previewUri }}
        style={[
          styles.previewImage,
          isDark && {
            borderColor: 'rgba(148, 163, 184, 0.22)',
            backgroundColor: '#182234',
          },
        ]}
        resizeMode="cover"
      />
    </SurfaceCard>
  ) : null;

  return (
    <KeyboardAvoidingView
      style={{ flex: 1 }}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      keyboardVerticalOffset={Platform.OS === 'ios' ? 110 : 0}
    >
      <ScrollView
        showsVerticalScrollIndicator={false}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={styles.scrollContent}
      >
      {/* Upload & Sample Section */}
      {!scanResult ? (
        <View style={styles.sectionStack}>
          <SurfaceCard padding="xl" style={styles.heroScanCard}>
            <View
              style={[
                styles.scanIconBadge,
                {
                  backgroundColor: isDark
                    ? 'rgba(16, 185, 129, 0.14)'
                    : activeTheme.softTint,
                  borderColor: isDark
                    ? 'rgba(52, 211, 153, 0.28)'
                    : activeTheme.softTintBorder,
                },
              ]}
            >
              <Receipt
                size={22}
                color={isDark ? '#34D399' : activeTheme.primarySurface}
                strokeWidth={1.75}
              />
            </View>
            <Text style={[styles.scanTitle, isDark && { color: '#F8FAFC' }]}>
              {t('assistant.scanner.title')}
            </Text>
            <Text
              style={[styles.scanSubtitle, isDark && { color: '#CBD5E1' }]}
            >
              {t('assistant.scanner.subtitle')}
            </Text>

            <View style={styles.uploadActions}>
              {Platform.OS === 'web' ? (
                <>
                  <AppButton
                    label={t('assistant.scanner.uploadPhotoButton')}
                    onPress={handleWebFileUpload}
                    loading={isScanning}
                    primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
                    leftIcon={
                      <Camera size={18} color="#FFFFFF" strokeWidth={1.75} />
                    }
                  />
                  <Text
                    style={[styles.uploadHint, isDark && { color: '#94A3B8' }]}
                  >
                    {t('assistant.scanner.uploadWebHint')}
                  </Text>
                </>
              ) : (
                <>
                  <AppButton
                    label={t('assistant.scanner.cameraButton')}
                    onPress={() => void handleTakeCameraPhoto()}
                    loading={isScanning}
                    primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
                    leftIcon={
                      <Camera size={18} color="#FFFFFF" strokeWidth={1.75} />
                    }
                  />
                  <AppButton
                    label={t('assistant.scanner.galleryButton')}
                    variant="secondary"
                    onPress={() => void handlePickFromGallery()}
                    disabled={isScanning}
                    leftIcon={
                      <ImageIcon
                        size={17}
                        color={isDark ? '#E2E8F0' : activeTheme.primarySurface}
                        strokeWidth={1.75}
                      />
                    }
                  />
                </>
              )}

              <AppButton
                label={t('assistant.scanner.pasteToggleOpen')}
                variant="secondary"
                onPress={() => setShowPasteBox((prev) => !prev)}
                leftIcon={
                  <FileText
                    size={17}
                    color={isDark ? '#E2E8F0' : colors.ink}
                    strokeWidth={1.75}
                  />
                }
              />
            </View>
          </SurfaceCard>

          {showPasteBox ? (
            <SurfaceCard padding="lg" style={styles.pasteCard}>
              <TextInput
                value={rawReceiptText}
                onChangeText={setRawReceiptText}
                placeholder={t('assistant.scanner.pastePlaceholder')}
                placeholderTextColor={isDark ? '#94A3B8' : colors.inkSubtle}
                selectionColor={isDark ? '#34D399' : activeTheme.primarySurface}
                multiline
                textAlignVertical="top"
                style={[
                  styles.pasteInput,
                  isDark && {
                    backgroundColor: '#182234',
                    borderColor: 'rgba(148, 163, 184, 0.25)',
                    color: '#F8FAFC',
                  },
                ]}
              />
              <AppButton
                label={
                  isScanning
                    ? t('assistant.scanner.scanningLabel')
                    : t('assistant.scanner.scanTextButton')
                }
                onPress={() => void handleScanText(rawReceiptText)}
                loading={isScanning}
                primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
              />
            </SurfaceCard>
          ) : null}

          {/* Sample Turkish Receipts */}
          <View style={styles.samplesBlock}>
            <Text
              style={[styles.samplesLabel, isDark && { color: '#94A3B8' }]}
            >
              {t('assistant.scanner.samplesLabel')}
            </Text>
            <View style={styles.samplesWrap}>
              {SAMPLE_RECEIPTS.map((sample) => (
                <Pressable
                  key={sample.key}
                  disabled={isScanning}
                  onPress={() => handleSamplePress(sample)}
                  style={({ pressed }) => [
                    styles.sampleChip,
                    {
                      borderColor: isDark
                        ? 'rgba(148, 163, 184, 0.22)'
                        : activeTheme.softTintBorder,
                      backgroundColor: isDark
                        ? pressed
                          ? '#182234'
                          : '#131B29'
                        : pressed
                          ? activeTheme.softTint
                          : colors.surface,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.sampleChipText,
                      { color: isDark ? '#CBD5E1' : activeTheme.primarySurface },
                    ]}
                  >
                    {t(`assistant.scanner.samples.${sample.key}`)}
                  </Text>
                </Pressable>
              ))}
            </View>
          </View>

          {isScanning ? (
            <SurfaceCard padding="lg" style={styles.scanningBanner}>
              <ActivityIndicator
                size="small"
                color={isDark ? '#34D399' : activeTheme.primarySurface}
              />
              <Text
                style={[styles.scanningText, isDark && { color: '#CBD5E1' }]}
              >
                {t('assistant.scanner.scanningLabel')}
              </Text>
            </SurfaceCard>
          ) : null}

          {scanError ? (
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
                color={isDark ? themeColors.expense : colors.expense}
                strokeWidth={1.75}
              />
              <Text
                style={[
                  styles.errorText,
                  isDark && { color: themeColors.expense },
                ]}
              >
                {scanError}
              </Text>
            </View>
          ) : null}
        </View>
      ) : !scanResult.isValidReceipt ? (
        /* Invalid Receipt Rejection Card */
        <View style={styles.sectionStack}>
          {previewCard}
          <SurfaceCard padding="xl" style={styles.invalidCard}>
            <View style={styles.invalidHeader}>
              <AlertTriangle
                size={20}
                color={isDark ? themeColors.warning : colors.warning}
                strokeWidth={1.75}
              />
              <Text
                style={[
                  styles.invalidTitle,
                  isDark && { color: themeColors.warning },
                ]}
              >
                {t('assistant.scanner.invalidReceiptTitle')}
              </Text>
            </View>
            <Text
              style={[styles.invalidBody, isDark && { color: '#CBD5E1' }]}
            >
              {scanResult.rejectionReason || t('common.unknownError')}
            </Text>
            <AppButton
              label={t('assistant.scanner.scanAnotherButton')}
              variant="secondary"
              onPress={handleResetScan}
              leftIcon={
                <RotateCcw
                  size={16}
                  color={isDark ? '#E2E8F0' : colors.ink}
                  strokeWidth={1.75}
                />
              }
            />
          </SurfaceCard>
        </View>
      ) : (
        /* Editable Extracted Receipt Confirmation Card */
        <View style={styles.sectionStack}>
          {previewCard}

          <SurfaceCard padding="xl" style={styles.reviewCard}>
            <View style={styles.reviewTopRow}>
              <View style={styles.reviewTitleGroup}>
                <CheckCircle2
                  size={18}
                  color={isDark ? '#34D399' : colors.income}
                  strokeWidth={1.75}
                />
                <Text
                  style={[styles.reviewTitle, isDark && { color: '#F8FAFC' }]}
                >
                  {t('assistant.scanner.reviewTitle')}
                </Text>
              </View>
            </View>

            <View
              style={[
                styles.reviewAmountBox,
                isDark && { borderColor: themeColors.cardBorder },
              ]}
            >
              <MoneyText
                amount={parsedReviewAmount}
                currency={scanResult.currency || currencyCode}
                size="xl"
                color={isDark ? '#F8FAFC' : colors.ink}
              />
              {scanResult.taxAmount !== null &&
              scanResult.taxAmount !== undefined ? (
                <Text
                  style={[
                    styles.taxCaption,
                    typography.tabularNums,
                    isDark && { color: '#94A3B8' },
                  ]}
                >
                  {t('assistant.scanner.taxCaption', {
                    amount: formatMoney(
                      scanResult.taxAmount,
                      scanResult.currency || currencyCode,
                    ),
                  })}
                </Text>
              ) : null}
            </View>

            {reviewError ? (
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
                  color={isDark ? themeColors.expense : colors.expense}
                  strokeWidth={1.75}
                />
                <Text
                  style={[
                    styles.errorText,
                    isDark && { color: themeColors.expense },
                  ]}
                >
                  {reviewError}
                </Text>
              </View>
            ) : null}

            <AppInput
              label={t('assistant.scanner.merchantLabel')}
              placeholder={t('assistant.scanner.merchantPlaceholder')}
              value={merchantInput}
              onChangeText={setMerchantInput}
              accentColor={activeTheme.primarySurface}
            />

            <View style={styles.twoColRow}>
              <View style={styles.halfCol}>
                <AppInput
                  label={t('assistant.scanner.amountLabel')}
                  value={amountInput}
                  onChangeText={setAmountInput}
                  keyboardType="decimal-pad"
                  accentColor={activeTheme.primarySurface}
                />
              </View>
              <View style={styles.halfCol}>
                <AppInput
                  label={t('assistant.scanner.dateLabel')}
                  value={dateInput}
                  onChangeText={setDateInput}
                  autoCapitalize="none"
                  accentColor={activeTheme.primarySurface}
                />
              </View>
            </View>

            {/* Category Selector Pills */}
            <View style={styles.selectorGroup}>
              <Text
                style={[styles.selectorLabel, isDark && { color: '#E2E8F0' }]}
              >
                {t('assistant.scanner.categoryLabel')}
              </Text>
              <View style={styles.chipWrap}>
                {expenseCategories.map((cat) => {
                  const selected = cat.id === selectedCategoryId;
                  return (
                    <Pressable
                      key={cat.id}
                      onPress={() => setSelectedCategoryId(cat.id)}
                      style={[
                        styles.categoryChip,
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
                      <CategoryIcon
                        iconName={cat.icon}
                        size={14}
                        color={
                          selected
                            ? isDark
                              ? '#34D399'
                              : activeTheme.primarySurface
                            : isDark
                              ? '#94A3B8'
                              : colors.inkSecondary
                        }
                      />
                      <Text
                        numberOfLines={1}
                        style={[
                          styles.categoryChipText,
                          isDark && !selected && { color: '#CBD5E1' },
                          selected && {
                            color: isDark
                              ? '#34D399'
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

            {/* Wallet Selector Pills */}
            <View style={styles.selectorGroup}>
              <Text
                style={[styles.selectorLabel, isDark && { color: '#E2E8F0' }]}
              >
                {t('assistant.scanner.walletLabel')}
              </Text>
              {wallets.length === 0 ? (
                <View style={styles.noWalletsBox}>
                  <Text
                    style={[
                      styles.noWalletsText,
                      isDark && { color: '#94A3B8' },
                    ]}
                  >
                    {t('assistant.scanner.noWalletsNotice')}
                  </Text>
                  <AppButton
                    label={t('wallets.addWalletButton')}
                    variant="secondary"
                    onPress={() => setCreateWalletOpen(true)}
                  />
                </View>
              ) : (
                <ScrollView
                  horizontal
                  showsHorizontalScrollIndicator={false}
                  contentContainerStyle={styles.chipRow}
                >
                  {wallets.map((w) => {
                    const selected = w.id === selectedWalletId;
                    return (
                      <Pressable
                        key={w.id}
                        onPress={() => setSelectedWalletId(w.id)}
                        style={[
                          styles.walletChip,
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
                            styles.walletChipTitle,
                            isDark && !selected && { color: '#F8FAFC' },
                            selected && {
                              color: isDark
                                ? '#F8FAFC'
                                : activeTheme.primarySurface,
                              fontWeight: '600',
                            },
                          ]}
                        >
                          {w.name}
                        </Text>
                        <Text
                          style={[
                            styles.walletChipSub,
                            typography.tabularNums,
                            isDark && !selected && { color: '#94A3B8' },
                            selected && {
                              color: isDark
                                ? '#34D399'
                                : activeTheme.primarySurface,
                            },
                          ]}
                        >
                          {formatMoney(w.currentBalance, w.currencyCode)}
                        </Text>
                      </Pressable>
                    );
                  })}
                </ScrollView>
              )}
            </View>

            <View style={styles.reviewActions}>
              <AppButton
                label={t('assistant.scanner.saveTransactionButton')}
                onPress={() => void handleSaveTransaction()}
                loading={isSavingTx}
                disabled={wallets.length === 0}
                primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
              />
              <AppButton
                label={t('assistant.scanner.scanAnotherButton')}
                variant="ghost"
                onPress={handleResetScan}
              />
            </View>
          </SurfaceCard>
        </View>
      )}

      <CreateWalletModal
        visible={createWalletOpen}
        onClose={() => setCreateWalletOpen(false)}
        onCreated={(created) => {
          setSelectedWalletId(created.id);
          onWalletCreated();
        }}
      />
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  scrollContent: {
    paddingHorizontal: spacing.xl,
    paddingBottom: spacing.xxxl,
  },
  sectionStack: {
    gap: spacing.base,
  },
  previewCard: {
    gap: spacing.sm,
  },
  previewLabel: {
    ...typography.overline,
  },
  previewImage: {
    width: '100%',
    height: 156,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
  },
  heroScanCard: {
    gap: spacing.sm,
  },
  scanIconBadge: {
    width: 44,
    height: 44,
    borderRadius: radii.md,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.xs,
  },
  scanTitle: {
    ...typography.h3,
  },
  scanSubtitle: {
    ...typography.body,
    color: colors.inkSecondary,
    marginBottom: spacing.xs,
  },
  uploadActions: {
    gap: spacing.sm,
    marginTop: spacing.xs,
  },
  uploadHint: {
    ...typography.caption,
    fontSize: 12,
    textAlign: 'center',
  },
  pasteCard: {
    gap: spacing.md,
  },
  pasteInput: {
    minHeight: 112,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
    padding: spacing.base,
    fontSize: 13.5,
    lineHeight: 20,
    color: colors.ink,
  },
  samplesBlock: {
    gap: spacing.sm,
  },
  samplesLabel: {
    ...typography.overline,
  },
  samplesWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  sampleChip: {
    paddingHorizontal: spacing.md,
    paddingVertical: 8,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  sampleChipText: {
    fontSize: 13,
    fontWeight: '600',
  },
  scanningBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  scanningText: {
    ...typography.bodyMedium,
    color: colors.inkSecondary,
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
  invalidCard: {
    gap: spacing.md,
  },
  invalidHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  invalidTitle: {
    ...typography.h3,
    color: colors.warning,
  },
  invalidBody: {
    ...typography.body,
    color: colors.inkSecondary,
  },
  reviewCard: {
    gap: spacing.base,
  },
  reviewTopRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  reviewTitleGroup: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    flex: 1,
  },
  reviewTitle: {
    ...typography.h3,
  },
  reviewAmountBox: {
    paddingVertical: spacing.sm,
    borderTopWidth: 1,
    borderBottomWidth: 1,
    borderColor: colors.cardBorder,
    gap: 2,
  },
  taxCaption: {
    ...typography.caption,
  },
  twoColRow: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: spacing.md,
  },
  halfCol: {
    flex: 1,
  },
  selectorGroup: {
    gap: spacing.sm,
  },
  selectorLabel: {
    ...typography.caption,
    fontWeight: '600',
    color: colors.ink,
  },
  chipRow: {
    gap: spacing.sm,
    paddingRight: spacing.base,
  },
  chipWrap: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'space-between',
    rowGap: spacing.sm,
  },
  categoryChip: {
    width: '48.5%',
    minHeight: 42,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'flex-start',
    gap: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: 10,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
  },
  categoryChipText: {
    flexShrink: 1,
    fontSize: 13,
    fontWeight: '500',
    color: colors.ink,
  },
  walletChip: {
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
    gap: 2,
  },
  walletChipTitle: {
    fontSize: 13,
    fontWeight: '500',
    color: colors.ink,
  },
  walletChipSub: {
    fontSize: 11.5,
    color: colors.inkSecondary,
  },
  noWalletsBox: {
    gap: spacing.sm,
  },
  noWalletsText: {
    ...typography.caption,
  },
  reviewActions: {
    gap: spacing.xs,
    marginTop: spacing.xs,
  },
});
