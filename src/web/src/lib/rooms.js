import api from './api';
import { getIfChanged } from './conditional';

const toArray = (response) => {
  if (!Array.isArray(response)) {
    console.warn('got non-array response from rooms API', response);
    return undefined;
  }

  return response;
};

export const getAvailable = async () => {
  return toArray((await api.get('/rooms/available')).data);
};

export const getJoined = async () => {
  return toArray((await api.get('/rooms/joined')).data);
};

// fetches `url` unless unchanged since the response that carried `etag`;
// resolves to { changed, data, etag } (see getIfChanged)
const getArrayIfChanged = async (url, etag) => {
  const result = await getIfChanged(url, etag);

  return result.changed ? { ...result, data: toArray(result.data) } : result;
};

export const getMessages = ({ roomName, etag }) => {
  return getArrayIfChanged(
    `/rooms/joined/${encodeURIComponent(roomName)}/messages`,
    etag,
  );
};

export const getUsers = ({ roomName, etag }) => {
  return getArrayIfChanged(
    `/rooms/joined/${encodeURIComponent(roomName)}/users`,
    etag,
  );
};

export const join = async ({ roomName }) => {
  return api.post('/rooms/joined', roomName);
};

export const leave = async ({ roomName }) => {
  return api.delete(`/rooms/joined/${encodeURIComponent(roomName)}`);
};

export const sendMessage = async ({ roomName, message }) => {
  return api.post(
    `/rooms/joined/${encodeURIComponent(roomName)}/messages`,
    JSON.stringify(message),
  );
};
