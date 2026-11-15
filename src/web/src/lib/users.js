import api from './api';

export const getInfo = ({ username }) => {
  return api.get(`/users/${encodeURIComponent(username)}/info`);
};

export const getStatus = ({ username }) => {
  return api.get(`/users/${encodeURIComponent(username)}/status`);
};

export const getEndpoint = ({ username }) => {
  return api.get(`/users/${encodeURIComponent(username)}/endpoint`);
};

export const browse = async ({ username }) => {
  return (await api.get(`/users/${encodeURIComponent(username)}/browse`)).data;
};

export const getBrowseStatus = ({ username }) => {
  return api.get(`/users/${encodeURIComponent(username)}/browse/status`);
};

// performs a fresh browse, caches the result on the server and returns only
// directory metadata: { directoryCount, fileCount, lockedDirectoryCount,
// lockedFileCount, directories: [{ name, fileCount, locked }] }
export const browseDirectories = async ({ username }) => {
  return (
    await api.get(`/users/${encodeURIComponent(username)}/browse/directories`)
  ).data;
};

// files of a directory (and, if recursive, its descendants) from the server's
// cached browse result; never contacts the peer. any 404 means the cache entry
// is gone and the user has to be browsed again.
// returns { total, truncated, directories: [{ name, fileCount, locked, files }] }
export const getBrowseFiles = async ({
  username,
  directory,
  recursive = false,
  limit,
}) => {
  return (
    await api.post(`/users/${encodeURIComponent(username)}/browse/files`, {
      directory,
      limit,
      recursive,
    })
  ).data;
};

export const getDirectoryContents = async ({ username, directory }) => {
  return (
    await api.post(`/users/${encodeURIComponent(username)}/directory`, {
      directory,
    })
  ).data;
};
