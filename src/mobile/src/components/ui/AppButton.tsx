import React from 'react';
import {
  ActivityIndicator,
  Pressable,
  StyleSheet,
  Text,
  View,
  type StyleProp,
  type ViewStyle,
} from 'react-native';
import { useTheme } from '@/context/ThemeContext';
import { radii, spacing } from '@/theme/tokens';

export type AppButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

export interface AppButtonProps {
  label: string;
  onPress: () => void;
  variant?: AppButtonVariant;
  primaryColor?: string;
  loading?: boolean;
  disabled?: boolean;
  leftIcon?: React.ReactNode;
  style?: StyleProp<ViewStyle>;
}

export function AppButton({
  label,
  onPress,
  variant = 'primary',
  primaryColor = '#0D2B22',
  loading = false,
  disabled = false,
  leftIcon,
  style,
}: AppButtonProps) {
  const { colors, isDark } = useTheme();
  const isDisabled = disabled || loading;

  const getContainerColors = (pressed: boolean) => {
    if (variant === 'primary') {
      return {
        backgroundColor: primaryColor,
        borderColor: primaryColor,
        opacity: isDisabled ? 0.55 : pressed ? 0.88 : 1,
      };
    }
    if (variant === 'secondary') {
      return {
        backgroundColor: pressed ? colors.surfaceSubtle : colors.surface,
        borderColor: colors.border,
        opacity: isDisabled ? 0.55 : 1,
      };
    }
    if (variant === 'danger') {
      return {
        backgroundColor: pressed
          ? isDark
            ? '#5C1E1E'
            : '#FDECEC'
          : colors.expenseSoft,
        borderColor: isDark ? '#7F1D1D' : '#FECACA',
        opacity: isDisabled ? 0.55 : 1,
      };
    }
    return {
      backgroundColor: pressed ? colors.surfaceSubtle : 'transparent',
      borderColor: 'transparent',
      opacity: isDisabled ? 0.55 : 1,
    };
  };

  const labelColor =
    variant === 'primary'
      ? colors.inkOnDark
      : variant === 'danger'
        ? colors.expense
        : colors.ink;

  return (
    <Pressable
      onPress={onPress}
      disabled={isDisabled}
      accessibilityRole="button"
      accessibilityState={{ disabled: isDisabled, busy: loading }}
      style={({ pressed }) => [
        styles.button,
        getContainerColors(pressed),
        pressed && !isDisabled && styles.pressed,
        style,
      ]}
    >
      {loading ? (
        <ActivityIndicator size="small" color={labelColor} />
      ) : (
        <View style={styles.content}>
          {leftIcon ? <View style={styles.iconSlot}>{leftIcon}</View> : null}
          <Text style={[styles.label, { color: labelColor }]}>{label}</Text>
        </View>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    height: 52,
    borderRadius: radii.md,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.lg,
  },
  pressed: {
    transform: [{ scale: 0.99 }],
  },
  content: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
  },
  iconSlot: {
    marginRight: spacing.sm,
  },
  label: {
    fontSize: 15,
    fontWeight: '600',
    letterSpacing: -0.1,
  },
});
