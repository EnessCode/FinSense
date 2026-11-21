import React from 'react';
import { Pressable, StyleSheet, View, type StyleProp, type ViewStyle } from 'react-native';
import { useTheme } from '@/context/ThemeContext';
import { radii, spacing } from '@/theme/tokens';

export type SurfaceCardVariant = 'default' | 'subtle';
export type SurfaceCardPadding = 'none' | 'md' | 'lg' | 'xl';

export interface SurfaceCardProps {
  children: React.ReactNode;
  variant?: SurfaceCardVariant;
  padding?: SurfaceCardPadding;
  onPress?: () => void;
  style?: StyleProp<ViewStyle>;
}

const PADDING_MAP: Record<SurfaceCardPadding, number> = {
  none: 0,
  md: spacing.base,
  lg: spacing.lg,
  xl: spacing.xl,
};

export function SurfaceCard({
  children,
  variant = 'default',
  padding = 'lg',
  onPress,
  style,
}: SurfaceCardProps) {
  const { colors: themeColors } = useTheme();

  const cardStyle: StyleProp<ViewStyle> = [
    styles.card,
    variant === 'subtle'
      ? {
          backgroundColor: themeColors.surfaceSubtle,
          borderColor: themeColors.border,
        }
      : {
          backgroundColor: themeColors.surface,
          borderColor: themeColors.cardBorder,
        },
    { padding: PADDING_MAP[padding] },
    style,
  ];

  if (onPress) {
    return (
      <Pressable
        onPress={onPress}
        style={({ pressed }) => [cardStyle, pressed && styles.pressed]}
      >
        {children}
      </Pressable>
    );
  }

  return <View style={cardStyle}>{children}</View>;
}

const styles = StyleSheet.create({
  card: {
    borderRadius: radii.lg,
    borderWidth: 1,
  },
  pressed: {
    opacity: 0.92,
    transform: [{ scale: 0.992 }],
  },
});
