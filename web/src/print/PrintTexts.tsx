import { Text, Title } from '@mantine/core';
import type { Terminology } from '@/api/types';

/** What every printed paper needs besides the record and its layout (passed in, so the bodies render anywhere, the editor's preview included). */
export type PrintContext = { orgName: string; terms: Terminology; logoUrl?: string | null; language: string };

/** A layout's plain text: shown as typed, line breaks kept, never read as HTML. */
export function PlainText({ text, size = 'sm' }: { text: string; size?: 'xs' | 'sm' }) {
  return text ? <Text fz={size} style={{ whiteSpace: 'pre-wrap' }}>{text}</Text> : null;
}

/** The terms block: a title and the layout's terms in the page's language. */
export function TermsBlock({ title, text }: { title: string; text: string }) {
  if (!text) return null;
  return <div style={{ marginBottom: 16 }}><Title order={5} mb={6}>{title}</Title><PlainText text={text} /></div>;
}

/** The layout's footer, at the end of the paper. */
export function FooterText({ text }: { text: string }) {
  return text ? <Text fz="xs" c="dimmed" mt="xl" style={{ whiteSpace: 'pre-wrap', borderTop: '1px solid #ccc', paddingTop: 6 }}>{text}</Text> : null;
}

/** The printed page's size: A4 (as today), A5 with tighter spacing, or US Letter. */
export function PageSize({ size }: { size?: string | null }) {
  if (!size || size === 'A4') return null;
  const css = size === 'A5'
    ? '@page { size: A5; margin: 8mm; } @media print { .print-doc { zoom: 0.78; } }'
    : '@page { size: letter; }';
  return <style>{css}</style>;
}
