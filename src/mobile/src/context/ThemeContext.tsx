import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react';
import { Platform, useColorScheme } from 'react-native';
import { tokenStorage } from '@/api/storage';
import { darkColors, lightColors, type ColorPalette } from '@/theme/tokens';

export type ThemeMode = 'system' | 'light' | 'dark';
export type ResolvedTheme = 'light' | 'dark';

interface ThemeContextValue {
  mode: ThemeMode;
  resolvedTheme: ResolvedTheme;
  isDark: boolean;
  colors: ColorPalette;
  setThemeMode: (mode: ThemeMode) => Promise<void>;
}

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

function isValidThemeMode(value: string | null): value is ThemeMode {
  return value === 'system' || value === 'light' || value === 'dark';
}

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const systemScheme = useColorScheme();
  const [mode, setModeState] = useState<ThemeMode>('system');

  useEffect(() => {
    let isMounted = true;

    async function hydrateTheme() {
      try {
        const saved = await tokenStorage.getThemeMode();
        if (isMounted && isValidThemeMode(saved)) {
          setModeState(saved);
        }
      } catch {
        // Default to system theme on storage read error.
      }
    }

    void hydrateTheme();

    return () => {
      isMounted = false;
    };
  }, []);

  const resolvedTheme: ResolvedTheme = useMemo(() => {
    if (mode === 'system') {
      return systemScheme === 'dark' ? 'dark' : 'light';
    }
    return mode;
  }, [mode, systemScheme]);

  const isDark = resolvedTheme === 'dark';

  useEffect(() => {
    if (Platform.OS === 'web' && typeof document !== 'undefined') {
      document.documentElement.style.colorScheme = resolvedTheme;
      document.body.style.backgroundColor = isDark
        ? darkColors.canvas
        : lightColors.canvas;
    }
  }, [resolvedTheme, isDark]);

  const setThemeMode = useCallback(async (nextMode: ThemeMode) => {
    setModeState(nextMode);
    try {
      await tokenStorage.saveThemeMode(nextMode);
    } catch {
      // Non-blocking persistence failure; in-memory theme still updates immediately.
    }
  }, []);

  const value = useMemo<ThemeContextValue>(
    () => ({
      mode,
      resolvedTheme,
      isDark,
      colors: isDark ? darkColors : lightColors,
      setThemeMode,
    }),
    [mode, resolvedTheme, isDark, setThemeMode],
  );

  return (
    <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) {
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return ctx;
}
