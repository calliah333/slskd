// lazily loaded chunks are fetched relative to webpack's public path. the build uses a relative one
// ("homepage": ".") so it works under any urlBase, but relative chunk URLs resolve against the page's
// URL, so on a nested route like /system/logs a chunk is requested from /system/static/js/... and
// fails. resolve them against the root the server injected instead. this module must be imported
// before anything that loads a chunk.
import { urlBase } from './config';

// eslint-disable-next-line no-undef, camelcase
__webpack_public_path__ = `${urlBase}/`;
