import { Alert, Anchor, Text } from '@mantine/core';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconFileCertificate } from '@tabler/icons-react';
import type { CoveredBy } from '@/api/types';
import { P, useAuth } from '@/auth/AuthContext';

/** "Covered by CON-00003 · Lift Co" (0.17.0): the asset is under an active service contract. The number links to it for people who may see contracts. */
export function CoveredBanner({ covered }: { covered?: CoveredBy | null }) {
  const { t } = useTranslation();
  const { can } = useAuth();
  if (!covered) return null;
  const number = can(P.contractsView) ? <Anchor component={Link} to={`/contracts/${covered.id}`} fz="sm" fw={600}>{covered.number}</Anchor> : <Text span fz="sm" fw={600}>{covered.number}</Text>;
  return (
    <Alert variant="light" color="indigo" icon={<IconFileCertificate size={18} />} py="xs">
      <Text fz="sm">{t('contracts.coveredBy')} {number} · {covered.companyName}</Text>
    </Alert>
  );
}
