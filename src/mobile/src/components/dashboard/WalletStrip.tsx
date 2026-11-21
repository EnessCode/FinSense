import { Banknote, CreditCard, Landmark, PiggyBank, Trash2 } from 'lucide-react-native';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import type { WalletDto, WalletType } from '@/api/types';
import { AppButton, MoneyText, SurfaceCard } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { colors, radii, spacing, typography } from '@/theme/tokens';

export interface WalletStripProps {
  wallets: WalletDto[];
  onAddWalletPress?: () => void;
  onDeleteWalletPress?: (wallet: WalletDto) => void;
  deletingWalletId?: string | null;
}

function WalletTypeIcon({ type, color }: { type: WalletType; color: string }) {
  switch (type) {
    case 'Cash':
      return <Banknote size={17} color={color} strokeWidth={1.75} />;
    case 'CreditCard':
      return <CreditCard size={17} color={color} strokeWidth={1.75} />;
    case 'Savings':
      return <PiggyBank size={17} color={color} strokeWidth={1.75} />;
    case 'BankAccount':
    default:
      return <Landmark size={17} color={color} strokeWidth={1.75} />;
  }
}

export function WalletStrip({
  wallets,
  onAddWalletPress,
  onDeleteWalletPress,
  deletingWalletId,
}: WalletStripProps) {
  const { t } = useTranslation();
  const { activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();

  const activeWallets = wallets.filter((w) => !w.isArchived);
  const accentTint = isDark
    ? activeTheme.mode === 'Household'
      ? '#FBBF24'
      : '#34D399'
    : activeTheme.primarySurface;
  const pillBg = isDark
    ? activeTheme.mode === 'Household'
      ? 'rgba(251, 191, 36, 0.14)'
      : 'rgba(16, 185, 129, 0.14)'
    : activeTheme.softTint;
  const pillBorder = isDark
    ? activeTheme.mode === 'Household'
      ? 'rgba(251, 191, 36, 0.28)'
      : 'rgba(52, 211, 153, 0.28)'
    : activeTheme.softTintBorder;

  return (
    <View style={styles.section}>
      <View style={styles.headerRow}>
        <View style={styles.titleRow}>
          <Text style={[styles.sectionTitle, { color: themeColors.ink }]}>
            {t('dashboard.walletsTitle')}
          </Text>
          <Text
            style={[
              styles.countText,
              typography.tabularNums,
              { color: themeColors.inkSecondary },
            ]}
          >
            {activeWallets.length}
          </Text>
        </View>

        {onAddWalletPress ? (
          <Pressable
            onPress={onAddWalletPress}
            hitSlop={8}
            style={({ pressed }) => [
              styles.addPill,
              {
                backgroundColor: pillBg,
                borderColor: pillBorder,
                opacity: pressed ? 0.8 : 1,
              },
            ]}
          >
            <Text
              style={[
                styles.addPillText,
                { color: accentTint },
              ]}
            >
              {t('wallets.addWalletButton')}
            </Text>
          </Pressable>
        ) : null}
      </View>

      {activeWallets.length === 0 ? (
        <View style={styles.emptyWrap}>
          <SurfaceCard
            padding="lg"
            variant={isDark ? 'subtle' : 'default'}
            style={styles.emptyCard}
          >
            <Text style={[styles.emptyTitle, { color: themeColors.ink }]}>
              {t('dashboard.walletsEmptyTitle')}
            </Text>
            <Text style={[styles.emptyBody, { color: themeColors.inkSecondary }]}>
              {t('dashboard.walletsEmptyBody')}
            </Text>
            {onAddWalletPress ? (
              <AppButton
                label={t('wallets.addWalletButton')}
                onPress={onAddWalletPress}
                primaryColor={isDark ? '#10B981' : activeTheme.primarySurface}
                style={styles.emptyCta}
              />
            ) : null}
          </SurfaceCard>
        </View>
      ) : (
        <ScrollView
          horizontal
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={styles.scrollContent}
        >
          {activeWallets.map((wallet) => (
            <SurfaceCard
              key={wallet.id}
              padding="md"
              variant={isDark ? 'subtle' : 'default'}
              style={styles.walletCard}
            >
              <View style={styles.walletTop}>
                <View
                  style={[
                    styles.iconBadge,
                    {
                      backgroundColor: pillBg,
                      borderColor: pillBorder,
                    },
                  ]}
                >
                  <WalletTypeIcon
                    type={wallet.type}
                    color={accentTint}
                  />
                </View>
                <View style={styles.walletTopRight}>
                  <Text
                    style={[
                      styles.walletTypeCaption,
                      { color: themeColors.inkSecondary },
                    ]}
                    numberOfLines={1}
                  >
                    {t(`wallets.types.${wallet.type}`)}
                  </Text>
                  {onDeleteWalletPress ? (
                    <Pressable
                      onPress={() => onDeleteWalletPress(wallet)}
                      disabled={deletingWalletId === wallet.id}
                      hitSlop={8}
                      accessibilityRole="button"
                      accessibilityLabel={t('wallets.deleteAccessibility', {
                        name: wallet.name,
                      })}
                      style={({ pressed }) => [
                        styles.deleteButton,
                        {
                          backgroundColor: pressed
                            ? isDark
                              ? 'rgba(248, 113, 113, 0.16)'
                              : 'rgba(185, 28, 28, 0.08)'
                            : 'transparent',
                          opacity: deletingWalletId === wallet.id ? 0.6 : 1,
                        },
                      ]}
                    >
                      {({ pressed }) =>
                        deletingWalletId === wallet.id ? (
                          <ActivityIndicator
                            size="small"
                            color={themeColors.inkSubtle}
                          />
                        ) : (
                          <Trash2
                            size={14}
                            color={
                              pressed
                                ? themeColors.expense
                                : themeColors.inkSubtle
                            }
                            strokeWidth={1.75}
                          />
                        )
                      }
                    </Pressable>
                  ) : null}
                </View>
              </View>

              <Text
                style={[styles.walletName, { color: themeColors.ink }]}
                numberOfLines={1}
              >
                {wallet.name}
              </Text>

              <MoneyText
                amount={wallet.currentBalance}
                currency={wallet.currencyCode}
                size="lg"
                color={
                  wallet.currentBalance < 0
                    ? themeColors.expense
                    : themeColors.ink
                }
              />
            </SurfaceCard>
          ))}
        </ScrollView>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  section: {
    gap: spacing.md,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.xl,
  },
  titleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  sectionTitle: {
    ...typography.h3,
  },
  countText: {
    ...typography.caption,
    fontWeight: '600',
  },
  addPill: {
    paddingHorizontal: 11,
    paddingVertical: 5,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  addPillText: {
    fontSize: 12.5,
    fontWeight: '600',
  },
  emptyWrap: {
    paddingHorizontal: spacing.xl,
  },
  emptyCard: {
    gap: spacing.xs,
  },
  emptyCta: {
    marginTop: spacing.sm,
    height: 44,
  },
  scrollContent: {
    paddingHorizontal: spacing.xl,
    gap: spacing.md,
  },
  walletCard: {
    width: 184,
    padding: spacing.base,
    justifyContent: 'space-between',
  },
  walletTop: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: spacing.md,
    gap: spacing.xs,
  },
  walletTopRight: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'flex-end',
    gap: 4,
  },
  deleteButton: {
    width: 24,
    height: 24,
    borderRadius: radii.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  iconBadge: {
    width: 34,
    height: 34,
    borderRadius: radii.sm,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  walletTypeCaption: {
    ...typography.caption,
    fontSize: 11.5,
    color: colors.inkSubtle,
    flexShrink: 1,
    textAlign: 'right',
  },
  walletName: {
    fontSize: 14,
    fontWeight: '600',
    color: colors.ink,
    marginBottom: 4,
    letterSpacing: -0.1,
  },
  emptyTitle: {
    ...typography.bodyMedium,
  },
  emptyBody: {
    ...typography.caption,
  },
});
