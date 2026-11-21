import { router, Tabs, useNavigation } from 'expo-router';
import {
  ArrowLeftRight,
  LayoutGrid,
  PieChart,
  Plus,
  Settings,
  Sparkles,
  TrendingUp,
} from 'lucide-react-native';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useTheme } from '@/context/ThemeContext';
import { useWorkspace } from '@/context/WorkspaceContext';
import { useKeyboardHeight } from '@/hooks/useKeyboardHeight';
import { colors, radii, spacing } from '@/theme/tokens';

const VISIBLE_TAB_NAMES = [
  'index',
  'budgets',
  'add',
  'investments',
  'assistant',
] as const;

interface TabRoute {
  key: string;
  name: string;
  params?: Record<string, unknown>;
}

interface CustomTabBarProps {
  state?: {
    routes: TabRoute[];
    index: number;
    key?: string;
  };
  descriptors?: Record<
    string,
    {
      options?: {
        tabBarAccessibilityLabel?: string;
        tabBarButtonTestID?: string;
        title?: string;
        [key: string]: unknown;
      };
      navigation?: {
        emit?: (event: { type: string; target: string; canPreventDefault?: boolean }) => {
          defaultPrevented?: boolean;
        };
        navigate?: (name: string, params?: Record<string, unknown>) => void;
        [key: string]: unknown;
      };
      [key: string]: unknown;
    }
  >;
  navigation?: {
    emit?: (event: { type: string; target: string; canPreventDefault?: boolean }) => {
      defaultPrevented?: boolean;
    };
    navigate?: (name: string, params?: Record<string, unknown>) => void;
    [key: string]: unknown;
  };
}

function MinimalEditorialTabBar({
  state,
  descriptors,
  navigation: propNavigation,
}: CustomTabBarProps) {
  const { t } = useTranslation();
  const insets = useSafeAreaInsets();
  const { activeTheme } = useWorkspace();
  const { colors: themeColors, isDark } = useTheme();
  const keyboardHeight = useKeyboardHeight();
  const hookNavigation = useNavigation<any>();
  const activeTabTint = isDark
    ? activeTheme.mode === 'Household'
      ? '#FBBF24'
      : '#34D399'
    : activeTheme.primarySurface;
  const inactiveTabTint = isDark ? '#94A3B8' : themeColors.inkSubtle;
  const addButtonBg = isDark
    ? activeTheme.mode === 'Household'
      ? '#D97706'
      : '#10B981'
    : activeTheme.primarySurface;

  // Strictly filter state.routes to only the 5 symmetrical visible tabs in exact order
  const routes = state?.routes ?? [];
  const routeMap = new Map<string, TabRoute>(routes.map((r) => [r.name, r]));
  const visibleRoutes = VISIBLE_TAB_NAMES.map((name) => routeMap.get(name)).filter(
    (r): r is TabRoute => Boolean(r),
  );
  const displayRoutes: TabRoute[] =
    visibleRoutes.length > 0
      ? visibleRoutes
      : VISIBLE_TAB_NAMES.map((name) => ({ key: name, name }));

  const currentRoute = routes[state?.index ?? 0];

  const renderIcon = (routeName: string, focused: boolean) => {
    const color = focused ? activeTabTint : inactiveTabTint;
    switch (routeName) {
      case 'index':
        return <LayoutGrid size={19} color={color} strokeWidth={1.75} />;
      case 'budgets':
        return <PieChart size={19} color={color} strokeWidth={1.75} />;
      case 'investments':
        return <TrendingUp size={19} color={color} strokeWidth={1.75} />;
      case 'assistant':
        return <Sparkles size={19} color={color} strokeWidth={1.75} />;
      case 'transactions':
        return <ArrowLeftRight size={19} color={color} strokeWidth={1.75} />;
      case 'settings':
        return <Settings size={19} color={color} strokeWidth={1.75} />;
      default:
        return null;
    }
  };

  const getLabel = (routeName: string) => {
    switch (routeName) {
      case 'index':
        return t('tabs.overview');
      case 'budgets':
        return t('tabs.budgets');
      case 'add':
        return t('tabs.add');
      case 'investments':
        return t('tabs.investments');
      case 'assistant':
        return t('tabs.assistant');
      case 'transactions':
        return t('tabs.transactions');
      case 'settings':
        return t('tabs.settings');
      default:
        return routeName;
    }
  };

  if (keyboardHeight > 0) {
    return null;
  }

  return (
    <View
      style={[
        styles.tabBarContainer,
        {
          backgroundColor: isDark ? '#0E1420' : themeColors.surface,
          borderTopColor: isDark
            ? 'rgba(255, 255, 255, 0.07)'
            : themeColors.border,
          paddingBottom: Math.max(insets.bottom, spacing.xs),
        },
      ]}
    >
      <View style={styles.tabBarInner}>
        {displayRoutes.map((route) => {
          const isFocused =
            currentRoute?.key === route.key || currentRoute?.name === route.name;
          const isAddAction = route.name === 'add';
          const options = descriptors?.[route.key]?.options;

          const onPress = () => {
            const activeNav =
              propNavigation ??
              descriptors?.[route.key]?.navigation ??
              hookNavigation;

            const event = activeNav?.emit?.({
              type: 'tabPress',
              target: route.key,
              canPreventDefault: true,
            });

            if (!isFocused && !event?.defaultPrevented) {
              const routePath =
                route.name === 'index' ? '/(tabs)' : `/(tabs)/${route.name}`;

              if (typeof activeNav?.navigate === 'function') {
                try {
                  activeNav.navigate(route.name, route.params);
                } catch {
                  router.navigate(routePath as any);
                }
              } else {
                router.navigate(routePath as any);
              }
            }
          };

          const onLongPress = () => {
            const activeNav =
              propNavigation ??
              descriptors?.[route.key]?.navigation ??
              hookNavigation;

            activeNav?.emit?.({
              type: 'tabLongPress',
              target: route.key,
            });
          };

          if (isAddAction) {
            return (
              <View key={route.key} style={styles.tabItem}>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={getLabel(route.name)}
                  testID={options?.tabBarButtonTestID}
                  onPress={onPress}
                  onLongPress={onLongPress}
                  style={({ pressed }) => [
                    styles.addButton,
                    {
                      backgroundColor: addButtonBg,
                      opacity: pressed ? 0.88 : 1,
                    },
                  ]}
                >
                  <Plus size={18} color="#FFFFFF" strokeWidth={2} />
                  <Text style={styles.addButtonLabel}>{getLabel(route.name)}</Text>
                </Pressable>
              </View>
            );
          }

          return (
            <Pressable
              key={route.key}
              accessibilityRole="button"
              accessibilityState={isFocused ? { selected: true } : {}}
              accessibilityLabel={options?.tabBarAccessibilityLabel ?? getLabel(route.name)}
              testID={options?.tabBarButtonTestID}
              onPress={onPress}
              onLongPress={onLongPress}
              style={({ pressed }) => [
                styles.tabItem,
                pressed && styles.tabItemPressed,
              ]}
            >
              {renderIcon(route.name, isFocused)}
              <Text
                numberOfLines={1}
                style={[
                  styles.tabLabel,
                  {
                    color: isFocused ? activeTabTint : inactiveTabTint,
                    fontWeight: isFocused ? '600' : '500',
                  },
                ]}
              >
                {getLabel(route.name)}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

export default function TabsLayout() {
  return (
    <Tabs
      tabBar={(props) => <MinimalEditorialTabBar {...(props as any)} />}
      screenOptions={{
        headerShown: false,
        tabBarHideOnKeyboard: true,
      }}
    >
      {/* 5-Tab Perfect Mirror Symmetry: 2 Left + Center + 2 Right */}
      <Tabs.Screen name="index" />
      <Tabs.Screen name="budgets" />
      <Tabs.Screen name="add" />
      <Tabs.Screen name="investments" />
      <Tabs.Screen name="assistant" />
    </Tabs>
  );
}

const styles = StyleSheet.create({
  tabBarContainer: {
    backgroundColor: colors.surface,
    borderTopWidth: 1,
    borderTopColor: colors.border,
    paddingTop: spacing.xs + 2,
    width: '100%',
    alignItems: 'center',
  },
  tabBarInner: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    width: '100%',
    maxWidth: 720,
    alignSelf: 'center',
    paddingHorizontal: spacing.sm,
    height: 56,
  },
  tabItem: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: 4,
    paddingVertical: 4,
    minHeight: 48,
  },
  tabItemPressed: {
    opacity: 0.7,
  },
  tabLabel: {
    fontSize: 10.5,
    letterSpacing: -0.1,
  },
  addButton: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    height: 38,
    paddingHorizontal: spacing.md,
    borderRadius: radii.full,
    gap: 4,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.08,
    shadowRadius: 2,
    elevation: 2,
  },
  addButtonLabel: {
    fontSize: 12.5,
    fontWeight: '600',
    color: colors.inkOnDark,
    letterSpacing: -0.1,
  },
});
