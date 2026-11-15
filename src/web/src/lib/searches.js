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

export const getResponses = async ({ id }) => {
  const response = (
    await api.get(`/searches/${encodeURIComponent(id)}/responses`)
  ).data;

  if (!Array.isArray(response)) {
    console.warn('got non-array response from searches API', response);
    return undefined;
  }

  return response;
};

const getNthMatch = (string, regex, n) => {
  const match = string.match(regex);

  if (match) {
    return Number.parseInt(match[n], 10);
  }

  return undefined;
};

export const parseFiltersFromString = (string) => {
  const filters = {
    exclude: [],
    include: [],
    isCBR: false,
    isLossless: false,
    isLossy: false,
    isVBR: false,
    minBitDepth: 0,
    minBitRate: 0,
    minFilesInFolder: 0,
    minFileSize: 0,
    minLength: 0,
  };

  filters.minBitRate =
    getNthMatch(string, /(minbr|minbitrate):(\d+)/iu, 2) || filters.minBitRate;
  filters.minBitDepth =
    getNthMatch(string, /(minbd|minbitdepth):(\d+)/iu, 2) ||
    filters.minBitDepth;
  filters.minFileSize =
    getNthMatch(string, /(minfs|minfilesize):(\d+)/iu, 2) ||
    filters.minFileSize;
  filters.minLength =
    getNthMatch(string, /(minlen|minlength):(\d+)/iu, 2) || filters.minLength;
  filters.minFilesInFolder =
    getNthMatch(string, /(minfif|minfilesinfolder):(\d+)/iu, 2) ||
    filters.minFilesInFolder;

  filters.isVBR = Boolean(/isvbr/iu.test(string));
  filters.isCBR = Boolean(/iscbr/iu.test(string));
  filters.isLossless = Boolean(/islossless/iu.test(string));
  filters.isLossy = Boolean(/islossy/iu.test(string));

  const terms = string
    .toLowerCase()
    .split(' ')
    .filter(
      (term) =>
        !term.includes(':') &&
        term !== 'isvbr' &&
        term !== 'iscbr' &&
        term !== 'islossless' &&
        term !== 'islossy',
    );

  filters.include = terms.filter((term) => !term.startsWith('-'));
  filters.exclude = terms
    .filter((term) => term.startsWith('-'))
    .map((term) => term.slice(1));

  return filters;
};

export const filterResponse = ({
  filters = {
    exclude: [],
    include: [],
    isCBR: false,
    isLossless: false,
    isLossy: false,
    isVBR: false,
    minBitDepth: 0,
    minBitRate: 0,
    minFileSize: 0,
    minLength: 0,
  },
  response = {
    files: [],
    lockedFiles: [],
  },
}) => {
  const { files = [], lockedFiles = [] } = response;

  if (
    response.fileCount + response.lockedFileCount <
    filters.minFilesInFolder
  ) {
    return { ...response, files: [] };
  }

  const {
    isCBR,
    isVBR,
    isLossless,
    isLossy,
    minBitRate,
    minBitDepth,
    minFileSize,
    minLength,
    include = [],
    exclude = [],
  } = filters;
  // an empty include term matches everything (the parser yields one for an empty string)
  const includeTerms = include.filter((term) => term !== '');
  const hasTerms = includeTerms.length > 0 || exclude.length > 0;

  const filterFiles = (filesToFilter) =>
    filesToFilter.filter((file) => {
      const {
        bitRate,
        size,
        length,
        filename,
        sampleRate,
        bitDepth,
        isVariableBitRate,
      } = file;

      if (isCBR && (isVariableBitRate === undefined || isVariableBitRate))
        return false;
      if (isVBR && (isVariableBitRate === undefined || !isVariableBitRate))
        return false;
      if (isLossless && (!sampleRate || !bitDepth)) return false;
      if (isLossy && (sampleRate || bitDepth)) return false;
      if (bitRate < minBitRate) return false;
      if (bitDepth < minBitDepth) return false;
      if (size < minFileSize) return false;
      if (length < minLength) return false;

      if (hasTerms) {
        // lowercase once per file rather than once per term
        const lowerFilename = filename.toLowerCase();

        if (!includeTerms.every((term) => lowerFilename.includes(term)))
          return false;
        if (exclude.some((term) => lowerFilename.includes(term))) return false;
      }

      return true;
    });

  const filteredFiles = filterFiles(files);
  const filteredLockedFiles = filterFiles(lockedFiles);

  // nothing was filtered out and the counts already agree: return the original
  // object so callers can skip re-rendering responses that didn't change
  if (
    filteredFiles.length === files.length &&
    filteredLockedFiles.length === lockedFiles.length &&
    response.files === files &&
    response.lockedFiles === lockedFiles &&
    response.fileCount === files.length &&
    response.lockedFileCount === lockedFiles.length
  ) {
    return response;
  }

  return {
    ...response,
    fileCount: filteredFiles.length,
    files: filteredFiles,
    lockedFileCount: filteredLockedFiles.length,
    lockedFiles: filteredLockedFiles,
  };
};
