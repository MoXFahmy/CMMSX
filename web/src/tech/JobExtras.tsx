import { useRef, useState } from 'react';
import { Anchor, Button, Card, Group, Modal, Select, Stack, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateTimePicker } from '@mantine/dates';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconChevronLeft, IconFileUpload, IconHandStop, IconListCheck, IconPlayerPlay, IconX } from '@tabler/icons-react';
import { dateTimeFormat, fmtDateTime, notifyError, notifySuccess } from '@/components/utils';
import { FILE_ACCEPT, fileKind, sizeProblem } from './files';
import { useTech } from './useTech';
import type { TechJob } from './types';
import { listItems } from '@/components/listLabels';
import { placeFilter } from '@/components/bilingualSearch';

/** A picker's local "YYYY-MM-DD HH:mm:ss" (or Date) as an ISO instant. */
const iso = (v: string | Date | null) => (v ? new Date(typeof v === 'string' ? v.replace(' ', 'T') : v).toISOString() : new Date().toISOString());

/** The asset's stop from the job (0.11.0): stopped / back running, with an editable time, offline too. */
export function StopCard({ job }: { job: TechJob }) {
  const { t } = useTranslation();
  const { add } = useTech();
  const [action, setAction] = useState<'downtime.start' | 'downtime.end' | null>(null);
  const [when, setWhen] = useState<string | Date | null>(new Date());
  if (!job.asset) return null;
  const down = job.asset.downSince ?? (job.downtime && !job.downtime.endedAt ? job.downtime.startedAt : null);
  const send = async () => {
    try { await add({ type: action!, workOrderId: job.id, payload: { assetId: job.asset!.id, at: iso(when) } }); notifySuccess(t('techStock.saved')); }
    catch (e) { notifyError(e); }
    setAction(null);
  };
  return (
    <Card><Stack gap="xs">
      <Group justify="space-between">
        <Text fz="sm">{down ? t('techJob.downSince', { when: fmtDateTime(down) }) : t('techJob.running')}</Text>
        {down
          ? <Button size="xs" color="teal" leftSection={<IconPlayerPlay size={14} />} onClick={() => { setWhen(new Date()); setAction('downtime.end'); }}>{t('techJob.backRunning')}</Button>
          : <Button size="xs" color="red" variant="light" leftSection={<IconHandStop size={14} />} onClick={() => { setWhen(new Date()); setAction('downtime.start'); }}>{t('techJob.assetStopped')}</Button>}
      </Group>
      <Modal opened={!!action} onClose={() => setAction(null)} title={action === 'downtime.end' ? t('techJob.backRunning') : t('techJob.assetStopped')}>
        <Stack>
          <DateTimePicker label={t('techJob.when')} valueFormat={dateTimeFormat()} value={when ? new Date(when) : null} onChange={setWhen} maxDate={new Date()} />
          <Button onClick={send}>{t('common.save')}</Button>
        </Stack>
      </Modal>
    </Stack></Card>
  );
}

/** Add a PM task's checklist, attach a video or document, cancel the job (0.11.0). */
export function JobActions({ job }: { job: TechJob }) {
  const { t } = useTranslation();
  const { stock, bundle, add } = useTech();
  const fileInput = useRef<HTMLInputElement>(null);
  const [checklist, setChecklist] = useState(false);
  const [task, setTask] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const [reason, setReason] = useState<string | null>(null);
  const [note, setNote] = useState('');
  const send = async (type: string, payload: unknown, file?: { blob: Blob; fileName: string }) => {
    try { await add({ type, workOrderId: job.id, payload }, file); notifySuccess(t('techStock.saved')); } catch (e) { notifyError(e); }
  };
  const attach = async (files: FileList | null) => {
    for (const f of Array.from(files ?? [])) {
      const kind = fileKind(f.name, f.type);
      if (kind !== 'file') { notifyError(new Error(t('techJob.notAFile', { name: f.name }))); continue; }
      const over = sizeProblem(kind, f.size);
      if (over) { notifyError(new Error(t('techJob.tooBig', { name: f.name, mb: over }))); continue; }
      await send('photo.add', { kind: 'file', fileName: f.name }, { blob: f, fileName: f.name });
    }
    if (fileInput.current) fileInput.current.value = '';
  };
  const pmTasks = stock?.pmTasks ?? [];
  return (
    <Group gap="xs">
      {pmTasks.length > 0 && <Button size="xs" variant="light" leftSection={<IconListCheck size={14} />} onClick={() => setChecklist(true)}>{t('techJob.addChecklist')}</Button>}
      <Button size="xs" variant="light" leftSection={<IconFileUpload size={14} />} onClick={() => fileInput.current?.click()}>{t('techJob.attachFile')}</Button>
      <input ref={fileInput} type="file" accept={FILE_ACCEPT} hidden multiple onChange={(e) => void attach(e.currentTarget.files)} />
      {bundle?.can?.cancel && <Button size="xs" variant="subtle" color="red" leftSection={<IconX size={14} />} onClick={() => setCancelling(true)}>{t('techJob.cancelJob')}</Button>}
      <Modal opened={checklist} onClose={() => setChecklist(false)} title={t('techJob.addChecklist')}>
        <Stack>
          <Select label={t('techJob.pmTask')} searchable data={pmTasks.map((p) => ({ value: p.id, label: `${p.code} · ${p.name}` }))} value={task} onChange={setTask}
            filter={placeFilter(new Map(pmTasks.map((p) => [p.id, p.otherName])))} />
          {task && <Stack gap={2}>{pmTasks.find((p) => p.id === task)?.steps.map((s, i) => <Text key={i} fz="sm">{i + 1}. {s}</Text>)}</Stack>}
          <Button disabled={!task} onClick={() => { void send('tasks.fromPm', { pmTaskId: task }); setChecklist(false); setTask(null); }}>{t('techJob.addSteps')}</Button>
        </Stack>
      </Modal>
      <Modal opened={cancelling} onClose={() => setCancelling(false)} title={t('techJob.cancelJob')}>
        <Stack>
          {(stock?.cancelReasons.length ?? 0) > 0 && <Select label={t('wo.cancelReason')} required data={stock!.cancelReasons.map((r) => ({ value: r.id, label: r.name }))} value={reason} onChange={setReason} />}
          <Textarea label={t('common.notes')} autosize value={note} onChange={(e) => setNote(e.currentTarget.value)} />
          <Button color="red" disabled={(stock?.cancelReasons.length ?? 0) > 0 && !reason} onClick={() => {
            void send('status.change', { status: 'Cancelled', cancelReasonId: reason, note: note.trim() || null }); setCancelling(false);
          }}>{t('techJob.cancelJob')}</Button>
        </Stack>
      </Modal>
    </Group>
  );
}

/** A work order raised on the phone (0.11.0), for people who may create them; offline too, assigned to me. */
export function NewWorkOrderPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { places, bundle, add } = useTech();
  const [symptom, setSymptom] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState<string | null>('Medium');
  const [asset, setAsset] = useState<string | null>(null);
  const [location, setLocation] = useState<string | null>(null);
  const site = asset ? places?.assets.find((a) => a.id === asset)?.siteId : location ? places?.locations.find((l) => l.id === location)?.siteId : undefined;
  const save = async () => {
    try {
      await add({ type: 'workorder.create', payload: { title: title.trim(), description: description.trim() || null, priority, type: 'Corrective', siteId: site, assetId: asset, locationId: location, ...(symptom ? { symptomId: symptom } : {}) } });
      notifySuccess(t('techJob.workOrderSaved')); navigate('/tech');
    } catch (e) { notifyError(e); }
  };
  return (
    <Stack>
      <Anchor fz="sm" onClick={() => navigate('/tech')}><Group gap={2}><IconChevronLeft size={14} className="mirror-rtl" />{t('tech.myJobs')}</Group></Anchor>
      <Title order={4}>{t('techJob.newWorkOrder')}</Title>
      <TextInput label={t('techJob.whatToDo')} required value={title} onChange={(e) => setTitle(e.currentTarget.value)} data-autofocus />
      <Textarea label={t('common.description')} autosize value={description} onChange={(e) => setDescription(e.currentTarget.value)} />
      <Select label={t('priority.label')} data={listItems('priority', t, priority)} value={priority} onChange={setPriority} />
      {(bundle?.symptoms ?? []).length > 0 && <Select label={t('codes.symptom')} clearable searchable
        data={(bundle?.symptoms ?? []).map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` }))} value={symptom} onChange={setSymptom} />}
      <Select label={t('wo.asset')} searchable clearable filter={placeFilter(new Map((places?.assets ?? []).map((a) => [a.id, a.otherName])))}
        data={(places?.assets ?? []).map((a) => ({ value: a.id, label: `${a.assetNumber} · ${a.name}` }))} value={asset} onChange={(v) => { setAsset(v); if (v) setLocation(null); }} />
      {!asset && <Select label={t('wo.location')} searchable clearable filter={placeFilter(new Map((places?.locations ?? []).map((l) => [l.id, l.otherName])))}
        data={(places?.locations ?? []).map((l) => ({ value: l.id, label: l.path ?? l.name }))} value={location} onChange={setLocation} />}
      <Button disabled={!title.trim() || !site} onClick={save}>{t('techJob.createWorkOrder')}</Button>
    </Stack>
  );
}
