import { Link } from 'expo-router';
import { AlertCircle, Lock, Mail } from 'lucide-react-native';
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
import { radii, spacing, typography, workspaceThemes } from '@/theme/tokens';

const PERSONAL_PRIMARY = workspaceThemes.Personal.primarySurface;
const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export default function LoginScreen() {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { signIn } = useAuth();
  const { colors, isDark } = useTheme();
  const brandAccent = isDark ? '#5EEAD4' : PERSONAL_PRIMARY;

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [emailError, setEmailError] = useState<string | undefined>();
  const [passwordError, setPasswordError] = useState<string | undefined>();
  const [serverError, setServerError] = useState<string | undefined>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const validate = (): boolean => {
    let valid = true;
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
      await signIn({
        email: email.trim(),
        password,
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
            paddingTop: Math.max(insets.top, spacing.xl) + spacing.xl,
            paddingBottom: Math.max(insets.bottom, spacing.xl) + spacing.lg,
          },
        ]}
        keyboardShouldPersistTaps="handled"
        showsVerticalScrollIndicator={false}
      >
        <View style={styles.brandBlock}>
          <View style={styles.monogramBadge}>
            <Text style={[styles.monogramText, { color: colors.inkOnDark }]}>F</Text>
          </View>
          <Text style={[styles.brandName, { color: brandAccent }]}>{t('common.appName')}</Text>
          <Text style={[styles.brandTagline, { color: colors.inkSecondary }]}>{t('auth.brandTagline')}</Text>
        </View>

        <View style={styles.headerBlock}>
          <Text style={[styles.title, { color: colors.ink }]}>{t('auth.loginTitle')}</Text>
          <Text style={[styles.subtitle, { color: colors.inkSecondary }]}>{t('auth.loginSubtitle')}</Text>
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
            isPassword
            autoCapitalize="none"
            autoComplete="password"
            returnKeyType="done"
            onSubmitEditing={() => void handleSubmit()}
            accentColor={PERSONAL_PRIMARY}
            leftAccessory={
              <Lock size={18} color={colors.inkSecondary} strokeWidth={1.75} />
            }
          />

          <AppButton
            label={t('auth.loginButton')}
            onPress={() => void handleSubmit()}
            loading={isSubmitting}
            primaryColor={PERSONAL_PRIMARY}
            style={styles.submitButton}
          />
        </SurfaceCard>

        <View style={styles.footerRow}>
          <Text style={[styles.footerPrompt, { color: colors.inkSecondary }]}>{t('auth.noAccountPrompt')}</Text>
          <Link href="/(auth)/register" asChild>
            <Pressable hitSlop={8}>
              <Text style={[styles.footerLink, { color: brandAccent }]}>{t('auth.createAccountLink')}</Text>
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
  brandBlock: {
    marginBottom: spacing.xxl,
  },
  monogramBadge: {
    width: 44,
    height: 44,
    borderRadius: radii.md,
    backgroundColor: PERSONAL_PRIMARY,
    alignItems: 'center',
    justifyContent: 'center',
    marginBottom: spacing.base,
  },
  monogramText: {
    fontFamily: typography.editorialDisplay.fontFamily,
    fontSize: 22,
    fontWeight: '700',
  },
  brandName: {
    ...typography.overline,
    fontSize: 12,
    letterSpacing: 1.8,
    marginBottom: spacing.xs,
  },
  brandTagline: {
    ...typography.caption,
  },
  headerBlock: {
    marginBottom: spacing.xl,
    gap: spacing.xs + 2,
  },
  title: {
    ...typography.editorialDisplay,
  },
  subtitle: {
    ...typography.body,
  },
  formCard: {
    gap: spacing.lg,
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
