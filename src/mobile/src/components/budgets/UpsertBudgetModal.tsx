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
import { upsertBudget } from '@/api/endpoints';
import type { BudgetDto, CategoryDto } from '@/api/types';
import { AppButton, AppInput, CategoryIcon } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { useKeyboardHeight } from '@/hooks/useKeyboardHeight';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import { parseMoneyInput } from '@/utils/formatters';

export interface UpsertBudgetModalProps {
  visible: boolean;
  year: number;
  month: number;
  categories: CategoryDto[];
  onClose: () => void;
  onSaved?: (budget: BudgetDto) => void;
}

export function UpsertBudgetModal({
  visible,
  year,
  month,
  categories,
  onClose,
  onSaved,
}: UpsertBudgetModalProps) {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { isDark } = useTheme();
  const keyboardHeight = useKeyboardHeight();

  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);
  const [limitInput, setLimitInput] = useState('');
  const [thresholdInput, setThresholdInput] = useState('80');
  const [limitError, setLimitError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | undefined>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const expenseCategories = categories.filter((c) => c.type === 'Expense');

  useEffect(() => {
    if (visible) {
      setSelectedCategoryId(null);
      setLimitInput('');
      setThresholdInput('80');
      setLimitError(undefined);
      setServerError(undefined);
    }
  }, [visible]);

  const handleSubmit = async () => {
    setServerError(undefined);
    const limitAmount = parseMoneyInput(limitInput);
    if (limitAmount <= 0) {
      setLimitError(t('budgets.limitRequired'));
      return;
    }

    if (!activeWorkspace?.id) return;

    const threshold = Math.min(
      Math.max(Math.round(Number(thresholdInput) || 80), 10),
      100,
    );

    setIsSubmitting(true);
    try {
      const saved = await upsertBudget(activeWorkspace.id, {
        categoryId: selectedCategoryId,
        year,
        month,
        limitAmount,
        alertThresholdPercent: threshold,
      });
      onSaved?.(saved);
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
              {t('budgets.upsertTitle')}
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

            <View style={styles.scopeBlock}>
              <Text style={[styles.scopeLabel, isDark && { color: '#CBD5E1' }]}>
                {t('budgets.scopeLabel')}
              </Text>
              <ScrollView
                horizontal
                showsHorizontalScrollIndicator={false}
                contentContainerStyle={styles.chipRow}
              >
                <Pressable
                  onPress={() => setSelectedCategoryId(null)}
                  style={[
                    styles.chip,
                    isDark &&
                      selectedCategoryId !== null && {
                        backgroundColor: '#182234',
                        borderColor: 'rgba(148, 163, 184, 0.22)',
                      },
                    selectedCategoryId === null && {
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
                        selectedCategoryId !== null && { color: '#E2E8F0' },
                      selectedCategoryId === null && {
                        color: isDark
                          ? '#FFFFFF'
                          : activeTheme.primarySurface,
                        fontWeight: '600',
                      },
                    ]}
                  >
                    {t('budgets.overallOption')}
                  </Text>
                </Pressable>

                {expenseCategories.map((cat) => {
                  const selected = selectedCategoryId === cat.id;
                  return (
                    <Pressable
                      key={cat.id}
                      onPress={() => setSelectedCategoryId(cat.id)}
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
                      <CategoryIcon
                        iconName={cat.icon}
                        size={14}
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
                        {cat.name}
                      </Text>
                    </Pressable>
                  );
                })}
              </ScrollView>
            </View>

            <AppInput
              label={t('budgets.limitLabel')}
              placeholder={t('budgets.limitPlaceholder')}
              value={limitInput}
              onChangeText={(text) => {
                setLimitInput(text);
                if (limitError) setLimitError(undefined);
              }}
              error={limitError}
              keyboardType="decimal-pad"
              accentColor={activeTheme.primarySurface}
            />

            <AppInput
              label={t('budgets.thresholdLabel')}
              placeholder="80"
              value={thresholdInput}
              onChangeText={setThresholdInput}
              keyboardType="number-pad"
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
    maxHeight: '85%',
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
  scopeBlock: {
    gap: spacing.sm,
  },
  scopeLabel: {
    ...typography.caption,
    fontWeight: '600',
    color: colors.ink,
  },
  chipRow: {
    gap: spacing.sm,
  },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
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
