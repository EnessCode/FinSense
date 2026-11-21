import { useFocusEffect } from 'expo-router';
import React, { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { getCategories, getWallets } from '@/api/endpoints';
import type { CategoryDto, WalletDto } from '@/api/types';
import { FinancialCoachTab } from '@/components/assistant/FinancialCoachTab';
import { ReceiptScannerTab } from '@/components/assistant/ReceiptScannerTab';
import { WorkspacePillSwitcher } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { colors, radii, spacing, typography } from '@/theme/tokens';

type AssistantMode = 'coach' | 'scanner';
const ASSISTANT_MODES: AssistantMode[] = ['coach', 'scanner'];

export default function AssistantScreen() {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();

  const workspaceId = activeWorkspace?.id;
  const isHousehold = activeWorkspace?.type === 'Household';
  const currencyCode = activeWorkspace?.defaultCurrency ?? 'TRY';

  const [mode, setMode] = useState<AssistantMode>('coach');
  const [remainingQuota, setRemainingQuota] = useState<number | null>(null);
  const [wallets, setWallets] = useState<WalletDto[]>([]);
  const [categories, setCategories] = useState<CategoryDto[]>([]);

  const loadWorkspaceAssets = useCallback(async () => {
    if (!workspaceId) return;
    try {
      const [walletsRes, categoriesRes] = await Promise.all([
        getWallets(workspaceId, false),
        getCategories(workspaceId, 'Expense'),
      ]);
      setWallets(walletsRes.filter((w) => !w.isArchived));
      setCategories(categoriesRes);
    } catch {
      // Non-blocking; scanner handles empty or retry cleanly
    }
  }, [workspaceId]);

  useFocusEffect(
    useCallback(() => {
      void loadWorkspaceAssets();
    }, [loadWorkspaceAssets]),
  );

  const expenseCategories = useMemo(
    () => categories.filter((c) => c.type === 'Expense'),
    [categories],
  );

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
      {/* Top Bar */}
      <View style={styles.topHeader}>
        <View style={styles.titleGroup}>
          <Text style={[styles.screenTitle, { color: themeColors.ink }]}>
            {t('assistant.screenTitle')}
          </Text>
          {remainingQuota !== null ? (
            <View
              style={[
                styles.quotaPill,
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
              <Text
                style={[
                  styles.quotaPillText,
                  typography.tabularNums,
                  { color: isDark ? '#34D399' : activeTheme.primarySurface },
                ]}
              >
                {t('assistant.quotaPill', { count: remainingQuota })}
              </Text>
            </View>
          ) : null}
        </View>

        <WorkspacePillSwitcher />
      </View>

      {/* 2-Mode Segmented Switcher */}
      <View
        style={[
          styles.segmentedWrap,
          isDark && {
            backgroundColor: '#131B29',
            borderColor: 'rgba(148, 163, 184, 0.22)',
          },
        ]}
      >
        {ASSISTANT_MODES.map((item) => {
          const selected = mode === item;
          return (
            <Pressable
              key={item}
              onPress={() => setMode(item)}
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
                  isDark && !selected && { color: '#CBD5E1' },
                  selected && styles.segmentLabelSelected,
                ]}
              >
                {t(`assistant.modes.${item}`)}
              </Text>
            </Pressable>
          );
        })}
      </View>

      {/* Active Mode Body */}
      <View style={styles.body}>
        {mode === 'coach' ? (
          <FinancialCoachTab
            workspaceId={workspaceId}
            isHousehold={isHousehold}
            onQuotaUpdate={setRemainingQuota}
          />
        ) : (
          <ReceiptScannerTab
            workspaceId={workspaceId}
            currencyCode={currencyCode}
            wallets={wallets}
            expenseCategories={expenseCategories}
            onQuotaUpdate={setRemainingQuota}
            onWalletCreated={() => void loadWorkspaceAssets()}
          />
        )}
      </View>
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
  titleGroup: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    flexShrink: 1,
  },
  screenTitle: {
    ...typography.h2,
  },
  quotaPill: {
    paddingHorizontal: 9,
    paddingVertical: 4,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  quotaPillText: {
    fontSize: 11.5,
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
  body: {
    flex: 1,
  },
});
