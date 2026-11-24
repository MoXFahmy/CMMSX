import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';
import ar from './locales/ar.json';

void i18n.use(initReactI18next).init({
  // A phrase missing from Arabic shows in English (fallbackLng) rather than as its key.
  // Copies: the organization's list names (0.21.0) are written into i18next's store, never into the bundled text.
  resources: { en: { translation: structuredClone(en) }, ar: { translation: structuredClone(ar) } },
  lng: 'en',
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  // Screens show the organization's list names (0.21.0) as soon as they're added.
  react: { bindI18nStore: 'added' },
});

export default i18n;
