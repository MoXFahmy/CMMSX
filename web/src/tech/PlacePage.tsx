import { useState } from 'react';
import { Alert, Anchor, Button, Card, Checkbox, Group, Modal, NumberInput, Select, Stack, Text, TextInput, Textarea, Title } from '@mantine/core';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconChevronLeft, IconGauge, IconMessageReport } from '@tabler/icons-react';
import type { WorkPriority } from '@/api/types';
import { WoStatusBadge } from '@/components/WorkBadges';
import { fmtDateTime, fmtQty } from '@/components/utils';
import { useTech } from './useTech';

const PRIORITIES: WorkPriority[] = ['Low', 'Medium', 'High', 'Urgent'];

/** An asset or location opened by a scan: its open jobs, its meters, and raising a request — all from the phone's copy. */
export function PlacePage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { kind = 'asset', id = '' } = useParams();
  const { places, bundle, add, online } = useTech();
  const [meter, setMeter] = useState<string | null>(null);
  const [requestOpen, setRequestOpen] = useState(false);
  const asset = kind === 'asset' ? places?.assets.find((a) => a.id === id) : undefined;
  const location = kind === 'location' ? places?.locations.find((l) => l.id === id) : places?.locations.find((l) => l.id === asset?.locationId);
  if (!asset && !(kind === 'location' && location)) {
    return (
      <Stack>
        <Anchor onClick={() => navigate('/tech/scan')}>{t('tech.back')}</Anchor>
        <Text c="dimmed">{t('tech.notOnPhone')}</Text>
        {online && <Anchor href={kind === 'asset' ? `/assets/${id}` : '/locations'}>{t('tech.openInFullApp')}</Anchor>}
      </Stack>
    );
  }
  const jobs = (bundle?.jobs ?? []).filter((j) => kind === 'asset'
    ? j.asset?.id === id || j.roundAssets.some((a) => a.id === id)
    : j.location?.id === id);
  const meters = asset ? (places?.meters ?? []).filter((m) => m.assetId === asset.id) : [];
  const siteId = asset?.siteId ?? location!.siteId;

  return (
    <Stack gap="sm">
      <Anchor fz="sm" onClick={() => navigate('/tech/scan')}><Group gap={2}><IconChevronLeft size={14} className="mirror-rtl" />{t('tech.scan')}</Group></Anchor>
      <Card withBorder p="sm">
        <Text fz="xs" c="dimmed">{asset ? asset.assetNumber : t('tech.location')}</Text>
        <Title order={4}>{asset ? asset.name : location!.name}</Title>
        {location?.path && <Text fz="xs" c="dimmed">{location.path}</Text>}
      </Card>
      {(asset?.notesToTechnician || location?.notesToTechnician) && (
        <Alert color="yellow" title={t('tech.notesToTech')}>{[asset?.notesToTechnician, location?.notesToTechnician].filter(Boolean).join('\n\n')}</Alert>
      )}
      <Card withBorder p="sm">
        <Text fw={600} mb={6}>{t('tech.openJobsHere')} · {jobs.length}</Text>
        {jobs.length === 0 && <Text fz="sm" c="dimmed">{t('tech.noJobsHere')}</Text>}
        {jobs.map((j) => (
          <Group key={j.id} justify="space-between" py={4} style={{ cursor: 'pointer' }} onClick={() => navigate(`/tech/jobs/${j.id}`)}>
            <Text fz="sm">{j.number} · {j.title}</Text><WoStatusBadge status={j.status} />
          </Group>
        ))}
      </Card>
      {meters.length > 0 && (
        <Card withBorder p="sm">
          <Text fw={600} mb={6}>{t('tech.meters')}</Text>
          {meters.map((m) => (
            <Group key={m.id} justify="space-between" py={4}>
              <div><Text fz="sm">{m.name}</Text><Text fz="xs" c="dimmed">{m.currentReading != null ? `${fmtQty(m.currentReading)} ${m.unit} · ${fmtDateTime(m.lastReadAt)}` : t('tech.noReading')}</Text></div>
              <Button size="compact-xs" variant="light" leftSection={<IconGauge size={12} />} onClick={() => setMeter(m.id)}>{t('tech.enterReading')}</Button>
            </Group>
          ))}
        </Card>
      )}
      <Button variant="light" leftSection={<IconMessageReport size={16} />} onClick={() => setRequestOpen(true)}>{t('tech.raiseRequest')}</Button>

      <ReadingModal meter={meters.find((m) => m.id === meter)} onClose={() => setMeter(null)}
        onSave={(p) => void add({ type: 'reading.add', payload: { meterId: meter, ...p } })} />
      <RequestModal opened={requestOpen} onClose={() => setRequestOpen(false)}
        onSave={(p) => void add({ type: 'request.create', payload: { ...p, siteId, assetId: asset?.id ?? null, locationId: location?.id ?? null } })} />
    </Stack>
  );
}

function ReadingModal({ meter, onClose, onSave }: { meter?: { name: string; unit: string }; onClose: () => void; onSave: (p: { value: number; note: string | null; confirm: boolean }) => void }) {
  const { t } = useTranslation();
  const [value, setValue] = useState<number | string>('');
  const [note, setNote] = useState('');
  const [confirm, setConfirm] = useState(false);
  return (
    <Modal opened={!!meter} onClose={onClose} title={meter ? `${t('tech.enterReading')} · ${meter.name}` : ''}>
      <Stack>
        <NumberInput label={`${t('tech.reading')} (${meter?.unit ?? ''})`} decimalScale={3} value={value} onChange={setValue} data-autofocus />
        <TextInput label={t('common.notes')} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
        <Checkbox label={t('tech.meterReset')} checked={confirm} onChange={(e) => setConfirm(e.currentTarget.checked)} />
        <Button disabled={value === ''} onClick={() => { onSave({ value: Number(value), note: note.trim() || null, confirm }); setValue(''); setNote(''); setConfirm(false); onClose(); }}>{t('common.save')}</Button>
      </Stack>
    </Modal>
  );
}

function RequestModal({ opened, onClose, onSave }: { opened: boolean; onClose: () => void; onSave: (p: { title: string; description: string | null; priority: WorkPriority }) => void }) {
  const { t } = useTranslation();
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState<WorkPriority>('Medium');
  return (
    <Modal opened={opened} onClose={onClose} title={t('tech.raiseRequest')}>
      <Stack>
        <TextInput label={t('tech.whatsWrong')} required maxLength={300} value={title} onChange={(e) => setTitle(e.currentTarget.value)} data-autofocus />
        <Textarea label={t('common.description')} autosize minRows={2} value={description} onChange={(e) => setDescription(e.currentTarget.value)} />
        <Select label={t('priority.label')} data={PRIORITIES.map((p) => ({ value: p, label: t(`priority.${p}`) }))} value={priority} onChange={(v) => setPriority((v as WorkPriority) ?? 'Medium')} />
        <Button disabled={!title.trim()} onClick={() => { onSave({ title: title.trim(), description: description.trim() || null, priority }); setTitle(''); setDescription(''); onClose(); }}>{t('tech.send')}</Button>
      </Stack>
    </Modal>
  );
}
