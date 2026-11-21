import { ChevronRight } from 'lucide-react-native';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import type { TransactionDto } from '@/api/types';
import { CategoryIcon, MoneyText, SurfaceCard } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { getActiveLocale } from '@/i18n';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import { formatRelativeDate } from '@/utils/formatters';

export interface RecentTransactionsCardProps {
  transactions: TransactionDto[];
  onViewAllPress?: () => void;
}

export function RecentTransactionsCard({
  transactions,
  onViewAllPress,
}: RecentTransactionsCardProps) {
  const { t } = useTranslation();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();
  const isHousehold = activeWorkspace?.type === 'Household';
  const locale = getActiveLocale();
  const accentTint = isDark
    ? isHousehold
      ? '#FBBF24'
      : '#34D399'
    : activeTheme.primarySurface;

  return (
    <View style={styles.section}>
      <View style={styles.headerRow}>
        <Text style={[styles.sectionTitle, { color: themeColors.ink }]}>
          {t('dashboard.recentTransactionsTitle')}
        </Text>
        {onViewAllPress ? (
          <Pressable
            onPress={onViewAllPress}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel={t('dashboard.viewAllTransactions')}
            style={({ pressed }) => [
              styles.viewAllButton,
              pressed && { opacity: 0.7 },
            ]}
          >
            <Text
              style={[
                styles.viewAllText,
                { color: accentTint },
              ]}
            >
              {t('dashboard.viewAllTransactions')}
            </Text>
            <ChevronRight
              size={14}
              color={accentTint}
              strokeWidth={2}
            />
          </Pressable>
        ) : null}
      </View>

      <SurfaceCard padding={transactions.length === 0 ? 'lg' : 'none'}>
        {transactions.length === 0 ? (
          <View>
            <Text style={[styles.emptyTitle, { color: themeColors.ink }]}>
              {t('dashboard.recentTransactionsEmptyTitle')}
            </Text>
            <Text style={[styles.emptyBody, { color: themeColors.inkSecondary }]}>
              {t('dashboard.recentTransactionsEmptyBody')}
            </Text>
          </View>
        ) : (
          transactions.map((tx, index) => {
            const isIncome = tx.type === 'Income';
            const isTransfer = tx.type === 'Transfer';

            const primaryTitle =
              tx.merchantName?.trim() ||
              (isTransfer
                ? t('transactions.transferFallback')
                : tx.categoryName || t('transactions.uncategorized'));

            const memberAttribution =
              tx.paidByDisplayName || tx.createdByDisplayName;

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
              tx.type === 'Expense' ? -Math.abs(tx.amount) : tx.amount;

            return (
              <View
                key={tx.id}
                style={[
                  styles.row,
                  index > 0 && [
                    styles.rowBorder,
                    { borderTopColor: themeColors.cardBorder },
                  ],
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
                    iconName={tx.categoryIcon}
                    transactionType={tx.type}
                    size={18}
                    color={iconColor}
                  />
                </View>

                <View style={styles.centerMeta}>
                  <Text
                    style={[styles.merchantText, { color: themeColors.ink }]}
                    numberOfLines={1}
                  >
                    {primaryTitle}
                  </Text>

                  <View style={styles.subMetaRow}>
                    <Text
                      style={[
                        styles.subMetaText,
                        typography.tabularNums,
                        { color: themeColors.inkSecondary },
                      ]}
                      numberOfLines={1}
                    >
                      {formatRelativeDate(tx.transactionDate, locale)} •{' '}
                      {tx.walletName}
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
                            { color: accentTint },
                          ]}
                        >
                          {memberAttribution}
                        </Text>
                      </View>
                    ) : null}
                  </View>
                </View>

                <View style={styles.amountCol}>
                  <MoneyText
                    amount={displayAmount}
                    currency={tx.currencyCode}
                    size="md"
                    color={amountColor}
                    showSign={isIncome}
                  />
                </View>
              </View>
            );
          })
        )}
      </SurfaceCard>
    </View>
  );
}

const styles = StyleSheet.create({
  section: {
    paddingHorizontal: spacing.xl,
    gap: spacing.md,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  sectionTitle: {
    ...typography.h3,
  },
  viewAllButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 3,
    paddingVertical: 4,
    paddingHorizontal: 6,
  },
  viewAllText: {
    fontSize: 12.5,
    fontWeight: '600',
    letterSpacing: -0.1,
  },
  emptyTitle: {
    ...typography.bodyMedium,
    marginBottom: 4,
  },
  emptyBody: {
    ...typography.caption,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.base,
    gap: spacing.md,
  },
  rowBorder: {
    borderTopWidth: 1,
    borderTopColor: colors.cardBorder,
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
  centerMeta: {
    flex: 1,
    gap: 3,
  },
  merchantText: {
    ...typography.bodyMedium,
    letterSpacing: -0.1,
  },
  subMetaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: 6,
  },
  subMetaText: {
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
  amountCol: {
    alignItems: 'flex-end',
  },
});
