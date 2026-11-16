import api from './api';

const notModified = 304;

// GETs `url` unless the server's copy still matches `etag` (the ETag of a
// previous response from the same url), in which case the server answers
// 304 Not Modified with no body.
// resolves to { changed, etag, data }: when unchanged, `changed` is false,
// `data` is undefined, and the caller should keep what it already has.
// servers (or proxies) that don't send an ETag simply always return the data.
export const getIfChanged = async (url, etag) => {
  const response = await api.get(url, {
    headers: etag ? { 'If-None-Match': etag } : {},
    validateStatus: (status) =>
      (status >= 200 && status < 300) || status === notModified,
  });

  if (response.status === notModified) {
    return { changed: false, data: undefined, etag };
  }

  return {
    changed: true,
    data: response.data,
    etag: response.headers?.etag ?? undefined,
  };
};
