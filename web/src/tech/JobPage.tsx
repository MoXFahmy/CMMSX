import { useEffect, useMemo, useRef, useState } from 'react';
import { AccessWindowsList, StoppedBadge } from '@/components/AccessWindowsEditor';
import { ActionIcon, Alert, Anchor, Badge, Button, Card, Checkbox, Divider, Group, Modal, NumberInput, Select, Stack, Switch, Text, TextInput, Textarea, Title } from '@mantine/core';
import { DateInput } from '@mantine/dates';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { IconCamera, IconPhoto, IconCheck, IconChevronLeft, IconClock, IconMicrophone, IconPlayerPause, IconPlayerPlay, IconSparkles } from '@tabler/icons-react';
import { HOLD_REASONS, type HoldReason } from '@/api/types';
import { PriorityBadge, WoStatusBadge } from '@/components/WorkBadges';
import { fmtDate, fmtDateTime, hoursText, notifyError } from '@/components/utils';
import { downFor } from '@/components/downtime';
import { JobUnits } from './JobUnits';
import { withQueued } from './overlay';
import { shrink } from './photos';
import { dueLabel } from './due';
import { codeOptions } from '@/components/codes';
import { alreadySigned, needsLabour, safetyOpen, staleCompletions, stepProblems } from './complete';
import { Checklist } from './Checklist';
import { SignaturePad, type SignatureHandle } from './SignaturePad';
import { useTech } from './useTech';
import { JobParts } from './StockPages';
import { JobActions, StopCard } from './JobExtras';
import type { TechJob } from './types';
import type { AiReportResult } from '@/components/aiReport';
import { aiChanges, aiEnabled, defaultTicks, partStore } from './aiFill';
import { dictate, speechAvailable } from './speech';
import { fillReport } from './api';

/** One job: checklist, hours, photos, comments, and start / hold / complete — all of it works without signal. */
export function JobPage() {
  const { t } = useTranslation();
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const { bundle, places, queue, sent, add, online } = useTech();
  const base = bundle?.jobs.find((j) => j.id === id);
  // Sent changes first (they are older), then those still waiting.
  const canIssue = bundle?.can?.issueStock ?? true;
  const job = useMemo(() => (base ? withQueued(base, [...sent, ...queue], { canIssue, points: bundle?.points }) : undefined), [base, sent, queue, canIssue, bundle?.points]);
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
  // Safety steps first (0.13.0): the work doesn't start until they are answered.
  const safetyBlocked = safetyOpen(job.tasks, null).some((s) => !s.assetId);
  const start = () => (safetyBlocked ? notifyError(new Error(t('checklist.safetyFirstBody'))) : void change('status.change', { status: 'InProgress' }));
  const addStepPhoto = async (taskId: string, f: File) => {
    try { await add({ type: 'photo.add', workOrderId: job.id, payload: { kind: 'photo', taskId } }, { blob: await shrink(f), fileName: f.name.replace(/\.\w+$/, '') + '.jpg' }); }
    catch (e) { notifyError(e); }
  };

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
        <JobCodes job={job} />
        {job.group && <Text fz="xs" c="dimmed">{t('tech.inGroup', { number: job.group.number })}</Text>}
        {job.description && <Text fz="sm" mt={6} style={{ whiteSpace: 'pre-wrap' }}>{job.description}</Text>}
      </Card>
      {job.asset?.downSince && (
        <Alert color="red" title={t('downtime.downSince', { when: fmtDateTime(job.asset.downSince), for: downFor(job.asset.downSince, new Date()) })} />
      )}
      {(job.onlyWhenStopped || (job.accessWindows?.length ?? 0) > 0) && (
        <Card><Stack gap={6}><StoppedBadge show={job.onlyWhenStopped} /><AccessWindowsList windows={job.accessWindows} /></Stack></Card>
      )}
      {job.safetyNotes && <Alert color="red" title={t('tech.safety')}>{job.safetyNotes}</Alert>}
      {(job.asset?.notesToTechnician || job.location?.notesToTechnician) && (
        <Alert color="yellow" title={t('tech.notesToTech')}>{[job.asset?.notesToTechnician, job.location?.notesToTechnician].filter(Boolean).join('\n\n')}</Alert>
      )}

      {job.group && job.group.comments.length > 0 && (
        <Card withBorder p="sm"><Stack gap={6}>
          <Text fw={600}>{t('tech.groupComments', { number: job.group.number })}</Text>
          {job.group.comments.slice(0, 10).map((c, i) => <div key={i}><Text fz="xs" c="dimmed">{c.userName} · {fmtDateTime(c.createdAt)}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{c.body}</Text></div>)}
        </Stack></Card>
      )}
      <Group grow>
        {canStart && <Button leftSection={<IconPlayerPlay size={16} />} onClick={start}>{t('tech.start')}</Button>}
        {canHold && <Button variant="default" leftSection={<IconPlayerPause size={16} />} onClick={() => setHoldOpen(true)}>{t('tech.hold')}</Button>}
        {canComplete && <Button color="teal" leftSection={<IconCheck size={16} />} onClick={() => setCompleteOpen(true)}>{t('tech.complete')}</Button>}
      </Group>

      {job.tasks.length > 0 && (
        <Checklist job={job} bundle={bundle} meters={places?.meters ?? []} stepPhotos={job.stepPhotos} onChange={(taskId, p) => void change('task.update', { taskId, ...p })} onPhoto={(taskId, f) => void addStepPhoto(taskId, f)} />
      )}

      <StopCard job={job} />
      <JobParts job={job} />
      <JobUnits job={job} />
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
        {(job.templateDocuments?.length ?? 0) > 0 && <Stack gap={2} mt={6}>
          <Text fz="xs" c="dimmed">{online ? t('tech.templateDocs') : t('tech.templateDocsOffline')}</Text>
          {job.templateDocuments!.map((d) => online
            ? <Anchor key={d.id} fz="sm" href={`/api/attachments/${d.id}/content`} target="_blank" rel="noopener">{d.fileName}</Anchor>
            : <Text key={d.id} fz="sm" c="dimmed">{d.fileName}</Text>)}
        </Stack>}
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
      {bundle?.can?.createWorkOrders && <Anchor fz="xs" ta="center" onClick={() => navigate('/tech/new-work-order')}>{t('techJob.newWorkOrder')}</Anchor>}

      <HoursModal opened={hoursOpen} onClose={() => setHoursOpen(false)} onSave={(p) => void change('labor.add', p)}
        crew={bundle?.can?.logForOthers ? job.crew : undefined} me={bundle?.employeeId} />
      <HoldModal opened={holdOpen} noteRequired={!!bundle?.completionRules.holdNote} onClose={() => setHoldOpen(false)} onSave={(p) => void change('status.change', { status: 'OnHold', ...p })} />
      <CompleteSheet opened={completeOpen} job={job} onClose={() => setCompleteOpen(false)} onDone={() => navigate('/tech')} />
    </Stack>
  );
}


export function HoursModal({ opened, onClose, onSave, crew, me, initialDate }: {
  opened: boolean; onClose: () => void; onSave: (p: { workDate: string; hours: number; overtimeHours: number; notes: string | null; employeeId?: string }) => void;
  /** A crew leader picks whose hours these are (0.11.0); empty for everyone else. */
  crew?: { employeeId: string; name: string }[]; me?: string;
  /** The day the hours are for (My day, 0.19.0); today when not given. */
  initialDate?: string;
}) {
  const { t } = useTranslation();
  const [who, setWho] = useState<string | null>(me ?? null);
  const [date, setDate] = useState<Date | null>(initialDate ? new Date(`${initialDate}T12:00:00`) : new Date());
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
  const { t, i18n } = useTranslation();
  const { bundle, add, queue, discard, fullName, online } = useTech();
  const rules = bundle?.completionRules;
  const [notes, setNotes] = useState('');
  const [cause, setCause] = useState('');
  const [failure, setFailure] = useState<string | null>(job.failureCodeId ?? null);
  const [remedy, setRemedy] = useState<string | null>(job.remedyId ?? null);
  const [symptom, setSymptom] = useState<string | null>(job.symptomId ?? null);
  // AI report (0.15.0): dictation adds to Work done; Fill with AI suggests the fields; hours and parts stay ticked boxes until completion.
  const [ai, setAi] = useState<AiReportResult | null>(null);
  const [ticks, setTicks] = useState<{ hours: boolean; parts: string[] }>({ hours: false, parts: [] });
  const [filling, setFilling] = useState(false);
  const [canDictate, setCanDictate] = useState(false);
  const [listening, setListening] = useState(false);
  useEffect(() => { if (opened) void speechAvailable().then(setCanDictate); }, [opened]);
  const canIssue = !!bundle?.can?.issueStock;
  const listen = async () => {
    setListening(true);
    try { const heard = await dictate(i18n.language === 'ar' ? 'ar' : 'en'); if (heard) setNotes((n) => (n.trim() ? `${n.trimEnd()} ${heard}` : heard)); }
    finally { setListening(false); }
  };
  const fill = async () => {
    setFilling(true);
    try {
      const r = await fillReport(job.id, notes);
      setAi(r);
      if (r.workDone?.trim()) setNotes(r.workDone);
      if (r.causeDetails?.trim()) setCause(r.causeDetails);
      if (r.symptom) setSymptom(r.symptom.id);
      if (r.cause) setFailure(r.cause.id);
      if (r.remedy) setRemedy(r.remedy.id);
      setTicks(defaultTicks(job, r, canIssue));
    } catch (e) { notifyError(e); } finally { setFilling(false); }
  };
  const logsHours = !!ai?.hours && ticks.hours;
  const [down, setDown] = useState(false);
  const [downtime, setDowntime] = useState<number | string>('');
  const [signer, setSigner] = useState('');
  const [saving, setSaving] = useState(false);
  const mine = useRef<SignatureHandle>(null);
  const theirs = useRef<SignatureHandle>(null);
  const problems = [
    rules?.notes && !notes.trim() && t('tech.rule.notes'),
    rules?.failureCodeOnBreakdown && job.type === 'Breakdown' && !failure && t('tech.rule.failureCode'),
    rules?.labour && needsLabour(job) && !logsHours && t('tech.rule.labour'),
    rules?.allSteps && job.tasks.some((x) => !x.isDone) && t('tech.rule.allSteps'),
    ...(() => { const sp = stepProblems(job, queue, { readings: rules?.readings });
      return [sp.safety.length > 0 && t('tech.rule.safety', { steps: sp.safety.join(', ') }), sp.photos.length > 0 && t('tech.rule.photos', { steps: sp.photos.join(', ') }), sp.readings.length > 0 && t('tech.rule.readings', { steps: sp.readings.join(', ') })]; })(),
  ].filter(Boolean) as string[];

  const submit = async () => {
    setSaving(true);
    try {
      // Completing again after a refusal: drop the refused completion, and don't send signatures already given.
      for (const old of staleCompletions(queue, job.id)) await discard(old);
      // The AI's hours and parts the technician kept ticked go first, as the usual changes.
      if (ai) for (const c of aiChanges(job, ai, ticks, canIssue, dayjs().format('YYYY-MM-DD'))) await add({ type: c.type, workOrderId: job.id, payload: c.payload });
      const a = alreadySigned(fullName ?? '', job.files, queue, job.id, fullName) ? null : await mine.current?.toBlob();
      if (a) await add({ type: 'photo.add', workOrderId: job.id, payload: { kind: 'signature' } }, { blob: a, fileName: 'signature-technician.png' });
      const b = alreadySigned(signer.trim(), job.files, queue, job.id, fullName) ? null : await theirs.current?.toBlob();
      if (b) await add({ type: 'photo.add', workOrderId: job.id, payload: { kind: 'signature', signedBy: signer.trim() || null } }, { blob: b, fileName: 'signature.png' });
      await add({ type: 'status.change', workOrderId: job.id, payload: {
        status: 'Completed', completionNotes: notes.trim() || null, causeNotes: cause.trim() || null, failureCodeId: failure, remedyId: remedy,
        ...(symptom && symptom !== job.symptomId ? { symptomId: symptom } : {}),
        // With a stop on the asset's downtime log, the server fills the downtime from the stop.
        ...(job.downtime ? {} : { assetWasDown: down, downtimeHours: down && downtime !== '' ? Number(downtime) : null }),
      } });
      onClose(); onDone();
    } finally { setSaving(false); }
  };

  return (
    <Modal opened={opened} onClose={onClose} title={t('tech.complete')} fullScreen>
      <Stack maw={560} mx="auto">
        <Textarea label={t('tech.workDone')} autosize minRows={3} required={rules?.notes} value={notes} onChange={(e) => setNotes(e.currentTarget.value)}
          rightSection={canDictate ? <ActionIcon variant="subtle" size="lg" loading={listening} aria-label={t('tech.dictate')} onClick={() => void listen()}><IconMicrophone size={20} /></ActionIcon> : undefined} />
        {aiEnabled(bundle) && (
          <Group justify="space-between" gap="xs">
            {ai ? <Badge variant="light" color="violet" leftSection={<IconSparkles size={12} />}>{t('ai.fromAi')}</Badge> : <span />}
            <Button size="xs" variant="light" color="violet" leftSection={<IconSparkles size={14} />} loading={filling}
              disabled={!online || !notes.trim()} onClick={() => void fill()}>{t('tech.fillWithAi')}</Button>
          </Group>
        )}
        {aiEnabled(bundle) && !online && <Text fz="xs" c="dimmed">{t('tech.aiNeedsConnection')}</Text>}
        {ai && (ai.hours || ai.parts.length > 0) && (
          <Card withBorder padding="sm"><Stack gap={6}>
            {ai.hours ? <Checkbox label={t('tech.logHoursForMe', { hours: ai.hours })} checked={ticks.hours} onChange={(e) => { const on = e.currentTarget.checked; setTicks((x) => ({ ...x, hours: on })); }} /> : null}
            {ai.parts.map((p) => canIssue && partStore(job, p.partId)
              ? <Checkbox key={p.partId} label={t('ai.usePart', { qty: p.quantity, part: `${p.partNumber} · ${p.name}` })} checked={ticks.parts.includes(p.partId)}
                  onChange={(e) => { const on = e.currentTarget.checked; setTicks((x) => ({ ...x, parts: on ? [...x.parts, p.partId] : x.parts.filter((y) => y !== p.partId) })); }} />
              : <Text key={p.partId} fz="sm" c="dimmed">{t('ai.usePart', { qty: p.quantity, part: `${p.partNumber} · ${p.name}` })}</Text>)}
          </Stack></Card>
        )}
        <Textarea label={t('tech.cause')} autosize value={cause} onChange={(e) => setCause(e.currentTarget.value)} />
        <Select label={t('codes.symptom')} clearable searchable data={codeOptions(bundle?.symptoms ?? [], [], null, t)} value={symptom} onChange={setSymptom} />
        {/* Causes most often found with the job's symptom, and remedies most often used for the cause, come first (0.14.0). */}
        <Select label={t('tech.failureCode')} clearable searchable data={codeOptions(bundle?.failureCodes ?? [], bundle?.codePairs?.symptomCause ?? [], job.symptomId, t)}
          value={failure} onChange={setFailure} required={rules?.failureCodeOnBreakdown && job.type === 'Breakdown'} />
        <Select label={t('codes.remedy')} clearable searchable data={codeOptions(bundle?.remedies ?? [], bundle?.codePairs?.causeRemedy ?? [], failure, t)}
          value={remedy} onChange={setRemedy} />
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

/** The job's symptom, cause and remedy (0.14.0), as the phone's lists name them. */
function JobCodes({ job }: { job: TechJob }) {
  const { t } = useTranslation();
  const { bundle } = useTech();
  const name = (list: { id: string; code: string; name: string }[] | undefined, id?: string | null) => {
    const c = id ? list?.find((x) => x.id === id) : undefined;
    return c ? `${c.code} · ${c.name}` : null;
  };
  const lines = ([['codes.symptom', name(bundle?.symptoms, job.symptomId)], ['codes.cause', name(bundle?.failureCodes, job.failureCodeId)], ['codes.remedy', name(bundle?.remedies, job.remedyId)]] as const)
    .filter(([, v]) => v);
  if (lines.length === 0) return null;
  return <>{lines.map(([k, v]) => <Text key={k} fz="xs" c="dimmed">{t(k)}: {v}</Text>)}</>;
}

