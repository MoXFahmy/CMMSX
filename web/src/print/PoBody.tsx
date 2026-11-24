import { Fragment, type ReactNode } from 'react';
import { Badge, Card, Group, Stack, Table, Text, Title } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import type { PoDetail } from '@/api/types';
import { fmtDate, fmtPrice, fmtQty } from '@/components/utils';
import { printsSignatures } from '@/pages/purchasing/approvals';
import { blocksInOrder, layoutText, pairRows, poUnitPrice, shows, type PrintBlock, type PrintLayout } from './layouts';
import { FooterText, PlainText, TermsBlock, type PrintContext } from './PrintTexts';

/** The purchase order's content (0.23.0): today's paper in blocks, in the layout's order, with its fields and columns. */
export function PoBody({ o, layout, orgName, logoUrl, language }: PrintContext & { o: PoDetail; layout: PrintLayout }) {
  const { t } = useTranslation();
  const has = (block: string, field?: string) => shows(layout, block, field);
  const discountShown = has('lines', 'discount');

  const supplier = (
    <Card withBorder><Text fz="xs" c="dimmed" tt="uppercase" fw={600}>{t('po.supplier')}</Text>
      {has('supplier', 'name') && <Text fw={600}>{o.supplier.name}</Text>}
      <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{[
        has('supplier', 'contact') && o.supplier.contactName,
        has('supplier', 'address') && o.supplier.address,
        has('supplier', 'address') && [o.supplier.city, o.supplier.country].filter(Boolean).join(', '),
        has('supplier', 'email') && o.supplier.email,
        has('supplier', 'phone') && o.supplier.phone,
        has('supplier', 'taxNumber') && o.supplier.taxNumber && `${t('po.taxNumber')}: ${o.supplier.taxNumber}`,
      ].filter(Boolean).join('\n')}</Text></Card>
  );
  const detailRows: [string, string, string | null | undefined][] = [
    ['paymentTerms', t('po.paymentTerms'), o.paymentTerms], ['quoteRef', t('po.quoteRef'), o.supplierQuoteRef],
    ['shippingMethod', t('po.shippingMethod'), o.shippingMethod], ['requestedBy', t('po.requestedBy'), o.requestedBy],
    ['currency', t('po.currency'), o.currency], ['orderType', t('print.orderType'), o.orderType?.name],
  ];
  const details = (
    <Card withBorder><Stack gap={4}>
      {detailRows.filter(([k]) => has('details', k)).map(([k, label, value]) => <Text key={k} fz="sm"><b>{label}:</b> {value ?? '—'}</Text>)}
    </Stack></Card>
  );

  type Column = { key: string; title: string; right?: boolean; cell: (l: PoDetail['lines'][number]) => ReactNode };
  const allColumns: Column[] = [
    { key: 'lineNumber', title: '#', cell: (l) => l.lineNumber },
    { key: 'partNumber', title: t('inv.partNumber'), cell: (l) => l.partNumber ?? '' },
    { key: 'description', title: t('common.description'), cell: (l) => <>{l.description}{has('lines', 'supplierPartNumber') && l.supplierPartNumber && <Text fz="xs">{t('catalogue.yourNumber', { number: l.supplierPartNumber })}</Text>}</> },
    { key: 'quantity', title: t('inv.quantity'), right: true, cell: (l) => fmtQty(l.quantity) },
    { key: 'unit', title: t('inv.unit'), cell: (l) => l.unitOfMeasure },
    { key: 'unitPrice', title: t('po.unitPrice'), right: true, cell: (l) => fmtPrice(poUnitPrice(l, discountShown), o.currency) },
    { key: 'discount', title: t('po.discount'), right: true, cell: (l) => (l.discountPercent ? `${l.discountPercent}%` : '') },
    { key: 'lineTotal', title: t('po.lineTotal'), right: true, cell: (l) => fmtPrice(l.lineTotal, o.currency) },
  ];
  const columns = allColumns.filter((c) => has('lines', c.key));

  const render = (b: PrintBlock): ReactNode => {
    switch (b.key) {
      case 'supplier': return <div style={{ marginBottom: 20 }}>{supplier}</div>;
      case 'details': return <div style={{ marginBottom: 20 }}>{details}</div>;
      case 'lines': return (
        <Table withTableBorder withColumnBorders verticalSpacing={6}>
          <Table.Thead><Table.Tr>{columns.map((c) => <Table.Th key={c.key} w={c.key === 'lineNumber' ? 36 : undefined} ta={c.right ? 'right' : undefined}>{c.title}</Table.Th>)}</Table.Tr></Table.Thead>
          <Table.Tbody>{o.lines.map((l) => <Table.Tr key={l.id}>{columns.map((c) => <Table.Td key={c.key} ta={c.right ? 'right' : undefined}>{c.cell(l)}</Table.Td>)}</Table.Tr>)}</Table.Tbody>
        </Table>
      );
      case 'totals': return (
        <Group justify="flex-end" mt="md"><Stack gap={2} w={280}>
          {has('totals', 'subtotal') && <Group justify="space-between"><Text fz="sm">{t('po.subtotal')}</Text><Text fz="sm">{fmtPrice(o.totals.subtotal, o.currency)}</Text></Group>}
          {has('totals', 'tax') && <Group justify="space-between"><Text fz="sm">{t('po.tax')} ({o.taxPercent}%)</Text><Text fz="sm">{fmtPrice(o.totals.tax, o.currency)}</Text></Group>}
          {has('totals', 'shipping') && <Group justify="space-between"><Text fz="sm">{t('po.shipping')}</Text><Text fz="sm">{fmtPrice(o.totals.shippingCost, o.currency)}</Text></Group>}
          <Group justify="space-between" style={{ borderTop: '2px solid #000' }} pt={4}><Text fw={700}>{t('po.total')}</Text><Text fw={700}>{fmtPrice(o.totals.total, o.currency)}</Text></Group>
        </Stack></Group>
      );
      case 'notes': return o.notes && <Text mt="lg" fz="sm" style={{ whiteSpace: 'pre-wrap' }}><b>{t('common.notes')}:</b> {o.notes}</Text>;
      case 'approvals': return (
        <>
          {o.approval && printsSignatures(o.approval) && (
            <Table mt="lg" fz="xs" withTableBorder>
              <Table.Thead><Table.Tr><Table.Th>{t('approvals.level')}</Table.Th><Table.Th>{t('approvals.signedBy')}</Table.Th><Table.Th>{t('approvals.date')}</Table.Th><Table.Th>{t('approvals.comment')}</Table.Th></Table.Tr></Table.Thead>
              <Table.Tbody>{o.approval.steps.flatMap((s) => s.approvers.filter((x) => x.signed).map((x) => (
                <Table.Tr key={`${s.step}-${x.id}`}><Table.Td>{s.name}</Table.Td><Table.Td>{x.name}{x.approved === false ? ` (${t('approvals.returned')})` : ''}</Table.Td>
                  <Table.Td>{fmtDate(x.at ?? undefined)}</Table.Td><Table.Td>{x.comment}</Table.Td></Table.Tr>)))}</Table.Tbody>
            </Table>
          )}
          <Group mt={60} mb="md" grow>
            {!printsSignatures(o.approval) && <Stack gap={2}><div style={{ borderTop: '1px solid #000' }} /><Text fz="xs">{t('po.approvedBy')}{o.approvedByName ? `: ${o.approvedByName} · ${fmtDate(o.approvedAt)}` : ''}</Text></Stack>}
            <Stack gap={2}><div style={{ borderTop: '1px solid #000' }} /><Text fz="xs">{t('po.supplierAcceptance')}</Text></Stack>
          </Group>
        </>
      );
      case 'terms': return <div style={{ marginTop: 16 }}><TermsBlock title={t('print.terms')} text={layoutText(layout, 'terms', language)} /></div>;
      default: return null;
    }
  };

  const rest = blocksInOrder(layout).filter((b) => b.key !== 'header');
  const headerText = layoutText(layout, 'header', language);
  return (
    <>
      {has('header') && (
        <Group justify="space-between" align="flex-start" mb="xl">
          <Group gap="md" wrap="nowrap" align="flex-start">
            {layout.showLogo && logoUrl && <img src={logoUrl} alt={orgName} style={{ height: 56, maxWidth: 224, objectFit: 'contain' }} />}
            <Stack gap={2}>{has('header', 'orgName') && <Title order={2}>{orgName}</Title>}{has('header', 'site') && <Text c="dimmed">{o.siteName}</Text>}
              {has('header', 'deliveryAddress') && o.deliverTo && <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{t('po.deliveryAddress')}: {o.deliverTo}</Text>}</Stack>
          </Group>
          <Stack gap={2} align="flex-end">
            {has('header', 'title') && <><Title order={3}>{t('po.printTitle')}</Title><Text fw={700} fz="lg">{o.number}</Text></>}
            {has('header', 'orderDate') && <Text fz="sm">{t('po.orderDate')}: {fmtDate(o.orderDate)}</Text>}
            {has('header', 'expectedDate') && o.expectedDate && <Text fz="sm">{t('po.expected')}: {fmtDate(o.expectedDate)}</Text>}
            {o.status === 'Draft' || o.status === 'Submitted' ? <Badge color="orange" variant="outline">{t(`poStatus.${o.status}`)} — {t('po.notApproved')}</Badge> : null}
          </Stack>
        </Group>
      )}
      {!has('header', 'title') && <Title order={4} mb="sm">{o.number}</Title>}
      {!has('header') && (o.status === 'Draft' || o.status === 'Submitted') && <Badge color="orange" variant="outline" mb="sm">{t(`poStatus.${o.status}`)} — {t('po.notApproved')}</Badge>}
      {headerText && <div style={{ marginBottom: 12 }}><PlainText text={headerText} /></div>}
      {pairRows(rest, [['supplier', 'details']]).map((row) => row.length === 2
        ? <Group key={row.map((x) => x.key).join('+')} align="flex-start" grow mb="lg">{row[0].key === 'supplier' ? <>{supplier}{details}</> : <>{details}{supplier}</>}</Group>
        : <Fragment key={row[0].key}>{render(row[0])}</Fragment>)}
      <FooterText text={layoutText(layout, 'footer', language)} />
    </>
  );
}
