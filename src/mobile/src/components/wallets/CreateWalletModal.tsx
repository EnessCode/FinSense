import {
  AlertCircle,
  Banknote,
  CreditCard,
  Landmark,
  PiggyBank,
  X,
} from 'lucide-react-native';
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
import { createWallet } from '@/api/endpoints';
import type { WalletDto, WalletType } from '@/api/types';
import { AppButton, AppInput } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { useKeyboardHeight } from '@/hooks/useKeyboardHeight';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import { parseMoneyInput } from '@/utils/formatters';

export interface CreateWalletModalProps {
  visible: boolean;
  onClose: () => void;
  onCreated?: (wallet: WalletDto) => void;
}

const WALLET_TYPES: WalletType[] = ['Cash', 'BankAccount', 'CreditCard', 'Savings'];

function WalletTypeBadgeIcon({
  type,
  color,
}: {
  type: WalletType;
  color: string;
}) {
  switch (type) {
    case 'Cash':
      return <Banknote size={16} color={color} strokeWidth={1.75} />;
    case 'CreditCard':
      return <CreditCard size={16} color={color} strokeWidth={1.75} />;
    case 'Savings':
      return <PiggyBank size={16} color={color} strokeWidth={1.75} />;
    case 'BankAccount':
    default:
      return <Landmark size={16} color={color} strokeWidth={1.75} />;
  }
}

export function CreateWalletModal({
  visible,
  onClose,
  onCreated,
}: CreateWalletModalProps) {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { isDark } = useTheme();
  const keyboardHeight = useKeyboardHeight();

  const [walletType, setWalletType] = useState<WalletType>('BankAccount');
  const [name, setName] = useState('');
  const [initialBalanceInput, setInitialBalanceInput] = useState('');
  const [creditLimitInput, setCreditLimitInput] = useState('');
  const [nameError, setNameError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | undefined>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    if (visible) {
      setWalletType('BankAccount');
      setName('');
      setInitialBalanceInput('');
      setCreditLimitInput('');
      setNameError(undefined);
      setServerError(undefined);
    }
  }, [visible]);

  const handleSubmit = async () => {
    setServerError(undefined);
    const trimmedName = name.trim();
    if (!trimmedName) {
      setNameError(t('wallets.nameRequired'));
      return;
    }

    if (!activeWorkspace?.id) {
      return;
    }

    setIsSubmitting(true);
    try {
      const initialBalance = parseMoneyInput(initialBalanceInput);
      const parsedLimit =
        walletType === 'CreditCard' && creditLimitInput.trim()
          ? parseMoneyInput(creditLimitInput)
          : null;

      const created = await createWallet(activeWorkspace.id, {
        name: trimmedName,
        type: walletType,
        currencyCode: activeWorkspace.defaultCurrency || 'TRY',
        initialBalance,
        creditLimit: parsedLimit,
      });

      onCreated?.(created);
      onClose();
    } catch (err) {
      setServerError(toApiError(err).message);
    } finally {
      setIsSubmitting(false);
    }
  };

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
                {t('wallets.createTitle')}
              </Text>
              <Text style={[styles.subtitle, isDark && { color: '#94A3B8' }]}>
                {t('wallets.createSubtitle')}
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
            {serverError ? (
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
                  {serverError}
                </Text>
              </View>
            ) : null}

            <View style={styles.fieldGroup}>
              <Text style={[styles.fieldLabel, isDark && { color: '#CBD5E1' }]}>
                {t('wallets.typeLabel')}
              </Text>
              <View style={styles.typeGrid}>
                {WALLET_TYPES.map((typeOption) => {
                  const selected = walletType === typeOption;
                  return (
                    <Pressable
                      key={typeOption}
                      onPress={() => setWalletType(typeOption)}
                      style={[
                        styles.typeChip,
                        isDark &&
                          !selected && {
                            backgroundColor: '#182234',
                            borderColor: 'rgba(148, 163, 184, 0.22)',
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
                      <WalletTypeBadgeIcon
                        type={typeOption}
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
                          styles.typeChipText,
                          isDark && !selected && { color: '#E2E8F0' },
                          selected && {
                            color: isDark
                              ? '#FFFFFF'
                              : activeTheme.primarySurface,
                            fontWeight: '600',
                          },
                        ]}
                      >
                        {t(`wallets.types.${typeOption}`)}
                      </Text>
                    </Pressable>
                  );
                })}
              </View>
            </View>

            <AppInput
              label={t('wallets.nameLabel')}
              placeholder={t('wallets.namePlaceholder')}
              value={name}
              onChangeText={(text) => {
                setName(text);
                if (nameError) setNameError(undefined);
              }}
              error={nameError}
              accentColor={activeTheme.primarySurface}
              returnKeyType="next"
            />

            <AppInput
              label={`${t('wallets.initialBalanceLabel')} (${activeWorkspace?.defaultCurrency ?? 'TRY'})`}
              placeholder={t('wallets.initialBalancePlaceholder')}
              value={initialBalanceInput}
              onChangeText={setInitialBalanceInput}
              keyboardType="decimal-pad"
              accentColor={activeTheme.primarySurface}
            />

            {walletType === 'CreditCard' ? (
              <AppInput
                label={`${t('wallets.creditLimitLabel')} (${t('common.optional')})`}
                placeholder={t('wallets.creditLimitPlaceholder')}
                value={creditLimitInput}
                onChangeText={setCreditLimitInput}
                keyboardType="decimal-pad"
                accentColor={activeTheme.primarySurface}
              />
            ) : null}

            <View style={styles.actions}>
              <AppButton
                label={t('common.save')}
                onPress={() => void handleSubmit()}
                loading={isSubmitting}
                primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
              />
            </View>
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
    maxHeight: '88%',
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
    marginBottom: spacing.lg,
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
    gap: spacing.base,
    paddingBottom: spacing.sm,
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
  fieldGroup: {
    gap: spacing.sm,
  },
  fieldLabel: {
    ...typography.caption,
    fontWeight: '600',
    color: colors.ink,
  },
  typeGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
  },
  typeChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm + 2,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
  },
  typeChipText: {
    fontSize: 13.5,
    fontWeight: '500',
    color: colors.inkSecondary,
  },
  actions: {
    marginTop: spacing.sm,
  },
});
