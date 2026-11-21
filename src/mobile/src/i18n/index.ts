import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import en from './locales/en';
import tr from './locales/tr';
import type { SupportedLocale } from '@/utils/formatters';

export const resources = {
  'tr-TR': { translation: tr },
  'en-US': { translation: en },
} as const;

if (!i18n.isInitialized) {
  i18n.use(initReactI18next).init({
    resources,
    lng: 'tr-TR',
    fallbackLng: 'tr-TR',
    interpolation: {
      escapeValue: false,
    },
    compatibilityJSON: 'v4',
  });
}

export function getActiveLocale(): SupportedLocale {
  return i18n.language === 'en-US' ? 'en-US' : 'tr-TR';
}

export async function setAppLocale(locale: SupportedLocale): Promise<void> {
  if (i18n.language !== locale) {
    await i18n.changeLanguage(locale);
  }
}

export default i18n;
