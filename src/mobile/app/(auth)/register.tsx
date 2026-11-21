import { Link } from 'expo-router';
import { AlertCircle, Lock, Mail, User } from 'lucide-react-native';
import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { toApiError } from '@/api/client';
import { AppButton, AppInput, SurfaceCard } from '@/components/ui';
import { useAuth } from '@/context/AuthContext';
import { useTheme } from '@/context/ThemeContext';
import { setAppLocale } from '@/i18n';
import { radii, spacing, typography, workspaceThemes } from '@/theme/tokens';
import type { SupportedLocale } from '@/utils/formatters';

const PERSONAL_PRIMARY = workspaceThemes.Personal.primarySurface;
const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export default function RegisterScreen() {
  const { t, i18n } = useTranslation();
  const insets = useSafeAreaInsets();
  const { signUp } = useAuth();
  const { colors, isDark } = useTheme();
  const brandAccent = isDark ? '#5EEAD4' : PERSONAL_PRIMARY;

  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [locale, setLocale] = useState<SupportedLocale>(
    i18n.language === 'en-US' ? 'en-US' : 'tr-TR',
  );

  const [nameError, setNameError] = useState<string | undefined>();
  const [emailError, setEmailError] = useState<string | undefined>();
  const [passwordError, setPasswordError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | undefined>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleLocaleChange = async (nextLocale: SupportedLocale) => {
    setLocale(nextLocale);
    await setAppLocale(nextLocale);
  };

  const validate = (): boolean => {
    let valid = true;

    if (!displayName.trim()) {
      setNameError(t('auth.validation.displayNameRequired'));
      valid = false;
    } else {
      setNameError(undefined);
    }

    const trimmedEmail = email.trim();
    if (!trimmedEmail) {
      setEmailError(t('auth.validation.emailRequired'));
      valid = false;
    } else if (!EMAIL_REGEX.test(trimmedEmail)) {
      setEmailError(t('auth.validation.emailInvalid'));
      valid = false;
    } else {
      setEmailError(undefined);
    }

    if (!password) {
      setPasswordError(t('auth.validation.passwordRequired'));
      valid = false;
    } else if (password.length < 8) {
      setPasswordError(t('auth.validation.passwordMin'));
      valid = false;
    } else if (!/[A-Z]/.test(password)) {
      setPasswordError(t('auth.validation.passwordUpper'));
      valid = false;
    } else if (!/[a-z]/.test(password)) {
      setPasswordError(t('auth.validation.passwordLower'));
      valid = false;
    } else if (!/[0-9]/.test(password)) {
      setPasswordError(t('auth.validation.passwordDigit'));
      valid = false;
    } else {
      setPasswordError(undefined);
    }

    return valid;
  };

  const handleSubmit = async () => {
    setServerError(undefined);
    if (!validate()) {
      return;
    }

    setIsSubmitting(true);
    try {
      await signUp({
        displayName: displayName.trim(),
        email: email.trim(),
        password,
        preferredLocale: locale,
      });
    } catch (err) {
      const apiErr = toApiError(err);
      setServerError(apiErr.message);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <KeyboardAvoidingView
      style={[styles.flex, { backgroundColor: colors.canvas }]}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
    >
      <ScrollView
        contentContainerStyle={[
          styles.scrollContent,
          {
            paddingTop: Math.max(insets.top, spacing.xl) + spacing.lg,
            paddingBottom: Math.max(insets.bottom, spacing.xl) + spacing.lg,
          },
        ]}
        keyboardShouldPersistTaps="handled"
        showsVerticalScrollIndicator={false}
      >
        <View style={styles.headerBlock}>
          <Text style={[styles.overline, { color: brandAccent }]}>{t('common.appName')}</Text>
          <Text style={[styles.title, { color: colors.ink }]}>{t('auth.registerTitle')}</Text>
          <Text style={[styles.subtitle, { color: colors.inkSecondary }]}>{t('auth.registerSubtitle')}</Text>
        </View>

        <SurfaceCard padding="xl" style={styles.formCard}>
          {serverError ? (
            <View
              style={[
                styles.errorBanner,
                {
                  backgroundColor: colors.expenseSoft,
                  borderColor: colors.expense,
                },
              ]}
            >
              <AlertCircle size={17} color={colors.expense} strokeWidth={1.75} />
              <Text style={[styles.errorBannerText, { color: colors.expense }]}>{serverError}</Text>
            </View>
          ) : null}

          <AppInput
            label={t('auth.displayNameLabel')}
            placeholder={t('auth.displayNamePlaceholder')}
            value={displayName}
            onChangeText={(text) => {
              setDisplayName(text);
              if (nameError) setNameError(undefined);
            }}
            error={nameError}
            autoCapitalize="words"
            autoComplete="name"
            returnKeyType="next"
            accentColor={PERSONAL_PRIMARY}
            leftAccessory={
              <User size={18} color={colors.inkSecondary} strokeWidth={1.75} />
            }
          />

          <AppInput
            label={t('auth.emailLabel')}
            placeholder={t('auth.emailPlaceholder')}
            value={email}
            onChangeText={(text) => {
              setEmail(text);
              if (emailError) setEmailError(undefined);
            }}
            error={emailError}
            autoCapitalize="none"
            autoComplete="email"
            keyboardType="email-address"
            returnKeyType="next"
            accentColor={PERSONAL_PRIMARY}
            leftAccessory={
              <Mail size={18} color={colors.inkSecondary} strokeWidth={1.75} />
            }
          />

          <AppInput
            label={t('auth.passwordLabel')}
            placeholder={t('auth.passwordPlaceholder')}
            value={password}
            onChangeText={(text) => {
              setPassword(text);
              if (passwordError) setPasswordError(undefined);
            }}
            error={passwordError}
            helperText={t('auth.passwordHint')}
            isPassword
            autoCapitalize="none"
            autoComplete="new-password"
            returnKeyType="done"
            onSubmitEditing={() => void handleSubmit()}
            accentColor={PERSONAL_PRIMARY}
            leftAccessory={
              <Lock size={18} color={colors.inkSecondary} strokeWidth={1.75} />
            }
          />

          <View style={styles.localeSection}>
            <Text style={[styles.localeLabel, { color: colors.ink }]}>{t('auth.localeLabel')}</Text>
            <View style={styles.localeRow}>
              {(['tr-TR', 'en-US'] as const).map((option) => {
                const selected = locale === option;
                return (
                  <Pressable
                    key={option}
                    onPress={() => void handleLocaleChange(option)}
                    style={[
                      styles.localeOption,
                      {
                        borderColor: colors.border,
                        backgroundColor: colors.surfaceSubtle,
                      },
                      selected && {
                        backgroundColor: isDark ? 'rgba(94, 234, 212, 0.14)' : workspaceThemes.Personal.softTint,
                        borderColor: brandAccent,
                      },
                    ]}
                  >
                    <Text
                      style={[
                        styles.localeOptionText,
                        { color: colors.inkSecondary },
                        selected && { color: brandAccent, fontWeight: '600' },
                      ]}
                    >
                      {option === 'tr-TR' ? 'Türkçe (TRY)' : 'English (TRY)'}
                    </Text>
                  </Pressable>
                );
              })}
            </View>
          </View>

          <AppButton
            label={t('auth.registerButton')}
            onPress={() => void handleSubmit()}
            loading={isSubmitting}
            primaryColor={PERSONAL_PRIMARY}
            style={styles.submitButton}
          />
        </SurfaceCard>

        <View style={styles.footerRow}>
          <Text style={[styles.footerPrompt, { color: colors.inkSecondary }]}>{t('auth.hasAccountPrompt')}</Text>
          <Link href="/(auth)/login" asChild>
            <Pressable hitSlop={8}>
              <Text style={[styles.footerLink, { color: brandAccent }]}>{t('auth.loginLink')}</Text>
            </Pressable>
          </Link>
        </View>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: {
    flex: 1,
  },
  scrollContent: {
    flexGrow: 1,
    paddingHorizontal: spacing.xl,
    justifyContent: 'center',
  },
  headerBlock: {
    marginBottom: spacing.xl,
    gap: spacing.xs + 2,
  },
  overline: {
    ...typography.overline,
    letterSpacing: 1.6,
    marginBottom: spacing.xs,
  },
  title: {
    ...typography.editorialDisplay,
  },
  subtitle: {
    ...typography.body,
  },
  formCard: {
    gap: spacing.base,
  },
  errorBanner: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    padding: spacing.md,
    borderRadius: radii.md,
    borderWidth: 1,
  },
  errorBannerText: {
    flex: 1,
    fontSize: 13,
    lineHeight: 18,
    fontWeight: '500',
  },
  localeSection: {
    gap: spacing.xs + 2,
  },
  localeLabel: {
    ...typography.caption,
    fontWeight: '600',
  },
  localeRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  localeOption: {
    flex: 1,
    height: 42,
    borderRadius: radii.sm,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  localeOptionText: {
    fontSize: 13,
    fontWeight: '500',
  },
  submitButton: {
    marginTop: spacing.xs,
  },
  footerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    marginTop: spacing.xl,
    gap: spacing.xs + 2,
  },
  footerPrompt: {
    ...typography.caption,
  },
  footerLink: {
    fontSize: 13.5,
    fontWeight: '700',
  },
});
