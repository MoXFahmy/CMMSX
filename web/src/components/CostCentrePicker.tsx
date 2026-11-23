import { useMemo } from 'react';
import { Select, type SelectProps } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { CostCentre } from '@/api/types';
import { costCentreOptions } from './costCentres';

export function useCostCentres() {
  return useQuery({ queryKey: ['cost-centres'], queryFn: () => get<CostCentre[]>('/cost-centres'), staleTime: 60_000 });
}

type Props = Omit<SelectProps, 'data' | 'value' | 'onChange'> & {
  value: string | null;
  onChange: (id: string | null) => void;
  /** Hide cost centres that belong to another site. */
  siteId?: string | null;
};

/** 0.20.0: choose a cost centre from the tree ("Production › Line 1"); inactive ones only when already chosen. */
export function CostCentrePicker({ value, onChange, siteId, label, ...rest }: Props) {
  const { t } = useTranslation();
  const { data } = useCostCentres();
  const options = useMemo(() => {
    const list = (data ?? []).filter((c) => !siteId || !c.siteId || c.siteId === siteId || c.id === value);
    return costCentreOptions(list, value);
  }, [data, siteId, value]);
  return (
    <Select label={label ?? t('costCentres.one')} data={options} value={value} onChange={onChange} searchable clearable
      nothingFoundMessage={t('common.noRecords')} comboboxProps={{ width: 'max-content', position: 'bottom-start' }} {...rest} />
  );
}
