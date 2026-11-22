import { Button } from '@mantine/core';
import { useTranslation } from 'react-i18next';
import { toggleAnswer, type StepAnswer } from './measure';

const OPTIONS = [['Ok', 'checklist.ok', 'teal'], ['NotOk', 'checklist.notOk', 'red'], ['NotApplicable', 'checklist.na', 'gray']] as const;

/** OK / Not OK / N/A for a checklist step (0.13.0): the chosen answer is shown pressed, and pressing it again clears it. */
export function AnswerButtons({ value, disabled, fullWidth, onChange }: {
  value?: StepAnswer | null; disabled?: boolean; fullWidth?: boolean; onChange: (change: { answer: StepAnswer } | { isDone: false }) => void;
}) {
  const { t } = useTranslation();
  return (
    <Button.Group style={fullWidth ? { width: '100%' } : undefined}>
      {OPTIONS.map(([answer, label, color]) => (
        <Button key={answer} size="xs" color={color} variant={value === answer ? 'filled' : 'default'} aria-pressed={value === answer} disabled={disabled}
          style={fullWidth ? { flex: 1 } : undefined} onClick={() => onChange(toggleAnswer(value, answer))}>{t(label)}</Button>
      ))}
    </Button.Group>
  );
}
