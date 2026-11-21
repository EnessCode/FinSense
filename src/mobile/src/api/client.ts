import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { Platform } from 'react-native';
import Constants from 'expo-constants';
import i18n, { getActiveLocale } from '@/i18n';
import { tokenStorage } from './storage';
import type { AuthTokensDto, ProblemDetails } from './types';

export const BACKEND_PORT = 5000;

export function resolveApiBaseUrl(): string {
  const envUrl = process.env.EXPO_PUBLIC_API_URL?.trim();

  // On web, keep using localhost (or explicit custom URL)
  if (Platform.OS === 'web') {
    return envUrl && !envUrl.includes('localhost') && !envUrl.includes('127.0.0.1')
      ? envUrl.replace(/\/+$/, '')
      : `http://localhost:${BACKEND_PORT}/api/v1`;
  }

  // On physical device / Expo Go:
  // Use explicit URL only if it doesn't point to localhost/127.0.0.1
  if (envUrl && !envUrl.includes('localhost') && !envUrl.includes('127.0.0.1')) {
    return envUrl.replace(/\/+$/, '');
  }

  // Dynamically extract the PC's LAN IP address from Expo hostUri
  const hostUri =
    Constants.expoConfig?.hostUri ??
    (Constants as any).manifest2?.extra?.expoClient?.hostUri ??
    (Constants as any).manifest?.debuggerHost;

  const lanIp = hostUri ? hostUri.split(':')[0] : null;
  if (lanIp) {
    return `http://${lanIp}:${BACKEND_PORT}/api/v1`;
  }

  return `http://localhost:${BACKEND_PORT}/api/v1`;
}

export const API_BASE_URL = resolveApiBaseUrl();

export class ApiError extends Error {
  status: number;
  errorCode: string;
  fieldErrors: Record<string, string[]>;
  isNetworkError: boolean;

  constructor(
    message: string,
    options?: {
      status?: number;
      errorCode?: string;
      fieldErrors?: Record<string, string[]>;
      isNetworkError?: boolean;
    },
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = options?.status ?? 0;
    this.errorCode = options?.errorCode ?? 'UNKNOWN_ERROR';
    this.fieldErrors = options?.fieldErrors ?? {};
    this.isNetworkError = options?.isNetworkError ?? false;
  }
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }

  if (axios.isAxiosError<ProblemDetails>(error)) {
    if (!error.response) {
      return new ApiError(i18n.t('common.networkError'), {
        status: 0,
        errorCode: 'NETWORK_ERROR',
        isNetworkError: true,
      });
    }

    const data = error.response.data;
    const fieldErrors = data?.errors ?? {};
    const firstFieldError = Object.values(fieldErrors).flat()[0];

    return new ApiError(
      firstFieldError || data?.detail || data?.title || i18n.t('common.unknownError'),
      {
        status: error.response.status,
        errorCode: data?.errorCode ?? 'API_ERROR',
        fieldErrors,
      },
    );
  }

  return new ApiError(i18n.t('common.unknownError'));
}

type SessionExpiredListener = () => void;
const sessionExpiredListeners = new Set<SessionExpiredListener>();

export function onSessionExpired(listener: SessionExpiredListener): () => void {
  sessionExpiredListeners.add(listener);
  return () => {
    sessionExpiredListeners.delete(listener);
  };
}

function notifySessionExpired(): void {
  sessionExpiredListeners.forEach((listener) => listener());
}

interface RetriableRequestConfig extends InternalAxiosRequestConfig {
  _retry?: boolean;
}

const AUTH_BYPASS_PATHS = ['/auth/login', '/auth/register', '/auth/refresh'];

let refreshInFlight: Promise<string | null> | null = null;

async function rotateRefreshToken(): Promise<string | null> {
  const refreshToken = await tokenStorage.getRefreshToken();
  if (!refreshToken) {
    return null;
  }

  try {
    const response = await axios.post<AuthTokensDto>(
      `${API_BASE_URL}/auth/refresh`,
      { refreshToken },
      {
        headers: {
          'Content-Type': 'application/json',
          'Accept-Language': getActiveLocale(),
        },
        timeout: 15_000,
      },
    );

    await tokenStorage.saveTokens(response.data);
    return response.data.accessToken;
  } catch {
    await tokenStorage.clearTokens();
    notifySessionExpired();
    return null;
  }
}

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  timeout: 15_000,
  headers: {
    'Content-Type': 'application/json',
  },
});

apiClient.interceptors.request.use(async (config) => {
  config.headers = config.headers ?? {};
  config.headers['Accept-Language'] = getActiveLocale();

  // Never force application/json on FormData uploads; let React Native / Web set multipart boundary automatically.
  if (typeof FormData !== 'undefined' && config.data instanceof FormData) {
    if (typeof config.headers.delete === 'function') {
      config.headers.delete('Content-Type');
    } else {
      delete config.headers['Content-Type'];
    }
  }

  const url = config.url ?? '';
  const isBypass = AUTH_BYPASS_PATHS.some((path) => url.includes(path));

  if (!isBypass) {
    const accessToken = await tokenStorage.getAccessToken();
    if (accessToken) {
      config.headers.Authorization = `Bearer ${accessToken}`;
    }
  }

  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ProblemDetails>) => {
    const originalRequest = error.config as RetriableRequestConfig | undefined;
    const status = error.response?.status;
    const requestUrl = originalRequest?.url ?? '';
    const isBypass = AUTH_BYPASS_PATHS.some((path) => requestUrl.includes(path));

    if (status === 401 && originalRequest && !originalRequest._retry && !isBypass) {
      originalRequest._retry = true;

      if (!refreshInFlight) {
        refreshInFlight = rotateRefreshToken().finally(() => {
          refreshInFlight = null;
        });
      }

      const newAccessToken = await refreshInFlight;
      if (newAccessToken) {
        originalRequest.headers = originalRequest.headers ?? {};
        originalRequest.headers.Authorization = `Bearer ${newAccessToken}`;
        return apiClient(originalRequest);
      }
    }

    return Promise.reject(toApiError(error));
  },
);
