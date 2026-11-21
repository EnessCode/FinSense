import { Eye, EyeOff } from 'lucide-react-native';
import React, { useState } from 'react';
import {
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
  type StyleProp,
  type TextInputProps,
  type ViewStyle,
} from 'react-native';
import { useTheme } from '@/context/ThemeContext';
import { colors, radii, spacing, typography } from '@/theme/tokens';

export interface AppInputProps extends Omit<TextInputProps, 'style'> {
  label: string;
  error?: string;
  helperText?: string;
  accentColor?: string;
  isPassword?: boolean;
  leftAccessory?: React.ReactNode;
  containerStyle?: StyleProp<ViewStyle>;
}

export function AppInput({
  label,
  error,
  helperText,
  accentColor = '#0D2B22',
  isPassword = false,
  leftAccessory,
  containerStyle,
  onFocus,
  onBlur,
  ...rest
}: AppInputProps) {
  const { isDark } = useTheme();
  const [isFocused, setIsFocused] = useState(false);
  const [isPasswordVisible, setIsPasswordVisible] = useState(false);

  const effectiveAccent = isDark ? '#10B981' : accentColor;
  const borderColor = error
    ? isDark
      ? '#F87171'
      : colors.expense
    : isFocused
      ? effectiveAccent
      : isDark
        ? 'rgba(148, 163, 184, 0.25)'
        : colors.border;

  const wrapperBg = isDark
    ? '#182234'
    : isFocused
      ? colors.surface
      : colors.surfaceSubtle;

  return (
    <View style={[styles.container, containerStyle]}>
      <Text style={[styles.label, isDark && { color: '#E2E8F0' }]}>{label}</Text>

      <View
        style={[
          styles.inputWrapper,
          {
            borderColor,
            backgroundColor: wrapperBg,
          },
        ]}
      >
        {leftAccessory ? <View style={styles.leftAccessory}>{leftAccessory}</View> : null}

        <TextInput
          placeholderTextColor={isDark ? '#94A3B8' : colors.inkSubtle}
          secureTextEntry={isPassword && !isPasswordVisible}
          selectionColor={effectiveAccent}
          onFocus={(e) => {
            setIsFocused(true);
            onFocus?.(e);
          }}
          onBlur={(e) => {
            setIsFocused(false);
            onBlur?.(e);
          }}
          style={[styles.input, isDark && { color: '#F8FAFC' }]}
          {...rest}
        />

        {isPassword ? (
          <Pressable
            onPress={() => setIsPasswordVisible((prev) => !prev)}
            hitSlop={10}
            style={styles.eyeButton}
          >
            {isPasswordVisible ? (
              <EyeOff
                size={19}
                color={isDark ? '#94A3B8' : colors.inkSecondary}
                strokeWidth={1.75}
              />
            ) : (
              <Eye
                size={19}
                color={isDark ? '#94A3B8' : colors.inkSecondary}
                strokeWidth={1.75}
              />
            )}
          </Pressable>
        ) : null}
      </View>

      {error ? (
        <Text style={[styles.errorText, isDark && { color: '#F87171' }]}>
          {error}
        </Text>
      ) : helperText ? (
        <Text style={[styles.helperText, isDark && { color: '#94A3B8' }]}>
          {helperText}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    width: '100%',
  },
  label: {
    ...typography.caption,
    fontWeight: '600',
    color: colors.ink,
    marginBottom: spacing.xs + 2,
    letterSpacing: -0.1,
  },
  inputWrapper: {
    flexDirection: 'row',
    alignItems: 'center',
    height: 52,
    borderRadius: radii.md,
    borderWidth: 1,
    paddingHorizontal: spacing.base,
  },
  leftAccessory: {
    marginRight: spacing.sm + 2,
  },
  input: {
    flex: 1,
    fontSize: 15,
    color: colors.ink,
    paddingVertical: 0,
  },
  eyeButton: {
    marginLeft: spacing.sm,
    padding: spacing.xs,
  },
  errorText: {
    marginTop: spacing.xs + 2,
    fontSize: 12.5,
    lineHeight: 17,
    color: colors.expense,
    fontWeight: '500',
  },
  helperText: {
    marginTop: spacing.xs + 2,
    fontSize: 12.5,
    lineHeight: 17,
    color: colors.inkSecondary,
  },
});
