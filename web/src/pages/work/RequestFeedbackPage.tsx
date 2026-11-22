import { useEffect, useState } from 'react';
import { Alert, Card, Center, Container, Loader, Stack, Text, Title } from '@mantine/core';
import { useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { get, post } from '@/api/client';
import { fmtDateTime } from '@/components/utils';
import { AnswerForm } from './RequestLoop';

type Page = {
  organization: string; language: 'en' | 'ar'; number: string; title: string; requesterName?: string | null; place?: string | null;
  workOrderNumber?: string | null; completedAt?: string | null; completionNotes?: string | null; refuseNeedsComment: boolean;
};

/** The single-use link's page (0.10.0): a requester without a login accepts or refuses finished work. No sign-in, only this request. */
export function RequestFeedbackPage() {
  const { token } = useParams();
  const { t, i18n } = useTranslation();
  const [page, setPage] = useState<Page | null>(null);
  const [state, setState] = useState<'loading' | 'open' | 'gone' | 'accepted' | 'refused'>('loading');
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    get<Page>(`/public/feedback/${token}`).then((p) => { setPage(p); setState('open'); if (p.language && p.language !== i18n.language) void i18n.changeLanguage(p.language); })
      .catch(() => setState('gone'));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);
  const answer = async (accepted: boolean, rating: number | null, comment: string) => {
    try { await post(`/public/feedback/${token}`, { accepted, rating, comment }); setState(accepted ? 'accepted' : 'refused'); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
  };
  return (
    <Container size={560} py="xl">
      {state === 'loading' && <Center py="xl"><Loader /></Center>}
      {state === 'gone' && <Alert color="gray" title={t('requestLoop.linkGoneTitle')}>{t('requestLoop.linkGone')}</Alert>}
      {page && state !== 'loading' && state !== 'gone' && (
        <Card withBorder padding="lg"><Stack>
          <Text c="dimmed" fz="sm">{page.organization}</Text>
          <Title order={3}>{t('requestLoop.publicTitle')}</Title>
          <Stack gap={2}>
            <Text fw={600}>{page.number} · {page.title}</Text>
            {page.place && <Text fz="sm">{page.place}</Text>}
            {page.workOrderNumber && <Text fz="sm" c="dimmed">{t('wr.workOrder')} {page.workOrderNumber}{page.completedAt ? ` · ${fmtDateTime(page.completedAt)}` : ''}</Text>}
          </Stack>
          {page.completionNotes && <Stack gap={2}><Text fz="xs" c="dimmed">{t('requestLoop.workDone')}</Text><Text fz="sm" style={{ whiteSpace: 'pre-wrap' }}>{page.completionNotes}</Text></Stack>}
          {state === 'open' && <AnswerForm refuseNeedsComment={page.refuseNeedsComment} onAnswer={answer} />}
          {state === 'accepted' && <Alert color="teal">{t('requestLoop.thanksAccepted')}</Alert>}
          {state === 'refused' && <Alert color="orange">{t('requestLoop.thanksRefused')}</Alert>}
          {error && <Alert color="red">{error}</Alert>}
        </Stack></Card>
      )}
    </Container>
  );
}
