import { Text, Tooltip } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { fmtDateTime } from './utils';
import { deadlineText, slaColor, type SlaState } from './sla';

/** A deadline as "due in 3 h" / "2 h late" / "met", coloured when due soon or late; the exact time on hover. */
export function SlaText({ deadline, state, fz = 'sm' }: { deadline?: string | null; state: SlaState; fz?: string }) {
  const { t } = useTranslation();
  const text = deadlineText(deadline, state, new Date(), t);
  if (!text) return <Text fz={fz} c="dimmed" span>—</Text>;
  return (
    <Tooltip label={fmtDateTime(deadline)} withArrow>
      <Text fz={fz} c={slaColor(state)} fw={state === 'Late' ? 600 : undefined} span>{text}</Text>
    </Tooltip>
  );
}
