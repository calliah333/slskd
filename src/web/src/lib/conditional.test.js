import api from './api';
import { getIfChanged } from './conditional';

jest.mock('./api', () => ({
  __esModule: true,
  default: { get: jest.fn() },
}));

describe('getIfChanged', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('sends no If-None-Match without an etag and returns the data and new etag', async () => {
    expect.hasAssertions();
    api.get.mockResolvedValue({
      data: [1],
      headers: { etag: '"2"' },
      status: 200,
    });

    const result = await getIfChanged('/thing');

    expect(api.get.mock.calls[0][1].headers).toStrictEqual({});
    expect(result).toStrictEqual({ changed: true, data: [1], etag: '"2"' });
  });

  it('sends If-None-Match and keeps the etag when not modified', async () => {
    expect.hasAssertions();
    api.get.mockResolvedValue({ data: '', headers: {}, status: 304 });

    const result = await getIfChanged('/thing', '"1"');

    expect(api.get.mock.calls[0][1].headers).toStrictEqual({
      'If-None-Match': '"1"',
    });
    expect(result).toStrictEqual({
      changed: false,
      data: undefined,
      etag: '"1"',
    });
  });

  it('accepts 304 as a successful status', async () => {
    expect.hasAssertions();
    api.get.mockResolvedValue({ data: [], headers: {}, status: 200 });

    await getIfChanged('/thing', '"1"');
    const { validateStatus } = api.get.mock.calls[0][1];

    expect([200, 304].map(validateStatus)).toStrictEqual([true, true]);
    expect([302, 404, 500].map(validateStatus)).toStrictEqual([
      false,
      false,
      false,
    ]);
  });
});
