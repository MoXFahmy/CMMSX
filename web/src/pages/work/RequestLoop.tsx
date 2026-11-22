import { useState } from 'react';
import { Alert, Badge, Button, Card, Group, Paper, Rating, Stack, Text, Textarea, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { IconCheck, IconMessage, IconSend, IconX } from '@tabler/icons-react';
import { get, post } from '@/api/client';
import type { RequestComment, RequestFeedback } from '@/api/types';
import { fmtDateTime, notifyError, notifySuccess } from '@/components/utils';

/** The thread between the requester and maintenance (0.10.0). Read-only (on the work order) when <comments> are given. */
export function RequestComments({ requestId, comments, readOnly }: { requestId: string; comments?: RequestComment[]; readOnly?: boolean }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [body, setBody] = useState('');
  const [busy, setBusy] = useState(false);
  const { data } = useQuery({ queryKey: ['request-comments', requestId], queryFn: () => get<RequestComment[]>(`/work-requests/${requestId}/comments`), enabled: !comments });
  const list = comments ?? data ?? [];
  const send = async () => {
    if (!body.trim()) return;
    setBusy(true);
    try { await post(`/work-requests/${requestId}/comments`, { body }); setBody(''); await qc.invalidateQueries({ queryKey: ['request-comments', requestId] }); }
    catch (e) { notifyError(e); } finally { setBusy(false); }
  };
  return (
    <Stack gap="xs">
      <Group gap={6}><IconMessage size={16} /><Title order={6}>{t('requestLoop.comments')}</Title>{readOnly && <Text fz="xs" c="dimmed">{t('requestLoop.fromRequest')}</Text>}</Group>
      {list.length === 0 && <Text fz="sm" c="dimmed">{t('requestLoop.noComments')}</Text>}
      {list.map((c) => (
        <Paper key={c.id} withBorder p="xs" radius="sm" bg={c.byRequester ? 'var(--mantine-color-blue-light)' : undefined}>
          <Group justify="space-between" gap="xs"><Text fz="xs" fw={600}>{c.userName}{c.byRequester ? ` · ${t('requestLoop.requester')}` : ''}</Text><Text fz="xs" c="dimmed">{fmtDateTime(c.createdAt)}</Text></Group>
          <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{c.body}</Text>
        </Paper>
      ))}
      {!readOnly && (
        <Group align="flex-end" gap="xs" wrap="nowrap">
          <Textarea placeholder={t('requestLoop.writeComment')} autosize minRows={1} maxLength={4000} value={body} onChange={(e) => setBody(e.currentTarget.value)} style={{ flex: 1 }} />
          <Button leftSection={<IconSend size={16} />} loading={busy} disabled={!body.trim()} onClick={send}>{t('requestLoop.send')}</Button>
        </Group>
      )}
    </Stack>
  );
}

/** One answer: accepted or refused, the stars, the comment, who and when. */
export function FeedbackLine({ f }: { f: RequestFeedback }) {
  const { t } = useTranslation();
  return (
    <Stack gap={2}>
      <Group gap="xs">
        <Badge color={f.accepted ? 'teal' : 'red'} variant="light">{f.accepted ? t('requestLoop.accepted') : t('requestLoop.refused')}</Badge>
        {f.rating ? <Rating value={f.rating} readOnly size="sm" /> : null}
        <Text fz="xs" c="dimmed">{f.byName} · {fmtDateTime(f.answeredAt)}</Text>
      </Group>
      {f.comment && <Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{f.comment}</Text>}
    </Stack>
  );
}

/** The accept / refuse form: on the request page for its creator, and on the single-use link page. */
export function AnswerForm({ refuseNeedsComment, onAnswer }: { refuseNeedsComment: boolean; onAnswer: (accepted: boolean, rating: number | null, comment: string) => Promise<void> }) {
  const { t } = useTranslation();
  const [rating, setRating] = useState(0);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState<'accept' | 'refuse' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const answer = async (accepted: boolean) => {
    if (accepted && rating === 0) { setError(t('requestLoop.ratingNeeded')); return; }
    if (!accepted && refuseNeedsComment && !comment.trim()) { setError(t('requestLoop.reasonNeeded')); return; }
    setError(null); setBusy(accepted ? 'accept' : 'refuse');
    try { await onAnswer(accepted, rating || null, comment); } finally { setBusy(null); }
  };
  return (
    <Stack gap="sm">
      <Stack gap={4}><Text fz="sm" fw={500}>{t('requestLoop.howDidWeDo')}</Text><Rating value={rating} onChange={setRating} size="lg" /></Stack>
      <Textarea label={t('requestLoop.yourComment')} description={refuseNeedsComment ? t('requestLoop.refuseNeedsReason') : undefined} autosize minRows={2} maxLength={2000}
        value={comment} onChange={(e) => setComment(e.currentTarget.value)} />
      {error && <Text c="red" fz="sm">{error}</Text>}
      <Group>
        <Button color="teal" leftSection={<IconCheck size={16} />} loading={busy === 'accept'} disabled={busy !== null} onClick={() => void answer(true)}>{t('requestLoop.accept')}</Button>
        <Button color="red" variant="light" leftSection={<IconX size={16} />} loading={busy === 'refuse'} disabled={busy !== null} onClick={() => void answer(false)}>{t('requestLoop.refuse')}</Button>
      </Group>
    </Stack>
  );
}

/** On the request: waiting for the requester, the answer form for its creator, and the answers so far. */
export function FeedbackPanel({ requestId, awaiting, canAnswer, feedback, refuseNeedsComment, onDone }: {
  requestId: string; awaiting: boolean; canAnswer: boolean; feedback: RequestFeedback[]; refuseNeedsComment: boolean; onDone: () => Promise<void>;
}) {
  const { t } = useTranslation();
  if (!awaiting && feedback.length === 0) return null;
  const send = async (accepted: boolean, rating: number | null, comment: string) => {
    try { await post(`/work-requests/${requestId}/feedback`, { accepted, rating, comment }); notifySuccess(accepted ? t('requestLoop.thanksAccepted') : t('requestLoop.thanksRefused')); await onDone(); }
    catch (e) { notifyError(e); }
  };
  return (
    <Card><Stack gap="sm">
      <Title order={6}>{t('requestLoop.finishedWork')}</Title>
      {awaiting && canAnswer && <><Alert color="blue">{t('requestLoop.pleaseAnswer')}</Alert><AnswerForm refuseNeedsComment={refuseNeedsComment} onAnswer={send} /></>}
      {awaiting && !canAnswer && <Alert color="yellow">{t('requestLoop.waitingForRequester')}</Alert>}
      {feedback.map((f, i) => <FeedbackLine key={f.id ?? i} f={f} />)}
    </Stack></Card>
  );
}
