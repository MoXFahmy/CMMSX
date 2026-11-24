import { SegmentedControl } from '@mantine/core';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { put } from '@/api/client';
import { applyLanguage, LANGUAGES, pickLanguage, type Language } from '@/language';
import { notifyError } from '@/components/utils';

/**
 * English | العربية. Signed in, the choice is saved on the account (every device follows); on the sign-in page it only
 * changes this device. Each language is named in itself, so it can be found whichever one is showing.
 */
/** <c>onChanged</c> (0.28.0): runs after the language changed, e.g. the phone refreshing its stored copy (names come in the new language). */
export function LanguageSwitch({ saveToAccount = true, size = 'xs', onChanged }: { saveToAccount?: boolean; size?: 'xs' | 'sm'; onChanged?: () => void }) {
  const { i18n } = useTranslation();
  const qc = useQueryClient();
  const change = async (code: Language) => {
    applyLanguage(code);
    if (saveToAccount) {
      try {
        await put('/auth/me/language', { language: code });
        await qc.invalidateQueries({ queryKey: ['me'] });
      } catch (e) { notifyError(e); }
    }
    onChanged?.();
  };
  return (
    <SegmentedControl size={size} value={pickLanguage(i18n.language)} onChange={(v) => void change(v as Language)}
      data={LANGUAGES.map((l) => ({ value: l.code, label: l.name }))} />
  );
}
