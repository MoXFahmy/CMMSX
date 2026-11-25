import { Stack, Text } from '@mantine/core';
import { useTranslation } from 'react-i18next';

/**
 * A person limited to places that have all been deleted sees nothing. Taking the last such place off makes the stored sites
 * (or, with none, everything) their access, a silent widening (0.9.1): true when this edit does that. <paramref name="deleted"/> is
 * what the server says no longer exists: the editor's own tree can't tell "deleted" from "a place I'm not allowed to see".
 */
export function removesLastDeletedPlace(before: readonly string[], after: readonly string[], deleted: readonly string[]): boolean {
  return before.length > 0 && after.length === 0 && before.every((id) => deleted.includes(id));
}

/** What the person will see once the last deleted place is taken off: the sites stored on them, whole, or every site when none are. */
export function AccessAfterSave({ sites }: { sites: string[] }) {
  const { t } = useTranslation();
  return (
    <Stack gap="xs">
      <Text size="sm">{t('access.deletedPlaceNow')}</Text>
      <Text size="sm" fw={500}>{sites.length > 0 ? t('access.deletedPlaceAfterSites', { sites: sites.join(', ') }) : t('access.deletedPlaceAfterAll')}</Text>
    </Stack>
  );
}
