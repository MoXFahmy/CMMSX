import { Select, type SelectProps } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { get } from '@/api/client';
import type { Lookup } from '@/api/types';
import { codeOptions, type CodePairs } from './codes';
import { pick } from './bilingual';

type Code = { id: string; code: string; name: string };

/**
 * A symptom, cause (failure code) or remedy picker (0.14.0). It offers the active codes plus the current one (kept even when made
 * inactive). With `pairing`, the codes most often recorded with `pairedWith` (the chosen symptom, or cause) come first.
 */
export function CodeSelect({ path, label, value, onChange, current, pairing, pairedWith, ...rest }: {
  path: '/symptoms' | '/failure-codes' | '/remedies'; label: string; value: string | null; onChange: (value: string | null) => void;
  /** The record's code as the detail gives it: shown before the list loads. */
  current?: Code | null;
  pairing?: keyof CodePairs; pairedWith?: string | null;
} & Omit<SelectProps, 'data' | 'value' | 'onChange' | 'label'>) {
  const { t } = useTranslation();
  const { data: codes } = useQuery({ queryKey: ['codes', path], queryFn: () => get<Lookup[]>(path) });
  const { data: pairs } = useQuery({ queryKey: ['code-pairs'], queryFn: () => get<CodePairs>('/code-pairs'), enabled: !!pairing, staleTime: 5 * 60_000, retry: false });
  // Names in the reader's language (0.28.0); the list itself is the raw one Settings edits.
  const offered: Code[] = (codes ?? []).filter((c) => c.isActive || c.id === value).map((c) => ({ ...c, name: pick(c.name, c.nameAr) }));
  if (current && !offered.some((c) => c.id === current.id)) offered.push(current);
  return <Select label={label} clearable searchable value={value} onChange={onChange} data={codeOptions(offered, pairing ? pairs?.[pairing] ?? [] : [], pairedWith, t)} {...rest} />;
}
