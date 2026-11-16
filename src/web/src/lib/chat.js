import api from './api';
import { getIfChanged } from './conditional';

export const getAll = async () => {
  return (await api.get('/conversations')).data;
};

// the conversation (including its most recent messages), unless it is unchanged
// since the response that carried `etag`; resolves to { changed, data, etag }
// (see getIfChanged)
export const get = ({ username, etag }) => {
  return getIfChanged(`/conversations/${encodeURIComponent(username)}`, etag);
};

export const acknowledge = ({ username }) => {
  return api.put(`/conversations/${encodeURIComponent(username)}`);
};

export const send = ({ username, message }) => {
  return api.post(
    `/conversations/${encodeURIComponent(username)}`,
    JSON.stringify(message),
  );
};

export const remove = ({ username }) => {
  return api.delete(`/conversations/${encodeURIComponent(username)}`);
};
