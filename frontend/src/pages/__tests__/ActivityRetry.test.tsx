import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import ActivityPage from '../ActivityPage';
import { UI_SETTINGS_QUERY_KEY } from '../../hooks/useUISettings';

const transport = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }));
vi.mock('../../api/client', () => ({ default: transport }));

const clients: QueryClient[] = [];
beforeEach(() => {
  localStorage.clear();
  transport.get.mockReset();
  transport.post.mockReset().mockResolvedValue({ data: { success: true } });
});
afterEach(() => {
  for (const client of clients.splice(0)) client.clear();
});

async function showQueue(width: number, status = 9, progress = 100, canRetryImport = true) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: width });
  const queueItem = {
    id: 41, eventId: 7, event: { id: 7, title: 'Owned team event', organization: 'NFL',
      eventDate: '2020-09-01T20:00:00Z', monitored: true, hasFile: false },
    title: 'NFL.2020.Week1.720p.WEB-DL', downloadId: 'owned-pack-job', downloadClientId: 1,
    downloadClient: { id: 1, name: 'Owned fixture' }, status, progress, canRetryImport,
    size: 4096, downloaded: progress === 100 ? 4096 : 4000, protocol: 'Torrent', quality: 'WEBDL-720p',
    errorMessage: status === 9 ? 'Pack member unresolved: No member uniquely identifies this event.' : 'Import failed',
    statusMessages: [], added: '2020-09-02T00:00:00Z', completedAt: '2020-09-02T00:01:00Z'
  };
  transport.get.mockImplementation(async (path: string) => {
    if (path === '/queue') return { data: [queueItem] };
    if (path === '/pending-imports') return { data: [] };
    throw new Error('Unconfigured page request ' + path);
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  clients.push(client);
  client.setQueryData(UI_SETTINGS_QUERY_KEY, { eventViewMode: 'compact' });
  const { container } = render(<QueryClientProvider client={client}><MemoryRouter><ActivityPage /></MemoryRouter></QueryClientProvider>);
  await screen.findAllByText('Owned team event');
  const branch = width >= 640
    ? within(screen.getByRole('table')).getByText('Owned team event').closest('tr')
    : container.querySelector<HTMLElement>('[class~="sm:hidden"][class~="space-y-3"]');
  expect(branch).not.toBeNull();
  const row = within(branch as HTMLElement);
  expect(row.getByText('Owned team event')).toBeInTheDocument();
  return { user: userEvent.setup(), row };
}

describe.each([['desktop', 1000], ['phone', 390]] as const)('Activity retry on %s', (_view, width) => {
  it('uses the existing retry action for a recoverable pack warning', async () => {
    const { user, row } = await showQueue(width);
    await user.click(row.getByRole('button', { name: 'Retry Import' }));
    await waitFor(() => expect(transport.post).toHaveBeenCalledTimes(1));
    expect(transport.post).toHaveBeenCalledWith('/queue/41/retry');
  });

  it('sends a selected recoverable pack warning through bulk retry', async () => {
    const { user, row } = await showQueue(width);
    await user.click(row.getByRole('checkbox'));
    const action = screen.getByRole('button', { name: 'Import Selected' });
    expect(action).toBeEnabled();
    await user.click(action);
    await waitFor(() => expect(transport.post).toHaveBeenCalledTimes(1));
    expect(transport.post).toHaveBeenCalledWith('/queue/41/retry');
  });

  it('keeps an ineligible warning out of individual and bulk retry', async () => {
    const { user, row } = await showQueue(width, 9, 100, false);
    expect(row.queryByRole('button', { name: 'Retry Import' })).not.toBeInTheDocument();
    await user.click(row.getByRole('checkbox'));
    expect(screen.getByRole('button', { name: 'Import Selected' })).toBeDisabled();
    expect(transport.post).not.toHaveBeenCalled();
  });

  it('preserves the existing completed failure retry action', async () => {
    const { user, row } = await showQueue(width, 4, 100, true);
    await user.click(row.getByRole('button', { name: 'Retry Import' }));
    await waitFor(() => expect(transport.post).toHaveBeenCalledTimes(1));
    expect(transport.post).toHaveBeenCalledWith('/queue/41/retry');
  });

  it('keeps incomplete failures out of individual and bulk retry', async () => {
    const { user, row } = await showQueue(width, 4, 99, false);
    expect(row.queryByRole('button', { name: 'Retry Import' })).not.toBeInTheDocument();
    await user.click(row.getByRole('checkbox'));
    expect(screen.getByRole('button', { name: 'Import Selected' })).toBeDisabled();
    expect(transport.post).not.toHaveBeenCalled();
  });
});
