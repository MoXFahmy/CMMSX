import { useMemo, useRef, useState } from 'react';
import { AccessWindowsList, StoppedBadge } from '@/components/AccessWindowsEditor';
import { Alert, Anchor, Badge, Button, Card, Checkbox, Divider, Group, Modal, NumberInput, Select, Stack, Switch, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { IconCamera, IconPhoto, IconCheck, IconChevronLeft, IconClock, IconPlayerPause, IconPlayerPlay } from '@tabler/icons-react';
import { HOLD_REASONS, type HoldReason } from '@/api/types';
import { PriorityBadge, WoStatusBadge } from '@/components/WorkBadges';
import { fmtDate, fmtDateTime, hoursText, notifyError } from '@/components/utils';
import { downFor } from '@/components/downtime';
import { withQueued } from './overlay';
import { shrink } from './photos';
import { dueLabel } from './due';
import { alreadySigned, needsLabour, staleCompletions } from './complete';
import { SignaturePad, type SignatureHandle } from './SignaturePad';
import { useTech } from './useTech';
import { JobParts } from './StockPages';
import { JobActions, StopCard } from './JobExtras';
import type { TechJob } from './types';

/** One job: checklist, hours, photos, comments, and start / hold / complete — all of it works without signal. */
export function JobPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const { bundle, queue, sent, add } = useTech();
  const base = bundle?.jobs.find((j) => j.id === id);
  // Sent changes first (they are older), then those still waiting.
  const job = useMemo(() => (base ? withQueued(base, [...sent, ...queue]) : undefined), [base, sent, queue]);
  const [hoursOpen, setHoursOpen] = useState(false);
  const [holdOpen, setHoldOpen] = useState(false);
  const [completeOpen, setCompleteOpen] = useState(false);
  const [comment, setComment] = useState('');
  const photoInput = useRef<HTMLInputElement>(null);
  const galleryInput = useRef<HTMLInputElement>(null);

  if (!job) return <Stack><Anchor onClick={() => navigate('/tech')}>{t('tech.back')}</Anchor><Text c="dimmed">{t('tech.jobGone')}</Text></Stack>;
  const change = (type: string, payload: unknown) => add({ type, workOrderId: job.id, payload });
  const canStart = job.status === 'Open' || job.status === 'OnHold';
  const canHold = job.status === 'Open' || job.status === 'InProgress';
  const canComplete = job.status === 'Open' || job.status === 'InProgress' || job.status === 'OnHold';
  const groups = job.roundAssets.length > 0
    ? job.roundAssets.map((a) => ({ key: a.id, title: `${a.assetNumber} · ${a.name}` as string | null, tasks: job.tasks.filter((x) => x.assetId === a.id) }))
      .concat([{ key: 'other', title: null, tasks: job.tasks.filter((x) => !job.roundAssets.some((a) => a.id === x.assetId)) }]).filter((g) => g.tasks.length > 0)
    : [{ key: 'all', title: null as string | null, tasks: job.tasks }];

  const addPhoto = async (files: FileList | null) => {
    for (const f of Array.from(files ?? [])) {
      try { await add({ type: 'photo.add', workOrderId: job.id, payload: { kind: 'photo' } }, { blob: await shrink(f), fileName: f.name.replace(/\.\w+$/, '') + '.jpg' }); }
      catch (e) { notifyError(e); }
    }
    for (const input of [photoInput.current, galleryInput.current]) if (input) input.value = '';
  };

  return (
    <Stack gap="sm">
      <Anchor fz="sm" onClick={() => navigate('/tech')}><Group gap={2}><IconChevronLeft size={14} className="mirror-rtl" />{t('tech.myJobs')}</Group></Anchor>
      <Card withBorder p="sm">
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <div style={{ minWidth: 0 }}>
            <Text fz="xs" c="dimmed">{job.number}{dueLabel(job.dueDate, job.dueAt) ? ` · ${t('tech.due', { date: dueLabel(job.dueDate, job.dueAt) })}` : ''}</Text>
            <Title order={4}>{job.title}</Title>
          </div>
          <Stack gap={4} align="flex-end"><PriorityBadge value={job.priority} /><WoStatusBadge status={job.status} /></Stack>
        </Group>
        {job.asset && <Text fz="sm" mt={6}>{job.asset.assetNumber} · {job.asset.name}</Text>}
        {job.location?.path && <Text fz="xs" c="dimmed">{job.location.path}</Text>}
        {job.teamName && <Text fz="xs" c="dimmed">{t('tech.team', { team: job.teamName })}</Text>}
        {job.description && <Text fz="sm" mt={6} style={{ whiteSpace: 'pre-wrap' }}>{job.description}</Text>}
      </Card>
      {job.asset?.downSince && (
        <Alert color="red" title={t('downtime.downSince', { when: fmtDateTime(job.asset.downSince), for: downFor(job.asset.downSince, new Date()) })} />
      )}
      {(job.onlyWhenStopped || (job.accessWindows?.length ?? 0) > 0) && (
        <Card><Stack gap={6}><StoppedBadge show={job.onlyWhenStopped} /><AccessWindowsList windows={job.accessWindows} /></Stack></Card>
      )}
      {(job.asset?.notesToTechnician || job.location?.notesToTechnician) && (
        <Alert color="yellow" title={t('tech.notesToTech')}>{[job.asset?.notesToTechnician, job.location?.notesToTechnician].filter(Boolean).join('\n\n')}</Alert>
      )}

      <Group grow>
        {canStart && <Button leftSection={<IconPlayerPlay size={16} />} onClick={() => void change('status.change', { status: 'InProgress' })}>{t('tech.start')}</Button>}
        {canHold && <Button variant="default" leftSection={<IconPlayerPause size={16} />} onClick={() => setHoldOpen(true)}>{t('tech.hold')}</Button>}
        {canComplete && <Button color="teal" leftSection={<IconCheck size={16} />} onClick={() => setCompleteOpen(true)}>{t('tech.complete')}</Button>}
      </Group>

      {job.tasks.length > 0 && (
        <Card withBorder p="sm">
          <Text fw={600} mb={6}>{t('tech.checklist')} · {job.tasks.filter((x) => x.isDone).length}/{job.tasks.length}</Text>
          {groups.map((g) => (
            <Stack key={g.key} gap={8}>
              {g.title && <Text fz="xs" fw={700} c="dimmed" mt={6}>{g.title}</Text>}
              {g.tasks.map((s) => <Step key={s.id} step={s} onChange={(p) => void change('task.update', { taskId: s.id, ...p })} />)}
            </Stack>
          ))}
        </Card>
      )}

      <StopCard job={job} />
      <JobParts job={job} />
      <JobActions job={job} />

      <Card withBorder p="sm">
        <Group justify="space-between" mb={6}>
          <Text fw={600}>{t('tech.myHours')} · {hoursText(job.myLabor.reduce((a, l) => a + l.hours + l.overtimeHours, 0))}</Text>
          <Button size="compact-xs" variant="light" leftSection={<IconClock size={12} />} onClick={() => setHoursOpen(true)}>{t('tech.logHours')}</Button>
        </Group>
        {job.myLabor.map((l, i) => <Text key={i} fz="sm">{fmtDate(l.workDate)} · {hoursText(l.hours)}{l.overtimeHours ? ` + ${hoursText(l.overtimeHours)} ${t('unit.overtime')}` : ''}{l.notes ? ` · ${l.notes}` : ''}</Text>)}
      </Card>

      <Card withBorder p="sm">
        <Group justify="space-between" mb={6}>
          <Text fw={600}>{t('tech.photos')}</Text>
          <Group gap={6}>
            {/* The camera, or photos already on the phone (capture alone opens only the camera on Android). */}
            <Button size="compact-xs" variant="light" leftSection={<IconCamera size={12} />} onClick={() => photoInput.current?.click()}>{t('tech.takePhoto')}</Button>
            <Button size="compact-xs" variant="default" leftSection={<IconPhoto size={12} />} onClick={() => galleryInput.current?.click()}>{t('tech.pickPhoto')}</Button>
          </Group>
          <input ref={photoInput} type="file" accept="image/*" capture="environment" hidden onChange={(e) => void addPhoto(e.currentTarget.files)} />
          <input ref={galleryInput} type="file" accept="image/*" multiple hidden onChange={(e) => void addPhoto(e.currentTarget.files)} />
        </Group>
        {job.files.map((f) => <Text key={f.id} fz="sm">{f.isSignature ? `✍ ${f.signedBy ?? ''}` : f.fileName}</Text>)}
        {job.pendingPhotos + job.pendingSignatures > 0 && <Badge color="orange" variant="light">{t('tech.photosWaiting', { count: job.pendingPhotos + job.pendingSignatures })}</Badge>}
      </Card>

      <Card withBorder p="sm">
        <Text fw={600} mb={6}>{t('tech.comments')}</Text>
        <Group align="flex-end" wrap="nowrap">
          <Textarea style={{ flex: 1 }} autosize minRows={1} placeholder={t('tech.addComment')} value={comment} onChange={(e) => setComment(e.currentTarget.value)} />
          <Button disabled={!comment.trim()} onClick={() => { void change('comment.add', { body: comment.trim() }); setComment(''); }}>{t('tech.send')}</Button>
        </Group>
        <Stack gap={6} mt="sm">
          {job.comments.map((c, i) => <div key={i}><Text fz="xs" c="dimmed">{c.userName ?? t('tech.me')} · {fmtDateTime(c.createdAt)}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{c.body}</Text></div>)}
        </Stack>
      </Card>
      {bundle?.can?.createWorkOrders && <Anchor fz="xs" ta="center" onClick={() => navigate('/tech/new-work-order')}>{t('tech.newWorkOrder')}</Anchor>}

      <HoursModal opened={hoursOpen} onClose={() => setHoursOpen(false)} onSave={(p) => void change('labor.add', p)}
        crew={bundle?.can?.logForOthers ? job.crew : undefined} me={bundle?.employeeId} />
      <HoldModal opened={holdOpen} noteRequired={!!bundle?.completionRules.holdNote} onClose={() => setHoldOpen(false)} onSave={(p) => void change('status.change', { status: 'OnHold', ...p })} />
      <CompleteSheet opened={completeOpen} job={job} onClose={() => setCompleteOpen(false)} onDone={() => navigate('/tech')} />
    </Stack>
  );
}

function Step({ step, onChange }: { step: TechJob['tasks'][number]; onChange: (p: { isDone?: boolean; reading?: string; remarks?: string }) => void }) {
  const { t } = useTranslation();
  const [reading, setReading] = useState(step.reading ?? '');
  const [remarks, setRemarks] = useState(step.remarks ?? '');
  return (
    <div>
      <Checkbox label={step.description} checked={step.isDone} onChange={(e) => onChange({ isDone: e.currentTarget.checked })} />
      <Group grow gap="xs" mt={4} pl={30}>
        <TextInput size="xs" placeholder={t('tech.reading')} maxLength={100} value={reading} onChange={(e) => setReading(e.currentTarget.value)}
          onBlur={() => { if (reading !== (step.reading ?? '')) onChange({ reading }); }} />
        <TextInput size="xs" placeholder={t('tech.remarks')} maxLength={2000} value={remarks} onChange={(e) => setRemarks(e.currentTarget.value)}
          onBlur={() => { if (remarks !== (step.remarks ?? '')) onChange({ remarks }); }} />
      </Group>
    </div>
  );
}

function HoursModal({ opened, onClose, onSave, crew, me }: {
  opened: boolean; onClose: () => void; onSave: (p: { workDate: string; hours: number; overtimeHours: number; notes: string | null; employeeId?: string }) => void;
  /** A crew leader picks whose hours these are (0.11.0); empty for everyone else. */
  crew?: { employeeId: string; name: string }[]; me?: string;
}) {
  const { t } = useTranslation();
  const [who, setWho] = useState<string | null>(me ?? null);
  const [date, setDate] = useState<Date | null>(new Date());
  const [hours, setHours] = useState<number | string>(1);
  const [ot, setOt] = useState<number | string>(0);
  const [notes, setNotes] = useState('');
  const total = Number(hours || 0) + Number(ot || 0);
  return (
    <Modal opened={opened} onClose={onClose} title={t('tech.logHours')}>
      <Stack>
        {crew && crew.length > 0 && (
          <Select label={t('techJob.whoseHours')} data={[...(me && !crew.some((c) => c.employeeId === me) ? [{ value: me, label: t('tech.me') }] : []),
            ...crew.map((c) => ({ value: c.employeeId, label: c.employeeId === me ? `${c.name} (${t('tech.me')})` : c.name }))]} value={who} onChange={setWho} />
        )}
        <DateInput label={t('tech.date')} value={date} onChange={(v) => setDate(v ? new Date(v) : null)} minDate={dayjs().subtract(30, 'day').toDate()} maxDate={new Date()} valueFormat="DD MMM YYYY" />
        <Group grow>
          <NumberInput label={t('tech.hours')} min={0} max={24} step={0.25} decimalScale={2} value={hours} onChange={setHours} data-autofocus />
          <NumberInput label={t('tech.overtime')} min={0} max={24} step={0.25} decimalScale={2} value={ot} onChange={setOt} />
        </Group>
        <Textarea label={t('common.notes')} autosize value={notes} onChange={(e) => setNotes(e.currentTarget.value)} />
        <Button disabled={!date || total <= 0 || total > 24} onClick={() => {
          onSave({ workDate: dayjs(date).format('YYYY-MM-DD'), hours: Number(hours || 0), overtimeHours: Number(ot || 0), notes: notes.trim() || null,
            ...(who && who !== me ? { employeeId: who } : {}) });
          setNotes(''); onClose();
        }}>{t('common.save')}</Button>
      </Stack>
    </Modal>
  );
}

function HoldModal({ opened, noteRequired, onClose, onSave }: { opened: boolean; noteRequired: boolean; onClose: () => void; onSave: (p: { holdReason: HoldReason; note: string | null }) => void }) {
  const { t } = useTranslation();
  const [reason, setReason] = useState<HoldReason | null>(null);
  const [note, setNote] = useState('');
  return (
    <Modal opened={opened} onClose={onClose} title={t('tech.hold')}>
      <Stack>
        <Select data-autofocus label={t('tech.holdReason')} data={HOLD_REASONS.map((r) => ({ value: r, label: t(`holdReason.${r}`) }))} value={reason} onChange={(v) => setReason(v as HoldReason)} />
        <Textarea label={t('common.notes')} required={noteRequired} autosize value={note} onChange={(e) => setNote(e.currentTarget.value)} />
        <Button disabled={!reason || (noteRequired && !note.trim())} onClick={() => { onSave({ holdReason: reason!, note: note.trim() || null }); setNote(''); onClose(); }}>{t('tech.hold')}</Button>
      </Stack>
    </Modal>
  );
}

/** Completion: notes, cause, failure code, downtime, and up to two signatures. Signatures are queued first, then the completion. */
function CompleteSheet({ opened, job, onClose, onDone }: { opened: boolean; job: TechJob; onClose: () => void; onDone: () => void }) {
  const { t } = useTranslation();
  const { bundle, add, queue, discard, fullName } = useTech();
  const rules = bundle?.completionRules;
  const [notes, setNotes] = useState('');
  const [cause, setCause] = useState('');
  const [failure, setFailure] = useState<string | null>(null);
  const [down, setDown] = useState(false);
  const [downtime, setDowntime] = useState<number | string>('');
  const [signer, setSigner] = useState('');
  const [saving, setSaving] = useState(false);
  const mine = useRef<SignatureHandle>(null);
  const theirs = useRef<SignatureHandle>(null);
  const problems = [
    rules?.notes && !notes.trim() && t('tech.rule.notes'),
    rules?.failureCodeOnBreakdown && job.type === 'Breakdown' && !failure && t('tech.rule.failureCode'),
    rules?.labour && needsLabour(job) && t('tech.rule.labour'),
    rules?.allSteps && job.tasks.some((x) => !x.isDone) && t('tech.rule.allSteps'),
  ].filter(Boolean) as string[];

  const submit = async () => {
    setSaving(true);
    try {
      // Completing again after a refusal: drop the refused completion, and don't send signatures already given.
      for (const old of staleCompletions(queue, job.id)) await discard(old);
      const a = alreadySigned(fullName ?? '', job.files, queue, job.id, fullName) ? null : await mine.current?.toBlob();
      if (a) await add({ type: 'photo.add', workOrderId: job.id, payload: { kind: 'signature' } }, { blob: a, fileName: 'signature-technician.png' });
      const b = alreadySigned(signer.trim(), job.files, queue, job.id, fullName) ? null : await theirs.current?.toBlob();
      if (b) await add({ type: 'photo.add', workOrderId: job.id, payload: { kind: 'signature', signedBy: signer.trim() || null } }, { blob: b, fileName: 'signature.png' });
      await add({ type: 'status.change', workOrderId: job.id, payload: {
        status: 'Completed', completionNotes: notes.trim() || null, causeNotes: cause.trim() || null, failureCodeId: failure,
        // With a stop on the asset's downtime log, the server fills the downtime from the stop.
        ...(job.downtime ? {} : { assetWasDown: down, downtimeHours: down && downtime !== '' ? Number(downtime) : null }),
      } });
      onClose(); onDone();
    } finally { setSaving(false); }
  };

  return (
    <Modal opened={opened} onClose={onClose} title={t('tech.complete')} fullScreen>
      <Stack maw={560} mx="auto">
        <Textarea label={t('tech.workDone')} autosize minRows={3} required={rules?.notes} value={notes} onChange={(e) => setNotes(e.currentTarget.value)} />
        <Textarea label={t('tech.cause')} autosize value={cause} onChange={(e) => setCause(e.currentTarget.value)} />
        <Select label={t('tech.failureCode')} clearable searchable data={(bundle?.failureCodes ?? []).map((f) => ({ value: f.id, label: `${f.code} · ${f.name}` }))}
          value={failure} onChange={setFailure} required={rules?.failureCodeOnBreakdown && job.type === 'Breakdown'} />
        {job.downtime
          ? <Alert color="blue" variant="light" title={t('tech.assetWasDown')}>
              {job.downtime.endedAt
                ? t('downtime.fromLogEnded', { start: fmtDateTime(job.downtime.startedAt), end: fmtDateTime(job.downtime.endedAt) })
                : t('tech.stopFromLog', { start: fmtDateTime(job.downtime.startedAt) })}
            </Alert>
          : <>
              <Switch label={t('tech.assetWasDown')} checked={down} onChange={(e) => setDown(e.currentTarget.checked)} />
              {down && <NumberInput label={t('tech.downtime')} min={0} decimalScale={2} value={downtime} onChange={setDowntime} />}
            </>}
        <Divider />
        <SignaturePad ref={mine} label={t('tech.mySignature')} />
        <TextInput label={t('tech.signerName')} value={signer} onChange={(e) => setSigner(e.currentTarget.value)} maxLength={200} />
        <SignaturePad ref={theirs} label={t('tech.theirSignature')} />
        {problems.length > 0 && <Alert color="orange">{problems.map((p) => <Text key={p} fz="sm">{p}</Text>)}</Alert>}
        <Group grow>
          <Button variant="default" onClick={onClose}>{t('common.cancel')}</Button>
          <Button color="teal" loading={saving} disabled={problems.length > 0} onClick={() => void submit()}>{t('tech.complete')}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}
