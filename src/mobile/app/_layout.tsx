import '@/i18n';
import { Stack, useRouter, useSegments } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { ArrowLeft } from 'lucide-react-native';
import React, { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { AuthProvider, useAuth } from '@/context/AuthContext';
import { ThemeProvider, useTheme } from '@/context/ThemeContext';
import { WorkspaceProvider } from '@/context/WorkspaceContext';

function AuthNavigationGate() {
  const { t } = useTranslation();
  const { isAuthenticated, isLoading } = useAuth();
  const { colors: themeColors, isDark } = useTheme();
  const segments = useSegments();
  const router = useRouter();

  useEffect(() => {
    if (isLoading) {
      return;
    }

    const inAuthGroup = segments[0] === '(auth)';

    if (!isAuthenticated && !inAuthGroup) {
      router.replace('/(auth)/login');
    } else if (isAuthenticated && inAuthGroup) {
      router.replace('/(tabs)');
    }
  }, [isAuthenticated, isLoading, segments, router]);

  if (isLoading) {
    return (
      <View style={[styles.splashContainer, { backgroundColor: themeColors.canvas }]}>
        <StatusBar style={isDark ? 'light' : 'dark'} />
        <ActivityIndicator size="large" color={isDark ? '#10B981' : '#0D2B22'} />
      </View>
    );
  }

  return (
    <>
      <StatusBar style={isDark ? 'light' : 'dark'} />
      <Stack
        screenOptions={{
          headerShown: false,
          contentStyle: { backgroundColor: themeColors.canvas },
          animation: 'fade',
        }}
      >
        <Stack.Screen name="(auth)" />
        <Stack.Screen name="(tabs)" />
        <Stack.Screen
          name="transactions"
          options={{
            headerShown: true,
            title: 'Tüm İşlemler',
            headerStyle: { backgroundColor: themeColors.canvas },
            headerTintColor: themeColors.ink,
            headerTitleStyle: {
              color: themeColors.ink,
              fontWeight: '600',
              fontSize: 18,
            },
            headerShadowVisible: false,
            animation: 'slide_from_right',
            headerLeft: () => (
              <Pressable
                onPress={() =>
                  router.canGoBack() ? router.back() : router.replace('/(tabs)')
                }
                hitSlop={10}
                style={styles.headerBackButton}
                accessibilityRole="button"
                accessibilityLabel={t('common.close')}
              >
                <ArrowLeft size={22} color={themeColors.ink} strokeWidth={2} />
              </Pressable>
            ),
          }}
        />
        <Stack.Screen
          name="settings"
          options={{
            headerShown: true,
            title: t('tabs.settings'),
            headerStyle: { backgroundColor: themeColors.canvas },
            headerTintColor: themeColors.ink,
            headerTitleStyle: {
              color: themeColors.ink,
              fontWeight: '600',
              fontSize: 18,
            },
            headerShadowVisible: false,
            animation: 'slide_from_right',
            headerLeft: () => (
              <Pressable
                onPress={() =>
                  router.canGoBack() ? router.back() : router.replace('/(tabs)')
                }
                hitSlop={10}
                style={styles.headerBackButton}
                accessibilityRole="button"
                accessibilityLabel={t('common.close')}
              >
                <ArrowLeft size={22} color={themeColors.ink} strokeWidth={2} />
              </Pressable>
            ),
          }}
        />
      </Stack>
    </>
  );
}

export default function RootLayout() {
  return (
    <SafeAreaProvider>
      <ThemeProvider>
        <AuthProvider>
          <WorkspaceProvider>
            <AuthNavigationGate />
          </WorkspaceProvider>
        </AuthProvider>
      </ThemeProvider>
    </SafeAreaProvider>
  );
}

const styles = StyleSheet.create({
  splashContainer: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  headerBackButton: {
    paddingRight: 12,
    paddingVertical: 4,
  },
});
