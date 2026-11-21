import Constants from 'expo-constants';
import { router } from 'expo-router';
import {
  Banknote,
  Globe,
  Home,
  Info,
  LogOut,
  Monitor,
  Moon,
  ShieldCheck,
  Sun,
} from 'lucide-react-native';
import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { AppButton, SurfaceCard, WorkspacePillSwitcher } from '@/components/ui';
import { useAuth } from '@/context/AuthContext';
import { useTheme, type ThemeMode } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { getActiveLocale, setAppLocale } from '@/i18n';
import { radii, spacing, typography } from '@/theme/tokens';
import type { SupportedLocale } from '@/utils/formatters';

function getInitials(name?: string | null): string {
  if (!name) return 'FS';
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) {
    return (parts[0] ?? 'FS').slice(0, 2).toUpperCase();
  }
  const first = parts[0]?.[0] ?? '';
  const last = parts[parts.length - 1]?.[0] ?? '';
  return `${first}${last}`.toUpperCase() || 'FS';
}

const THEME_OPTIONS: {
  mode: ThemeMode;
  labelKey: 'settings.themes.system' | 'settings.themes.light' | 'settings.themes.dark';
}[] = [
  { mode: 'system', labelKey: 'settings.themes.system' },
  { mode: 'light', labelKey: 'settings.themes.light' },
  { mode: 'dark', labelKey: 'settings.themes.dark' },
];

export default function SettingsScreen() {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { user, signOut } = useAuth();
  const { activeWorkspace, activeTheme } = useWorkspace();
  const { mode, isDark, colors: themeColors, setThemeMode } = useTheme();
  const activeLocale = getActiveLocale();
  const [isSigningOut, setIsSigningOut] = useState(false);

  const appVersion = Constants.expoConfig?.version ?? '1.0.0';
  const currencyCode = activeWorkspace?.defaultCurrency ?? 'TRY';
  const currencyDisplay =
    currencyCode === 'TRY'
      ? 'TRY (₺)'
      : currencyCode === 'USD'
        ? 'USD ($)'
        : currencyCode === 'EUR'
          ? 'EUR (€)'
          : currencyCode;

  const isHousehold = activeWorkspace?.type === 'Household';
  const accentColor = isDark
    ? isHousehold
      ? '#FBBF24'
      : '#34D399'
    : activeTheme.primarySurface;
  const activePillBg = isDark
    ? isHousehold
      ? '#D97706'
      : '#10B981'
    : activeTheme.primarySurface;

  const handleLocaleChange = async (locale: SupportedLocale) => {
    if (locale === activeLocale) return;
    await setAppLocale(locale);
  };

  const performSignOut = async () => {
    setIsSigningOut(true);
    try {
      await signOut();
      router.replace('/(auth)/login');
    } finally {
      setIsSigningOut(false);
    }
  };

  const handleSignOutPress = () => {
    if (Platform.OS === 'web') {
      const confirmed = window.confirm(t('auth.logoutConfirmBody'));
      if (confirmed) {
        void performSignOut();
      }
      return;
    }

    Alert.alert(
      t('auth.logoutConfirmTitle'),
      t('auth.logoutConfirmBody'),
      [
        { text: t('common.cancel'), style: 'cancel' },
        {
          text: t('auth.logout'),
          style: 'destructive',
          onPress: () => {
            void performSignOut();
          },
        },
      ],
      { cancelable: true },
    );
  };

  const renderThemeIcon = (optionMode: ThemeMode, selected: boolean) => {
    const iconColor = selected ? '#FFFFFF' : themeColors.inkSecondary;
    switch (optionMode) {
      case 'system':
        return <Monitor size={16} color={iconColor} strokeWidth={1.75} />;
      case 'light':
        return <Sun size={16} color={iconColor} strokeWidth={1.75} />;
      case 'dark':
        return <Moon size={16} color={iconColor} strokeWidth={1.75} />;
    }
  };

  return (
    <View style={[styles.screen, { backgroundColor: themeColors.canvas }]}>
      <ScrollView
        showsVerticalScrollIndicator={false}
        contentContainerStyle={[
          styles.scrollContent,
          { paddingBottom: Math.max(insets.bottom, spacing.xl) + spacing.xl },
        ]}
      >
        {/* 1. Hesap & Çalışma Alanı */}
        <View style={styles.sectionBlock}>
          <Text style={[styles.sectionTitle, { color: themeColors.inkSubtle }]}>
            {t('settings.sections.accountWorkspace')}
          </Text>

          <SurfaceCard padding="lg" style={styles.card}>
            {/* User Info Row */}
            <View style={styles.cardHeader}>
              <View
                style={[
                  styles.profileAvatar,
                  { backgroundColor: activeTheme.primarySurface },
                ]}
              >
                <Text style={styles.profileAvatarText}>
                  {getInitials(user?.displayName)}
                </Text>
              </View>
              <View style={styles.cardTitleCol}>
                <Text
                  style={[styles.profileName, { color: themeColors.ink }]}
                  numberOfLines={1}
                >
                  {user?.displayName || 'FinSense'}
                </Text>
                <Text
                  style={[styles.profileEmail, { color: themeColors.inkSecondary }]}
                  numberOfLines={1}
                >
                  {user?.email || '—'}
                </Text>
              </View>
            </View>

            <View
              style={[styles.divider, { backgroundColor: themeColors.border }]}
            />

            {/* Active Workspace Row */}
            <View style={styles.workspaceRow}>
              <View style={styles.workspaceLeft}>
                <View
                  style={[
                    styles.iconWrap,
                    {
                      backgroundColor: isDark
                        ? themeColors.surfaceSubtle
                        : activeTheme.softTint,
                      borderColor: themeColors.border,
                    },
                  ]}
                >
                  {isHousehold ? (
                    <Home size={18} color={accentColor} strokeWidth={1.75} />
                  ) : (
                    <ShieldCheck size={18} color={accentColor} strokeWidth={1.75} />
                  )}
                </View>
                <View style={styles.cardTitleCol}>
                  <Text
                    style={[styles.rowMetaLabel, { color: themeColors.inkSubtle }]}
                  >
                    {t('settings.activeWorkspace')}
                  </Text>
                  <Text
                    style={[styles.cardTitle, { color: themeColors.ink }]}
                    numberOfLines={1}
                  >
                    {activeWorkspace?.name ?? t('workspace.personalDefaultName')}
                  </Text>
                  <Text
                    style={[styles.cardSubtitle, { color: themeColors.inkSecondary }]}
                  >
                    {isHousehold
                      ? t('workspace.householdType')
                      : t('workspace.personalType')}{' '}
                    •{' '}
                    {t(
                      `workspace.roles.${activeWorkspace?.currentUserRole ?? 'Owner'}`,
                    )}
                  </Text>
                </View>
              </View>
            </View>

            <View style={styles.switcherWrap}>
              <WorkspacePillSwitcher />
            </View>
          </SurfaceCard>
        </View>

        {/* 2. Görünüm (Tema Seçimi: Sistem / Açık / Koyu) */}
        <View style={styles.sectionBlock}>
          <Text style={[styles.sectionTitle, { color: themeColors.inkSubtle }]}>
            {t('settings.sections.appearance')}
          </Text>

          <SurfaceCard padding="lg" style={styles.card}>
            <View style={styles.cardHeader}>
              <View
                style={[
                  styles.iconWrap,
                  {
                    backgroundColor: isDark
                      ? themeColors.surfaceSubtle
                      : activeTheme.softTint,
                    borderColor: themeColors.border,
                  },
                ]}
              >
                {isDark ? (
                  <Moon size={18} color={accentColor} strokeWidth={1.75} />
                ) : (
                  <Sun size={18} color={accentColor} strokeWidth={1.75} />
                )}
              </View>
              <View style={styles.cardTitleCol}>
                <Text style={[styles.cardTitle, { color: themeColors.ink }]}>
                  {t('settings.themeLabel')}
                </Text>
                <Text
                  style={[styles.cardSubtitle, { color: themeColors.inkSecondary }]}
                >
                  {t('settings.themeHint')}
                </Text>
              </View>
            </View>

            <View
              style={[
                styles.segmentedTrack,
                {
                  backgroundColor: themeColors.surfaceSubtle,
                  borderColor: themeColors.border,
                },
              ]}
            >
              {THEME_OPTIONS.map((option) => {
                const selected = mode === option.mode;
                return (
                  <Pressable
                    key={option.mode}
                    onPress={() => void setThemeMode(option.mode)}
                    accessibilityRole="radio"
                    accessibilityState={{ selected }}
                    accessibilityLabel={t(option.labelKey)}
                    style={[
                      styles.segmentedOption,
                      selected && {
                        backgroundColor: activePillBg,
                      },
                    ]}
                  >
                    {renderThemeIcon(option.mode, selected)}
                    <Text
                      style={[
                        styles.segmentedOptionText,
                        {
                          color: selected ? '#FFFFFF' : themeColors.inkSecondary,
                          fontWeight: selected ? '600' : '500',
                        },
                      ]}
                    >
                      {t(option.labelKey)}
                    </Text>
                  </Pressable>
                );
              })}
            </View>
          </SurfaceCard>
        </View>

        {/* 3. Uygulama (Dil, Para Birimi, Uygulama Sürümü) */}
        <View style={styles.sectionBlock}>
          <Text style={[styles.sectionTitle, { color: themeColors.inkSubtle }]}>
            {t('settings.sections.application')}
          </Text>

          <SurfaceCard padding="lg" style={styles.card}>
            {/* Currency Row */}
            <View style={styles.infoRow}>
              <View style={styles.infoRowLeft}>
                <View
                  style={[
                    styles.iconWrapSmall,
                    {
                      backgroundColor: themeColors.surfaceSubtle,
                      borderColor: themeColors.border,
                    },
                  ]}
                >
                  <Banknote size={16} color={accentColor} strokeWidth={1.75} />
                </View>
                <Text style={[styles.infoRowLabel, { color: themeColors.ink }]}>
                  {t('settings.currencyLabel')}
                </Text>
              </View>
              <View
                style={[
                  styles.valuePill,
                  {
                    backgroundColor: themeColors.surfaceSubtle,
                    borderColor: themeColors.border,
                  },
                ]}
              >
                <Text
                  style={[styles.valuePillText, { color: themeColors.ink }]}
                >
                  {currencyDisplay}
                </Text>
              </View>
            </View>

            <View
              style={[styles.divider, { backgroundColor: themeColors.border }]}
            />

            {/* App Version Row */}
            <View style={styles.infoRow}>
              <View style={styles.infoRowLeft}>
                <View
                  style={[
                    styles.iconWrapSmall,
                    {
                      backgroundColor: themeColors.surfaceSubtle,
                      borderColor: themeColors.border,
                    },
                  ]}
                >
                  <Info size={16} color={accentColor} strokeWidth={1.75} />
                </View>
                <Text style={[styles.infoRowLabel, { color: themeColors.ink }]}>
                  {t('settings.versionLabel')}
                </Text>
              </View>
              <Text
                style={[
                  styles.infoRowValue,
                  typography.tabularNums,
                  { color: themeColors.inkSecondary },
                ]}
              >
                v{appVersion}
              </Text>
            </View>

            <View
              style={[styles.divider, { backgroundColor: themeColors.border }]}
            />

            {/* Language Preference Row */}
            <View style={styles.localeSection}>
              <View style={styles.infoRowLeft}>
                <View
                  style={[
                    styles.iconWrapSmall,
                    {
                      backgroundColor: themeColors.surfaceSubtle,
                      borderColor: themeColors.border,
                    },
                  ]}
                >
                  <Globe size={16} color={accentColor} strokeWidth={1.75} />
                </View>
                <Text style={[styles.infoRowLabel, { color: themeColors.ink }]}>
                  {t('auth.localeLabel')}
                </Text>
              </View>

              <View style={styles.localeButtonsRow}>
                <Pressable
                  onPress={() => void handleLocaleChange('tr-TR')}
                  accessibilityRole="button"
                  accessibilityLabel="Türkçe (TR)"
                  style={[
                    styles.localePill,
                    {
                      backgroundColor: themeColors.surfaceSubtle,
                      borderColor: themeColors.border,
                    },
                    activeLocale === 'tr-TR' && {
                      backgroundColor: activePillBg,
                      borderColor: activePillBg,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.localePillText,
                      {
                        color:
                          activeLocale === 'tr-TR'
                            ? '#FFFFFF'
                            : themeColors.inkSecondary,
                        fontWeight: activeLocale === 'tr-TR' ? '600' : '500',
                      },
                    ]}
                  >
                    Türkçe (TR)
                  </Text>
                </Pressable>

                <Pressable
                  onPress={() => void handleLocaleChange('en-US')}
                  accessibilityRole="button"
                  accessibilityLabel="English (EN)"
                  style={[
                    styles.localePill,
                    {
                      backgroundColor: themeColors.surfaceSubtle,
                      borderColor: themeColors.border,
                    },
                    activeLocale === 'en-US' && {
                      backgroundColor: activePillBg,
                      borderColor: activePillBg,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.localePillText,
                      {
                        color:
                          activeLocale === 'en-US'
                            ? '#FFFFFF'
                            : themeColors.inkSecondary,
                        fontWeight: activeLocale === 'en-US' ? '600' : '500',
                      },
                    ]}
                  >
                    English (EN)
                  </Text>
                </Pressable>
              </View>
            </View>
          </SurfaceCard>
        </View>

        {/* 4. Oturum (Çıkış Yap) */}
        <View style={styles.sectionBlock}>
          <Text style={[styles.sectionTitle, { color: themeColors.inkSubtle }]}>
            {t('settings.sections.session')}
          </Text>

          <SurfaceCard padding="lg" style={styles.card}>
            <Text
              style={[styles.sessionHint, { color: themeColors.inkSecondary }]}
            >
              {t('settings.sessionHint')}
            </Text>

            <AppButton
              label={t('auth.logout')}
              variant="danger"
              loading={isSigningOut}
              leftIcon={
                <LogOut
                  size={17}
                  color={themeColors.expense}
                  strokeWidth={1.75}
                />
              }
              onPress={handleSignOutPress}
            />
          </SurfaceCard>
        </View>
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
  },
  scrollContent: {
    paddingHorizontal: spacing.xl,
    paddingTop: spacing.sm,
    gap: spacing.lg,
  },
  sectionBlock: {
    gap: spacing.sm,
  },
  sectionTitle: {
    ...typography.overline,
    paddingHorizontal: spacing.xs,
  },
  card: {
    gap: spacing.md,
  },
  cardHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  profileAvatar: {
    width: 48,
    height: 48,
    borderRadius: radii.full,
    alignItems: 'center',
    justifyContent: 'center',
  },
  profileAvatarText: {
    fontSize: 16,
    fontWeight: '700',
    color: '#FFFFFF',
    letterSpacing: 0.4,
  },
  cardTitleCol: {
    flex: 1,
    gap: 2,
  },
  profileName: {
    ...typography.bodyMedium,
    fontWeight: '600',
    fontSize: 16,
  },
  profileEmail: {
    ...typography.caption,
    fontSize: 13,
  },
  divider: {
    height: 1,
  },
  workspaceRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.sm,
  },
  workspaceLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    flex: 1,
  },
  switcherWrap: {
    paddingTop: spacing.xs,
  },
  rowMetaLabel: {
    fontSize: 11,
    fontWeight: '600',
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  iconWrap: {
    width: 40,
    height: 40,
    borderRadius: radii.md,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  iconWrapSmall: {
    width: 34,
    height: 34,
    borderRadius: radii.sm,
    borderWidth: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  cardTitle: {
    ...typography.bodyMedium,
    fontWeight: '600',
  },
  cardSubtitle: {
    ...typography.caption,
  },
  segmentedTrack: {
    flexDirection: 'row',
    padding: 4,
    borderRadius: radii.md,
    borderWidth: 1,
    gap: 4,
  },
  segmentedOption: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
    paddingVertical: 10,
    paddingHorizontal: spacing.sm,
    borderRadius: radii.sm,
  },
  segmentedOptionText: {
    fontSize: 13,
  },
  infoRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  infoRowLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    flex: 1,
  },
  infoRowLabel: {
    fontSize: 14.5,
    fontWeight: '500',
  },
  infoRowValue: {
    fontSize: 14,
    fontWeight: '500',
  },
  valuePill: {
    paddingHorizontal: spacing.md,
    paddingVertical: 5,
    borderRadius: radii.full,
    borderWidth: 1,
  },
  valuePillText: {
    fontSize: 13,
    fontWeight: '600',
  },
  localeSection: {
    gap: spacing.md,
  },
  localeButtonsRow: {
    flexDirection: 'row',
    gap: spacing.sm,
  },
  localePill: {
    flex: 1,
    paddingVertical: 10,
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: radii.sm,
    borderWidth: 1,
  },
  localePillText: {
    fontSize: 13,
  },
  sessionHint: {
    fontSize: 13,
    lineHeight: 19,
  },
});
