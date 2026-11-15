import api from './api';
import * as transfers from './transfers';

jest.mock('./api', () => ({
  __esModule: true,
  default: { delete: jest.fn(), get: jest.fn(), post: jest.fn() },
}));

const parametersOf = (url) => new URLSearchParams(url.split('?')[1] ?? '');

const makePage = ({ files, total }) => ({
  counts: {},
  total,
  users: [
    {
      directories: [{ directory: 'dir', fileCount: files.length, files }],
      username: 'user',
    },
  ],
});

describe('transfers', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('getPage', () => {
    it('calls the paged endpoint for the direction', async () => {
      expect.hasAssertions();
      api.get.mockResolvedValue({ data: { users: [] } });
      await transfers.getPage({ direction: 'upload' });
      expect(api.get.mock.calls[0][0]).toContain('/transfers/uploads/page?');
    });

    it('defaults filter, offset and limit', async () => {
      expect.hasAssertions();
      api.get.mockResolvedValue({ data: {} });
      await transfers.getPage({ direction: 'download' });
      const parameters = parametersOf(api.get.mock.calls[0][0]);
      expect(parameters.get('filter')).toBe('all');
      expect(parameters.get('offset')).toBe('0');
      expect(parameters.get('limit')).toBe(String(transfers.PAGE_SIZE));
    });

    it('passes filter, offset and limit', async () => {
      expect.hasAssertions();
      api.get.mockResolvedValue({ data: {} });
      await transfers.getPage({
        direction: 'download',
        filter: 'errored',
        limit: 50,
        offset: 100,
      });
      const parameters = parametersOf(api.get.mock.calls[0][0]);
      expect(parameters.get('filter')).toBe('errored');
      expect(parameters.get('offset')).toBe('100');
      expect(parameters.get('limit')).toBe('50');
    });

    it('returns the response body', async () => {
      expect.hasAssertions();
      const data = { total: 1, users: [] };
      api.get.mockResolvedValue({ data });
      await expect(transfers.getPage({ direction: 'download' })).resolves.toBe(
        data,
      );
    });
  });

  describe('getAllMatching', () => {
    it('pages through results in MAX_PAGE_SIZE chunks until total is reached', async () => {
      expect.hasAssertions();
      api.get
        .mockResolvedValueOnce({
          data: makePage({ files: [{ id: 'a' }, { id: 'b' }], total: 3 }),
        })
        .mockResolvedValueOnce({
          data: makePage({ files: [{ id: 'c' }], total: 3 }),
        });

      const files = await transfers.getAllMatching({
        direction: 'download',
        filter: 'errored',
      });

      expect(files.map((f) => f.id)).toStrictEqual(['a', 'b', 'c']);
      expect(api.get).toHaveBeenCalledTimes(2);
      const first = parametersOf(api.get.mock.calls[0][0]);
      const second = parametersOf(api.get.mock.calls[1][0]);
      expect(first.get('limit')).toBe(String(transfers.MAX_PAGE_SIZE));
      expect(first.get('offset')).toBe('0');
      expect(first.get('filter')).toBe('errored');
      expect(second.get('offset')).toBe('2');
    });

    it('stops when a page comes back empty', async () => {
      expect.hasAssertions();
      api.get
        .mockResolvedValueOnce({
          data: makePage({ files: [{ id: 'a' }], total: 10 }),
        })
        .mockResolvedValueOnce({ data: makePage({ files: [], total: 10 }) });

      const files = await transfers.getAllMatching({
        direction: 'download',
        filter: 'all',
      });

      expect(files).toHaveLength(1);
      expect(api.get).toHaveBeenCalledTimes(2);
    });
  });

  describe('clearCompleted', () => {
    it('omits filter when not provided', async () => {
      expect.hasAssertions();
      await transfers.clearCompleted({ direction: 'upload' });
      expect(api.delete).toHaveBeenCalledWith(
        '/transfers/uploads/all/completed',
      );
    });

    it('appends filter when provided', async () => {
      expect.hasAssertions();
      await transfers.clearCompleted({
        direction: 'download',
        filter: 'succeeded',
      });
      expect(api.delete).toHaveBeenCalledWith(
        '/transfers/downloads/all/completed?filter=succeeded',
      );
    });
  });

  describe('cancelAll', () => {
    it('posts to the cancel endpoint with the filter', async () => {
      expect.hasAssertions();
      await transfers.cancelAll({ direction: 'download', filter: 'queued' });
      expect(api.post).toHaveBeenCalledWith(
        '/transfers/downloads/all/cancel?filter=queued',
      );
    });

    it('defaults the filter to all', async () => {
      expect.hasAssertions();
      await transfers.cancelAll({ direction: 'upload' });
      expect(api.post).toHaveBeenCalledWith(
        '/transfers/uploads/all/cancel?filter=all',
      );
    });
  });

  describe('getFilterCounts', () => {
    it('returns zeros for missing counts', () => {
      expect(transfers.getFilterCounts(undefined)).toStrictEqual({
        active: 0,
        all: 0,
        cancelled: 0,
        completed: 0,
        errored: 0,
        succeeded: 0,
      });
    });

    it('buckets states into filters', () => {
      expect(
        transfers.getFilterCounts({
          'Completed, Aborted': 1,
          'Completed, Cancelled': 3,
          'Completed, Errored': 6_049,
          'Completed, Rejected': 14_283,
          'Completed, Succeeded': 503_458,
          'Completed, TimedOut': 645,
          InProgress: 2,
          Initializing: 1,
          'Queued, Locally': 56,
        }),
      ).toStrictEqual({
        active: 59,
        all: 524_498,
        cancelled: 3,
        completed: 524_439,
        errored: 20_978,
        succeeded: 503_458,
      });
    });
  });
});
