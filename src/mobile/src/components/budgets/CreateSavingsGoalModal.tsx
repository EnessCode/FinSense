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
import { createSavingsGoal } from '@/api/endpoints';
import type { SavingsGoalDto, WalletDto } from '@/api/types';
import { AppButton, AppInput } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { useKeyboardHeight } from '@/hooks/useKeyboardHeight';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import { parseMoneyInput } from '@/utils/formatters';

export interface CreateSavingsGoalModalProps {
  visible: boolean;
  wallets: WalletDto[];
  onClose: () => void;
  onCreated?: (goal: SavingsGoalDto) => void;
}

export function CreateSavingsGoalModal({
  visible,
  wallets,
  onClose,
  onCreated,
}: CreateSavingsGoalModalProps) {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { isDark } = useTheme();
  const keyboardHeight = useKeyboardHeight();

  const [name, setName] = useState('');
  const [targetInput, setTargetInput] = useState('');
  const [currentInput, setCurrentInput] = useState('');
  const [targetDateInput, setTargetDateInput] = useState('');
  const [linkedWalletId, setLinkedWalletId] = useState<string | null>(null);

  const [nameError, setNameError] = useState<string | undefined>();
  const [targetError, setTargetError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | undefined>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const activeWallets = wallets.filter((w) => !w.isArchived);

  useEffect(() => {
    if (visible) {
      setName('');
      setTargetInput('');
      setCurrentInput('');
      setTargetDateInput('');
      setLinkedWalletId(null);
      setNameError(undefined);
      setTargetError(undefined);
      setServerError(undefined);
    }
  }, [visible]);

  const handleSubmit = async () => {
    setServerError(undefined);
    let valid = true;

    const trimmedName = name.trim();
    if (!trimmedName) {
      setNameError(t('budgets.goalNameRequired'));
      valid = false;
    } else {
      setNameError(undefined);
    }

    const targetAmount = parseMoneyInput(targetInput);
    if (targetAmount <= 0) {
      setTargetError(t('budgets.goalTargetRequired'));
      valid = false;
    } else {
      setTargetError(undefined);
    }

    if (!valid || !activeWorkspace?.id) return;

    const trimmedDate = targetDateInput.trim();
    const validDate = /^\d{4}-\d{2}-\d{2}$/.test(trimmedDate)
      ? trimmedDate
      : null;

    setIsSubmitting(true);
    try {
      const created = await createSavingsGoal(activeWorkspace.id, {
        name: trimmedName,
        targetAmount,
        currentAmount: linkedWalletId ? 0 : parseMoneyInput(currentInput),
        linkedWalletId,
        targetDate: validDate,
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
            <Text style={[styles.title, isDark && { color: '#F8FAFC' }]}>
              {t('budgets.createGoalTitle')}
            </Text>
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

            <AppInput
              label={t('budgets.goalNameLabel')}
              placeholder={t('budgets.goalNamePlaceholder')}
              value={name}
              onChangeText={(text) => {
                setName(text);
                if (nameError) setNameError(undefined);
              }}
              error={nameError}
              accentColor={activeTheme.primarySurface}
            />

            <AppInput
              label={t('budgets.goalTargetLabel')}
              placeholder={t('budgets.goalTargetPlaceholder')}
              value={targetInput}
              onChangeText={(text) => {
                setTargetInput(text);
                if (targetError) setTargetError(undefined);
              }}
              error={targetError}
              keyboardType="decimal-pad"
              accentColor={activeTheme.primarySurface}
            />

            {activeWallets.length > 0 ? (
              <View style={styles.walletBlock}>
                <Text
                  style={[styles.walletLabel, isDark && { color: '#CBD5E1' }]}
                >
                  {t('budgets.goalWalletLabel')}
                </Text>
                <ScrollView
                  horizontal
                  showsHorizontalScrollIndicator={false}
                  contentContainerStyle={styles.chipRow}
                >
                  <Pressable
                    onPress={() => setLinkedWalletId(null)}
                    style={[
                      styles.chip,
                      isDark &&
                        linkedWalletId !== null && {
                          backgroundColor: '#182234',
                          borderColor: 'rgba(148, 163, 184, 0.22)',
                        },
                      linkedWalletId === null && {
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
                        styles.chipText,
                        isDark &&
                          linkedWalletId !== null && { color: '#E2E8F0' },
                        linkedWalletId === null && {
                          color: isDark
                            ? '#FFFFFF'
                            : activeTheme.primarySurface,
                          fontWeight: '600',
                        },
                      ]}
                    >
                      {t('budgets.goalNoWallet')}
                    </Text>
                  </Pressable>

                  {activeWallets.map((w) => {
                    const selected = linkedWalletId === w.id;
                    return (
                      <Pressable
                        key={w.id}
                        onPress={() => setLinkedWalletId(w.id)}
                        style={[
                          styles.chip,
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
                        <Text
                          style={[
                            styles.chipText,
                            isDark && !selected && { color: '#E2E8F0' },
                            selected && {
                              color: isDark
                                ? '#FFFFFF'
                                : activeTheme.primarySurface,
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
              </View>
            ) : null}

            {linkedWalletId === null ? (
              <AppInput
                label={t('budgets.goalCurrentLabel')}
                placeholder="0,00"
                value={currentInput}
                onChangeText={setCurrentInput}
                keyboardType="decimal-pad"
                accentColor={activeTheme.primarySurface}
              />
            ) : null}

            <AppInput
              label={`${t('budgets.goalTargetDateLabel')} (${t('common.optional')})`}
              placeholder={t('budgets.goalTargetDatePlaceholder')}
              value={targetDateInput}
              onChangeText={setTargetDateInput}
              autoCapitalize="none"
              accentColor={activeTheme.primarySurface}
            />

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
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: spacing.lg,
  },
  title: {
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
  walletBlock: {
    gap: spacing.sm,
  },
  walletLabel: {
    ...typography.caption,
    fontWeight: '600',
    color: colors.ink,
  },
  chipRow: {
    gap: spacing.sm,
  },
  chip: {
    paddingHorizontal: spacing.md,
    paddingVertical: 9,
    borderRadius: radii.full,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.surfaceSubtle,
  },
  chipText: {
    fontSize: 13,
    fontWeight: '500',
    color: colors.inkSecondary,
  },
  submitBtn: {
    marginTop: spacing.xs,
  },
});
