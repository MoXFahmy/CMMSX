import { beforeEach, describe, expect, it, vi } from 'vitest';
import { modals } from '@mantine/modals';
import { renderHtml } from '@/test/render';
import { ApprovalPanel, askRestartApproval } from './ApprovalPanel';
import type { ApprovalPanel as Panel } from './approvals';

vi.mock('@mantine/modals', () => ({ modals: { openConfirmModal: vi.fn() } }));

const open = vi.mocked(modals.openConfirmModal);

describe('Restart approval', () => {
  beforeEach(() => open.mockClear());

  it('asks for confirmation before it restarts anything', () => {
    const restart = vi.fn();
    askRestartApproval(restart);
    expect(restart).not.toHaveBeenCalled();
    expect(open).toHaveBeenCalledTimes(1);
    const dialog = open.mock.calls[0][0];
    expect(String(dialog.title)).toBe('Restart the approval?');
    expect(renderHtml(<>{dialog.children}</>)).toMatch(/signatures.*first level/);
    expect(dialog.labels?.confirm).toBe('Restart approval');
    dialog.onConfirm?.();
    expect(restart).toHaveBeenCalledTimes(1);
  });

  it('does not restart when the dialog is cancelled', () => {
    const restart = vi.fn();
    askRestartApproval(restart);
    open.mock.calls[0][0].onCancel?.();
    expect(restart).not.toHaveBeenCalled();
  });

  it('still offers the button to people who may restart', () => {
    const panel = { round: 1, steps: [], earlierRounds: [], waiting: true, nobodyCanSign: false } as unknown as Panel;
    expect(renderHtml(<ApprovalPanel approval={panel} canRestart onRestart={() => {}} />)).toContain('Restart approval');
    expect(renderHtml(<ApprovalPanel approval={panel} canRestart={false} onRestart={() => {}} />)).not.toContain('Restart approval');
  });
});
