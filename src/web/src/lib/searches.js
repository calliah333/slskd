import api from './api';

export const getAll = async () => {
  return (await api.get('/searches')).data;
};

export const stop = ({ id }) => {
  return api.put(`/searches/${encodeURIComponent(id)}`);
};

export const remove = ({ id }) => {
  return api.delete(`/searches/${encodeURIComponent(id)}`);
};

export const create = ({ id, searchText }) => {
  return api.post('/searches', { id, searchText });
};

export const getStatus = async ({ id, includeResponses = false }) => {
  return (
    await api.get(
      `/searches/${encodeURIComponent(id)}?includeResponses=${includeResponses}`,
    )
  ).data;
};

// fetches a page of the responses matching the given filter (see the filter
// input in Search/Detail for the syntax), sorted by 'uploadSpeed' (fastest
// first) or 'queueLength' (shortest first).  resolves to
// { matchedCount, totalCount, offset, limit, responses }
export const getResponsePage = async ({
  id,
  filter = '',
  sortBy = 'uploadSpeed',
  hideLocked = false,
  hideNoFreeSlots = false,
  hiddenUsernames = [],
  offset = 0,
  limit = 5,
}) => {
  const parameters = new URLSearchParams({
    filter,
    hideLocked: String(hideLocked),
    hideNoFreeSlots: String(hideNoFreeSlots),
    limit: String(limit),
    offset: String(offset),
    sortBy,
  });

  for (const username of hiddenUsernames) {
    parameters.append('hiddenUsernames', username);
  }

  return (
    await api.get(
      `/searches/${encodeURIComponent(id)}/responses/page?${parameters}`,
    )
  ).data;
};
