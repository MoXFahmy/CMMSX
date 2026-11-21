import { useEffect, type ReactNode } from 'react';
import { useDirection } from '@mantine/core';
import { DatesProvider } from '@mantine/dates';
import { useTranslation } from 'react-i18next';
import { directionOf, pickLanguage } from './language';

/** Keeps Mantine's direction and the date pickers' language in step with the chosen language. */
export function LocaleProviders({ children }: { children: ReactNode }) {
  const { i18n } = useTranslation();
  const language = pickLanguage(i18n.language);
  const { setDirection } = useDirection();
  useEffect(() => { setDirection(directionOf(language)); }, [language, setDirection]);
  // Egypt's week starts on Saturday; English keeps Monday.
  return <DatesProvider settings={{ locale: language, firstDayOfWeek: language === 'ar' ? 6 : 1 }}>{children}</DatesProvider>;
}
