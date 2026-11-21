import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react';
import { onSessionExpired } from '@/api/client';
import { authApi, usersApi, type LoginPayload, type RegisterPayload } from '@/api/endpoints';
import { tokenStorage } from '@/api/storage';
import type { UserProfileDto } from '@/api/types';
import { setAppLocale } from '@/i18n';
import type { SupportedLocale } from '@/utils/formatters';

interface AuthContextValue {
  user: UserProfileDto | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  signIn: (payload: LoginPayload) => Promise<void>;
  signUp: (payload: RegisterPayload) => Promise<void>;
  signOut: () => Promise<void>;
  refreshProfile: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

function normalizeLocale(locale?: string | null): SupportedLocale {
  return locale === 'en-US' ? 'en-US' : 'tr-TR';
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<UserProfileDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const loadCurrentUser = useCallback(async () => {
    const profile = await usersApi.getMe();
    await setAppLocale(normalizeLocale(profile.preferredLocale));
    setUser(profile);
  }, []);

  useEffect(() => {
    let isMounted = true;

    async function hydrateSession() {
      try {
        const [accessToken, refreshToken] = await Promise.all([
          tokenStorage.getAccessToken(),
          tokenStorage.getRefreshToken(),
        ]);

        if (!accessToken && !refreshToken) {
          return;
        }

        await loadCurrentUser();
      } catch {
        await tokenStorage.clearTokens();
        if (isMounted) {
          setUser(null);
        }
      } finally {
        if (isMounted) {
          setIsLoading(false);
        }
      }
    }

    void hydrateSession();

    const unsubscribe = onSessionExpired(() => {
      if (isMounted) {
        setUser(null);
      }
    });

    return () => {
      isMounted = false;
      unsubscribe();
    };
  }, [loadCurrentUser]);

  const signIn = useCallback(
    async (payload: LoginPayload) => {
      const auth = await authApi.login(payload);
      await tokenStorage.saveTokens(auth.tokens);
      await loadCurrentUser();
    },
    [loadCurrentUser],
  );

  const signUp = useCallback(
    async (payload: RegisterPayload) => {
      const auth = await authApi.register(payload);
      await tokenStorage.saveTokens(auth.tokens);
      await loadCurrentUser();
    },
    [loadCurrentUser],
  );

  const signOut = useCallback(async () => {
    try {
      const refreshToken = await tokenStorage.getRefreshToken();
      await authApi.logout(refreshToken);
    } catch {
      // Best-effort revocation; local credentials are always cleared below.
    } finally {
      await Promise.all([
        tokenStorage.clearTokens(),
        tokenStorage.clearActiveWorkspaceId(),
      ]);
      setUser(null);
    }
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isLoading,
      isAuthenticated: user !== null,
      signIn,
      signUp,
      signOut,
      refreshProfile: loadCurrentUser,
    }),
    [user, isLoading, signIn, signUp, signOut, loadCurrentUser],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return ctx;
}
