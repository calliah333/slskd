import api from './api';

export const PAGE_SIZE = 500;
export const MAX_PAGE_SIZE = 5_000;

/**
 * Fetches one page of transfers, grouped by user and directory.
 * @param {object} params
 * @param {'download'|'upload'} params.direction - The transfer direction.
 * @param {'all'|'active'|'completed'|'succeeded'|'errored'|'cancelled'} [params.filter] - The state filter.
 * @param {string} [params.username] - Only transfers with exactly this user.
 * @param {string} [params.search] - Only transfers whose filename or username contains this text, ignoring case.
 * @param {number} [params.offset] - The number of matching rows to skip.
 * @param {number} [params.limit] - The page size (1..5000).
 * @returns {Promise<{ filter: string, username?: string, search?: string, offset: number, limit: number, total: number, counts: object, users: object[] }>}
 */
export const getPage = async ({
  direction,
  filter = 'all',
  username,
  search,
  offset = 0,
  limit = PAGE_SIZE,
}) => {
  const parameters = new URLSearchParams();

  parameters.append('filter', filter);
  if (username) parameters.append('username', username);
  if (search) parameters.append('search', search);
  parameters.append('offset', offset);
  parameters.append('limit', limit);

  return (
    await api.get(
      `/transfers/${encodeURIComponent(direction)}s/page?${parameters}`,
    )
  ).data;
};

/**
 * Groups transfers by user, directory, or file and fetches the largest groups.
 * @param {object} params
 * @param {'download'|'upload'} params.direction - The transfer direction.
 * @param {'user'|'directory'|'file'} [params.by] - How to group.
 * @param {'users'|'transfers'|'bytes'|'recent'} [params.sort] - How to order groups (descending).
 * @param {string} [params.filter] - The state filter, as for getPage.
 * @param {string} [params.username] - Only transfers with exactly this user.
 * @param {string} [params.search] - Only transfers whose filename or username contains this text.
 * @param {number} [params.limit] - The maximum number of groups (1..1000).
 * @returns {Promise<{ groups: number, transfers: number, rows: { key: string, transfers: number, users: number, succeeded: number, bytes: number, lastActivityAt: string }[] }>}
 */
export const getSummary = async ({
  direction,
  by = 'directory',
  sort = 'users',
  filter = 'all',
  username,
  search,
  limit = 100,
}) => {
  const parameters = new URLSearchParams();

  parameters.append('by', by);
  parameters.append('sort', sort);
  parameters.append('filter', filter);
  if (username) parameters.append('username', username);
  if (search) parameters.append('search', search);
  parameters.append('limit', limit);

  return (
    await api.get(
      `/transfers/${encodeURIComponent(direction)}s/summary?${parameters}`,
    )
  ).data;
};

/**
 * Fetches every transfer matching a filter as a flat list, in chunks of MAX_PAGE_SIZE.
 * Intended for bulk actions on a bounded subset (e.g. errored downloads), not for display.
 * @param {object} params
 * @param {'download'|'upload'} params.direction - The transfer direction.
 * @param {string} params.filter - The state filter.
 * @returns {Promise<object[]>} The matching transfers.
 */
export const getAllMatching = async ({ direction, filter }) => {
  const files = [];
  let total = Number.POSITIVE_INFINITY;

  while (files.length < total) {
    const page = await getPage({
      direction,
      filter,
      limit: MAX_PAGE_SIZE,
      offset: files.length,
    });
    const before = files.length;

    total = page?.total ?? 0;

    for (const user of page?.users ?? []) {
      for (const directory of user.directories ?? []) {
        for (const file of directory.files ?? []) {
          files.push(file);
        }
      }
    }

    // the set can shrink while paging; stop rather than loop forever
    if (files.length === before) break;
  }

  return files;
};

export const download = ({ username, files = [] }) => {
  return api.post(
    `/transfers/downloads/${encodeURIComponent(username)}`,
    files,
  );
};

/**
 * Enqueues a batch of downloads.
 * @param {object} params
 * @param {string} params.username - The user to download from.
 * @param {{ filename: string, size: number }[]} [params.files] - Files to enqueue.
 * @param {string} [params.id] - Optional batch GUID. Generated server-side if omitted.
 * @param {string} [params.searchId] - Optional GUID of an associated search.
 * @param {{ destination?: string, externalId?: string }} [params.options] - `destination`: path relative to the configured download directory; `externalId`: optional external identifier for the batch.
 * @returns {Promise} Resolves with the axios response.
 *   - 201: all files enqueued successfully
 *   - 200: batch created, but every file failed to enqueue — check `response.data.failures`
 *   - 207: partial — some enqueued, some failed — check `response.data.failures`
 * @throws On error responses:
 *   - 400: validation failure (username/files required; duplicate filenames; id or searchId not a valid GUID)
 *   - 403: forbidden — running as relay agent
 *   - 404: user is offline
 *   - 409: a batch with the supplied id already exists
 *   - 429: a concurrent enqueue request is already in progress
 *   - 500: unexpected server error
 */
export const enqueueBatch = ({
  username,
  files = [],
  id,
  searchId,
  options = { destination: undefined, externalId: undefined },
}) => {
  return api.post('/transfers/downloads/batches', {
    files,
    id,
    options,
    searchId,
    username,
  });
};

export const cancel = ({ direction, username, id, remove = false }) => {
  return api.delete(
    `/transfers/${direction}s/${encodeURIComponent(username)}/${encodeURIComponent(id)}?remove=${remove}`,
  );
};

/**
 * Removes completed transfers server-side.
 * @param {object} params
 * @param {'download'|'upload'} params.direction - The transfer direction.
 * @param {'completed'|'succeeded'|'errored'|'cancelled'} [params.filter] - Which completed transfers to remove; all completed if omitted.
 * @returns {Promise} Resolves with the axios response.
 */
export const clearCompleted = ({ direction, filter }) => {
  const query = filter ? `?filter=${encodeURIComponent(filter)}` : '';

  return api.delete(`/transfers/${direction}s/all/completed${query}`);
};

/**
 * Cancels active transfers server-side.
 * @param {object} params
 * @param {'download'|'upload'} params.direction - The transfer direction.
 * @param {'all'|'queued'|'inprogress'} [params.filter] - Which active transfers to cancel.
 * @returns {Promise} Resolves with the axios response; data is `{ matched, cancelled }`.
 */
export const cancelAll = ({ direction, filter = 'all' }) => {
  return api.post(
    `/transfers/${direction}s/all/cancel?filter=${encodeURIComponent(filter)}`,
  );
};

export const getPlaceInQueue = ({ username, id }) => {
  return api.get(
    `/transfers/downloads/${encodeURIComponent(username)}/${encodeURIComponent(id)}/position`,
  );
};

/*
  states: 

  'Requested'
  'Queued, Remotely'
  'Queued, Locally'
  'Initializing'
  'InProgress'
  'Completed, Succeeded'
  'Completed, Cancelled'
  'Completed, TimedOut'
  'Completed, Errored'
  'Completed, Rejected'
*/
export const isStateRetryable = (state) =>
  state.includes('Completed') && state !== 'Completed, Succeeded';

export const isStateCancellable = (state) =>
  [
    'InProgress',
    'Requested',
    'Queued',
    'Queued, Remotely',
    'Queued, Locally',
    'Initializing',
  ].find((s) => s === state);

export const isStateRemovable = (state) => state.includes('Completed');

const ERRORED_PATTERN = /TimedOut|Errored|Rejected|Aborted/u;

/**
 * Derives per-filter totals from the `counts` (state string -> count) map returned by getPage.
 * @param {{[key: string]: number}} [counts] - Counts keyed by transfer state.
 * @returns {{ all: number, active: number, completed: number, succeeded: number, errored: number, cancelled: number }}
 */
export const getFilterCounts = (counts = {}) => {
  const result = {
    active: 0,
    all: 0,
    cancelled: 0,
    completed: 0,
    errored: 0,
    succeeded: 0,
  };

  for (const [state, count] of Object.entries(counts ?? {})) {
    result.all += count;

    if (!state.includes('Completed')) {
      result.active += count;
    } else {
      result.completed += count;

      if (state.includes('Succeeded')) result.succeeded += count;
      else if (state.includes('Cancelled')) result.cancelled += count;
      else if (ERRORED_PATTERN.test(state)) result.errored += count;
    }
  }

  return result;
};
