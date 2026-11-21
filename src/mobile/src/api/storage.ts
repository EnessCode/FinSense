import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';
import type { AuthTokensDto } from './types';

const KEYS = {
  ACCESS_TOKEN: 'finsense.auth.access_token',
  REFRESH_TOKEN: 'finsense.auth.refresh_token',
  ACCESS_EXPIRES_AT: 'finsense.auth.access_expires_at',
  ACTIVE_WORKSPACE_ID: 'finsense.workspace.active_id',
  THEME_MODE: 'finsense.theme.mode',
} as const;

async function setItem(key: string, value: string): Promise<void> {
  if (Platform.OS === 'web') {
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(key, value);
    }
    return;
  }
  await SecureStore.setItemAsync(key, value);
}

async function getItem(key: string): Promise<string | null> {
  if (Platform.OS === 'web') {
    if (typeof localStorage !== 'undefined') {
      return localStorage.getItem(key);
    }
    return null;
  }
  return SecureStore.getItemAsync(key);
}

async function deleteItem(key: string): Promise<void> {
  if (Platform.OS === 'web') {
    if (typeof localStorage !== 'undefined') {
      localStorage.removeItem(key);
    }
    return;
  }
  await SecureStore.deleteItemAsync(key);
}

export const tokenStorage = {
  async saveTokens(tokens: AuthTokensDto): Promise<void> {
    await Promise.all([
      setItem(KEYS.ACCESS_TOKEN, tokens.accessToken),
      setItem(KEYS.REFRESH_TOKEN, tokens.refreshToken),
      setItem(KEYS.ACCESS_EXPIRES_AT, tokens.accessTokenExpiresAtUtc),
    ]);
  },

  async getAccessToken(): Promise<string | null> {
    return getItem(KEYS.ACCESS_TOKEN);
  },

  async getRefreshToken(): Promise<string | null> {
    return getItem(KEYS.REFRESH_TOKEN);
  },

  async clearTokens(): Promise<void> {
    await Promise.all([
      deleteItem(KEYS.ACCESS_TOKEN),
      deleteItem(KEYS.REFRESH_TOKEN),
      deleteItem(KEYS.ACCESS_EXPIRES_AT),
    ]);
  },

  async saveActiveWorkspaceId(workspaceId: string): Promise<void> {
    await setItem(KEYS.ACTIVE_WORKSPACE_ID, workspaceId);
  },

  async getActiveWorkspaceId(): Promise<string | null> {
    return getItem(KEYS.ACTIVE_WORKSPACE_ID);
  },

  async clearActiveWorkspaceId(): Promise<void> {
    await deleteItem(KEYS.ACTIVE_WORKSPACE_ID);
  },

  async saveThemeMode(mode: string): Promise<void> {
    await setItem(KEYS.THEME_MODE, mode);
  },

  async getThemeMode(): Promise<string | null> {
    return getItem(KEYS.THEME_MODE);
  },
};
