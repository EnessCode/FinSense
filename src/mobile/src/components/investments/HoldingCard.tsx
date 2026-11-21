import {
  ChevronDown,
  ChevronUp,
  Trash2,
  TrendingDown,
  TrendingUp,
} from 'lucide-react-native';
import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ActivityIndicator,
  Pressable,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import type { InvestmentHoldingDto } from '@/api/types';
import { MoneyText, SurfaceCard } from '@/components/ui';
import { useTheme } from '@/context/ThemeContext';
import { getActiveLocale } from '@/i18n';
import { colors, radii, spacing, typography } from '@/theme/tokens';
import {
  formatDateShort,
  formatMoney,
  formatQuantity,
  formatSignedPercent,
} from '@/utils/formatters';
import { CATEGORY_COLORS } from './investmentTheme';

export interface HoldingCardProps {
  holding: InvestmentHoldingDto;
  canContribute: boolean;
  onDeleteLot: (lotId: string) => Promise<void>;
}

export function HoldingCard({
  holding,
  canContribute,
  onDeleteLot,
}: HoldingCardProps) {
  const { t } = useTranslation();
  const { colors: themeColors, isDark } = useTheme();
  const locale = getActiveLocale();

  const [isLotsExpanded, setIsLotsExpanded] = useState(false);
  const [deletingLotId, setDeletingLotId] = useState<string | null>(null);

  const isProfit = holding.profitLossAmount >= 0;
  const pnlColor = isProfit ? themeColors.income : themeColors.expense;
  const pnlSoftBg = isProfit ? themeColors.incomeSoft : themeColors.expenseSoft;
  const categoryColor = CATEGORY_COLORS[holding.category];
  const hasLivePriceDiff =
    Math.abs(holding.currentUnitPrice - holding.averageUnitCost) > 0.0001;

  const handleDeleteLot = async (lotId: string) => {
    setDeletingLotId(lotId);
    try {
      await onDeleteLot(lotId);
    } finally {
      setDeletingLotId(null);
    }
  };

  return (
    <SurfaceCard padding="lg" style={styles.card}>
      {/* Header */}
      <View style={styles.headerRow}>
        <View style={styles.leftTitleBlock}>
          <View
            style={[
              styles.symbolBadge,
              {
                borderColor: categoryColor,
                backgroundColor: isDark ? '#182234' : colors.surfaceSubtle,
              },
            ]}
          >
            <View
              style={[styles.categoryDot, { backgroundColor: categoryColor }]}
            />
            <Text style={[styles.symbolText, isDark && { color: '#F8FAFC' }]}>
              {holding.symbol}
            </Text>
          </View>

          <View style={styles.nameCol}>
            <Text
              style={[styles.assetName, isDark && { color: '#F8FAFC' }]}
              numberOfLines={1}
            >
              {holding.assetName}
            </Text>
            <Text
              style={[
                styles.quantityText,
                typography.tabularNums,
                isDark && { color: '#94A3B8' },
              ]}
            >
              {t(`investments.categories.${holding.category}`)} •{' '}
              {t('investments.quantityLabel', {
                quantity: formatQuantity(holding.totalQuantity),
              })}
            </Text>
          </View>
        </View>

        <View style={styles.rightValueCol}>
          <Text
            style={[
              styles.currentValueCaption,
              isDark && { color: '#94A3B8' },
            ]}
          >
            {t('investments.currentMarketValueLabel')}
          </Text>
          <MoneyText
            amount={holding.currentValue}
            currency={holding.currency}
            size="lg"
            color={isDark ? '#F8FAFC' : colors.ink}
          />
          <View style={[styles.pnlPill, { backgroundColor: pnlSoftBg }]}>
            {isProfit ? (
              <TrendingUp size={12} color={pnlColor} strokeWidth={1.75} />
            ) : (
              <TrendingDown size={12} color={pnlColor} strokeWidth={1.75} />
            )}
            <Text
              style={[
                styles.pnlText,
                typography.tabularNums,
                { color: pnlColor },
              ]}
            >
              {formatMoney(holding.profitLossAmount, holding.currency, {
                showSign: true,
              })}{' '}
              ({formatSignedPercent(holding.profitLossPercent, 2, locale)})
            </Text>
          </View>
        </View>
      </View>

      {/* Metrics Row */}
      <View
        style={[
          styles.metricsRow,
          isDark && { borderTopColor: themeColors.cardBorder },
        ]}
      >
        <View style={styles.metricCell}>
          <Text style={[styles.metricLabel, isDark && { color: '#94A3B8' }]}>
            {t('investments.avgCostLabel')}
          </Text>
          <MoneyText
            amount={holding.averageUnitCost}
            currency={holding.currency}
            size="sm"
            color={isDark ? '#F8FAFC' : colors.ink}
          />
        </View>

        <View
          style={[
            styles.metricCell,
            !hasLivePriceDiff && styles.metricCellRight,
          ]}
        >
          <Text style={[styles.metricLabel, isDark && { color: '#94A3B8' }]}>
            {t('investments.totalHoldingCostLabel')}
          </Text>
          <MoneyText
            amount={holding.totalCost}
            currency={holding.currency}
            size="sm"
            color={isDark ? '#F8FAFC' : colors.ink}
          />
        </View>

        {hasLivePriceDiff ? (
          <View style={[styles.metricCell, styles.metricCellRight]}>
            <Text style={[styles.metricLabel, isDark && { color: '#94A3B8' }]}>
              {t('investments.liveMarketPriceLabel')}
            </Text>
            <MoneyText
              amount={holding.currentUnitPrice}
              currency={holding.currency}
              size="sm"
              color={isDark ? '#F8FAFC' : colors.ink}
            />
          </View>
        ) : null}
      </View>

      {/* Expandable Purchase Lots History */}
      <View
        style={[
          styles.lotsSection,
          isDark && { borderTopColor: themeColors.cardBorder },
        ]}
      >
        <Pressable
          onPress={() => setIsLotsExpanded((prev) => !prev)}
          style={styles.lotsToggleRow}
        >
          <Text
            style={[
              styles.lotsToggleText,
              typography.tabularNums,
              isDark && { color: '#CBD5E1' },
            ]}
          >
            {t('investments.lotHistoryToggle', { count: holding.lots.length })}
          </Text>
          {isLotsExpanded ? (
            <ChevronUp
              size={16}
              color={isDark ? '#94A3B8' : colors.inkSecondary}
              strokeWidth={1.75}
            />
          ) : (
            <ChevronDown
              size={16}
              color={isDark ? '#94A3B8' : colors.inkSecondary}
              strokeWidth={1.75}
            />
          )}
        </Pressable>

        {isLotsExpanded ? (
          <View style={styles.lotsList}>
            {holding.lots.map((lot) => {
              const isDeleting = deletingLotId === lot.id;
              return (
                <View
                  key={lot.id}
                  style={[
                    styles.lotRow,
                    isDark && {
                      backgroundColor: '#182234',
                      borderColor: 'rgba(148, 163, 184, 0.22)',
                    },
                  ]}
                >
                  <View style={styles.lotInfo}>
                    <Text
                      style={[
                        styles.lotDate,
                        typography.tabularNums,
                        isDark && { color: '#F8FAFC' },
                      ]}
                    >
                      {formatDateShort(lot.purchaseDateUtc)}
                    </Text>
                    <Text
                      style={[
                        styles.lotMath,
                        typography.tabularNums,
                        isDark && { color: '#94A3B8' },
                      ]}
                    >
                      {t('investments.lotQuantityUnit', {
                        quantity: formatQuantity(lot.quantity),
                        unitPrice: formatMoney(lot.unitPrice, holding.currency),
                      })}
                    </Text>
                    {lot.notes ? (
                      <Text
                        style={[styles.lotNotes, isDark && { color: '#94A3B8' }]}
                        numberOfLines={1}
                      >
                        {lot.notes}
                      </Text>
                    ) : null}
                  </View>

                  <View style={styles.lotRight}>
                    <MoneyText
                      amount={lot.totalCost}
                      currency={holding.currency}
                      size="sm"
                      color={isDark ? '#F8FAFC' : colors.ink}
                    />

                    {canContribute ? (
                      <Pressable
                        onPress={() => void handleDeleteLot(lot.id)}
                        disabled={isDeleting}
                        hitSlop={8}
                        style={[
                          styles.lotDeleteBtn,
                          isDark && {
                            backgroundColor: 'rgba(248, 113, 113, 0.14)',
                            borderColor: 'rgba(248, 113, 113, 0.3)',
                          },
                        ]}
                      >
                        {isDeleting ? (
                          <ActivityIndicator
                            size="small"
                            color={themeColors.expense}
                          />
                        ) : (
                          <Trash2
                            size={14}
                            color={themeColors.expense}
                            strokeWidth={1.75}
                          />
                        )}
                      </Pressable>
                    ) : null}
                  </View>
                </View>
              );
            })}
          </View>
        ) : null}
      </View>
    </SurfaceCard>
  );
}

const styles = StyleSheet.create({
  card: {
    gap: spacing.md,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  leftTitleBlock: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm + 2,
    flex: 1,
  },
  symbolBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
    paddingHorizontal: 9,
    paddingVertical: 6,
    borderRadius: radii.sm,
    borderWidth: 1,
  },
  categoryDot: {
    width: 6,
    height: 6,
    borderRadius: radii.full,
  },
  symbolText: {
    fontSize: 12.5,
    fontWeight: '700',
    color: colors.ink,
    letterSpacing: 0.3,
  },
  nameCol: {
    flex: 1,
    gap: 2,
  },
  assetName: {
    ...typography.bodyMedium,
    fontWeight: '600',
  },
  quantityText: {
    ...typography.caption,
    fontSize: 12,
  },
  rightValueCol: {
    alignItems: 'flex-end',
    gap: 3,
  },
  currentValueCaption: {
    fontSize: 10.5,
    fontWeight: '600',
    color: colors.inkSubtle,
    textTransform: 'uppercase',
    letterSpacing: 0.4,
  },
  pnlPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: radii.full,
  },
  pnlText: {
    fontSize: 11.5,
    fontWeight: '600',
  },
  metricsRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingTop: spacing.sm,
    borderTopWidth: 1,
    borderTopColor: colors.cardBorder,
    gap: spacing.sm,
  },
  metricCell: {
    flex: 1,
    gap: 2,
  },
  metricCellRight: {
    alignItems: 'flex-end',
  },
  metricLabel: {
    fontSize: 11,
    color: colors.inkSubtle,
    fontWeight: '500',
  },
  lotsSection: {
    borderTopWidth: 1,
    borderTopColor: colors.cardBorder,
    paddingTop: spacing.sm,
  },
  lotsToggleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingVertical: 2,
  },
  lotsToggleText: {
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.inkSecondary,
  },
  lotsList: {
    marginTop: spacing.sm,
    gap: spacing.sm,
  },
  lotRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: spacing.sm + 2,
    borderRadius: radii.md,
    borderWidth: 1,
    borderColor: colors.cardBorder,
    backgroundColor: colors.canvas,
    gap: spacing.sm,
  },
  lotInfo: {
    flex: 1,
    gap: 2,
  },
  lotDate: {
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.ink,
  },
  lotMath: {
    fontSize: 11.5,
    color: colors.inkSecondary,
  },
  lotNotes: {
    fontSize: 11.5,
    color: colors.inkSubtle,
  },
  lotRight: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm + 2,
  },
  lotDeleteBtn: {
    padding: 6,
    borderRadius: radii.sm,
    backgroundColor: colors.expenseSoft,
    borderWidth: 1,
    borderColor: '#FECACA',
  },
});
