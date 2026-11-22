import { Alert, Anchor, Badge, Group, Stack, Text } from '@mantine/core';
import { Link } from 'react-router';
import { useTranslation } from 'react-i18next';
import { IconArrowBackUp } from '@tabler/icons-react';
import type { StepSource } from '@/api/types';
import { levelColor } from '@/components/measure';

/** Follow-up work (0.13.0): the work order and checklist step that raised it, with the answer, the value and the step's photos. */
export function SourceBanner({ source }: { source: StepSource }) {
  const { t } = useTranslation();
  return (
    <Alert variant="light" color="orange" icon={<IconArrowBackUp size={18} />}
      title={<Text fz="sm" fw={600}>{t('checklist.fromStep', { step: source.stepSequence ?? '', number: '' })}{' '}
        <Anchor component={Link} to={`/work-orders/${source.workOrderId}`} fz="sm" fw={600}>{source.number}</Anchor></Text>}>
      <Stack gap={4}>
        {source.stepDescription && <Text fz="sm">{source.stepDescription}</Text>}
        <Group gap="xs">
          {source.answer && <Badge size="sm" color={source.answer === 'NotOk' ? 'red' : 'gray'}>{t(`checklist.answers.${source.answer}`)}</Badge>}
          {source.value !== null && source.value !== undefined && <Text fz="sm">{source.value} {source.unit ?? ''}</Text>}
          {source.level && source.level !== 'Normal' && <Badge size="sm" color={levelColor(source.level)}>{t(`checklist.levels.${source.level}`)}</Badge>}
          {source.textValue && <Text fz="sm">{source.textValue}</Text>}
        </Group>
        {source.remarks && <Text fz="sm" c="dimmed">{source.remarks}</Text>}
        {source.photos.length > 0 && <Group gap="xs">{source.photos.map((p) => (
          <Anchor key={p.id} href={`/api/attachments/${p.id}/content`} target="_blank">
            <img src={`/api/attachments/${p.id}/content`} alt={p.fileName} style={{ height: 64, borderRadius: 4, objectFit: 'cover' }} />
          </Anchor>
        ))}</Group>}
      </Stack>
    </Alert>
  );
}
