import './Transfers.css';
import { poll } from '../../lib/polling';
import * as transfersLibrary from '../../lib/transfers';
import { LoaderSegment, PlaceholderSegment } from '../Shared';
import TransferBreakdown from './TransferBreakdown';
import TransferGroup from './TransferGroup';
import TransfersHeader from './TransfersHeader';
import TransfersSearch from './TransfersSearch';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useHistory, useLocation } from 'react-router-dom';
import { toast } from 'react-toastify';
import { Label, Loader, Menu, Pagination } from 'semantic-ui-react';

const PAGE_SIZE = transfersLibrary.PAGE_SIZE;

const filterOptions = [
  { text: 'All', value: 'all' },
  { text: 'Active', value: 'active' },
  { text: 'Succeeded', value: 'succeeded' },
  { text: 'Errored', value: 'errored' },
  { text: 'Cancelled', value: 'cancelled' },
];

const toastError = (error) => {
  console.error(error);
  toast.error(error?.response?.data ?? error?.message ?? error);
};

// typing in the search box updates the URL (and so the query) after this pause
const SEARCH_DEBOUNCE_MS = 350;

const Transfers = ({ direction, server }) => {
  const history = useHistory();
  const location = useLocation();

  // the user and search narrowing live in the URL, so a filtered view can be bookmarked or shared,
  // and the browser's back button undoes a drill-down from the breakdown
  const urlParameters = useMemo(
    () => new URLSearchParams(location.search),
    [location.search],
  );
  const username = urlParameters.get('user') ?? '';
  const search = urlParameters.get('q') ?? '';
  const showBreakdown = urlParameters.get('view') === 'breakdown';

  const [connecting, setConnecting] = useState(true);
  const [page, setPage] = useState(undefined);
  const [filter, setFilter] = useState('all');
  const [offset, setOffset] = useState(0);
  const [refreshToken, setRefreshToken] = useState(0);
  const [previousDirection, setPreviousDirection] = useState(direction);
  const [searchText, setSearchText] = useState(search);
  const [previousSearch, setPreviousSearch] = useState(search);
  const [previousUsername, setPreviousUsername] = useState(username);

  const [retrying, setRetrying] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [removing, setRemoving] = useState(false);

  if (direction !== previousDirection) {
    // the same instance serves both routes; reset the instant the prop changes (not after
    // the next effect) so the old direction's rows never flash under the new direction
    setPreviousDirection(direction);
    setConnecting(true);
    setOffset(0);
  }

  if (search !== previousSearch) {
    // the URL changed underneath the box (a breakdown row, back button, route change); follow it
    setPreviousSearch(search);
    setSearchText(search);
    setOffset(0);
  }

  if (username !== previousUsername) {
    setPreviousUsername(username);
    setOffset(0);
  }

  const updateUrl = useCallback(
    (changes, { push = false } = {}) => {
      const next = new URLSearchParams(location.search);

      for (const [name, value] of Object.entries(changes)) {
        if (value) next.set(name, value);
        else next.delete(name);
      }

      const target = { pathname: location.pathname, search: next.toString() };

      if (push) history.push(target);
      else history.replace(target);
    },
    [history, location.pathname, location.search],
  );

  useEffect(() => {
    if (searchText.trim() === search) {
      return undefined;
    }

    const timer = setTimeout(
      () => updateUrl({ q: searchText.trim() }),
      SEARCH_DEBOUNCE_MS,
    );

    return () => clearTimeout(timer);
  }, [search, searchText, updateUrl]);

  const filterByUser = useCallback(
    (value) => {
      setOffset(0);
      updateUrl({ user: value }, { push: true });
    },
    [updateUrl],
  );

  const selectBreakdownRow = useCallback(
    ({ by, key: value }) => {
      setOffset(0);

      // directories match their files (and any subdirectories) by including the trailing separator
      updateUrl(
        by === 'user'
          ? { user: value, view: undefined }
          : { q: by === 'directory' ? `${value}\\` : value, view: undefined },
        { push: true },
      );
    },
    [updateUrl],
  );

  const key = `${direction}:${filter}:${username}:${search}:${offset}`;

  useEffect(() => {
    let current = true;
    // the ETag of this poller's last response; this request only (a new key restarts the effect)
    let etag;
    // a plain function so the post-await assignment isn't flagged as a race; polls never overlap
    const remember = (value) => {
      etag = value;
    };

    // poll() never overlaps calls, pauses while the tab is hidden and refetches on return.
    // any change to the request (or an explicit refresh) restarts it immediately; responses
    // from the previous poller are discarded via `current`. while nothing in the transfers
    // database changes the server answers 304, so idle pages cost no query, payload, or render
    const stop = poll(async () => {
      try {
        const response = await transfersLibrary.getPageIfChanged(
          {
            direction,
            filter,
            limit: PAGE_SIZE,
            offset,
            search,
            username,
          },
          etag,
        );

        if (current) {
          remember(response.etag);

          if (response.changed) {
            setPage({ ...response.data, key });
          }
        }
      } catch (error) {
        if (current) {
          toastError(error);
        }
      } finally {
        if (current) {
          setConnecting(false);
        }
      }
    }, 1_000);

    return () => {
      current = false;
      stop();
    };
  }, [direction, filter, key, offset, refreshToken, search, username]);

  const refresh = useCallback(() => setRefreshToken((token) => token + 1), []);

  const loading = page?.key !== key;
  const total = page?.total ?? 0;
  const users = useMemo(() => page?.users ?? [], [page]);
  const filterCounts = useMemo(
    () => transfersLibrary.getFilterCounts(page?.counts),
    [page?.counts],
  );
  const rows = useMemo(
    () =>
      users.reduce(
        (count, user) =>
          count +
          (user.directories ?? []).reduce(
            (directoryCount, directory) =>
              directoryCount + (directory.files?.length ?? 0),
            0,
          ),
        0,
      ),
    [users],
  );
  const totalPages = Math.ceil(total / PAGE_SIZE);

  // the result set can shrink under the current page (e.g. after a removal); snap to the last page
  useEffect(() => {
    if (!loading && offset > 0 && offset >= total) {
      setOffset(Math.max(0, totalPages - 1) * PAGE_SIZE);
    }
  }, [loading, offset, total, totalPages]);

  const changeFilter = (value) => {
    if (value !== filter) {
      setFilter(value);
      setOffset(0);
    }
  };

  const retryAll = async (option) => {
    setRetrying(true);

    try {
      const filters = option === 'all' ? ['errored', 'cancelled'] : [option];
      const filesByUser = new Map();

      for (const retryFilter of filters) {
        const files = await transfersLibrary.getAllMatching({
          direction,
          filter: retryFilter,
        });

        for (const { filename, size, username: owner } of files) {
          if (!filesByUser.has(owner)) filesByUser.set(owner, new Map());
          filesByUser.get(owner).set(filename, { filename, size });
        }
      }

      // one request per user, sequentially: the enqueue endpoint rejects concurrent requests (429)
      let failed = 0;

      for (const [owner, files] of filesByUser) {
        try {
          await transfersLibrary.download({
            files: [...files.values()],
            username: owner,
          });
        } catch (error) {
          failed++;
          console.error(error);
        }
      }

      if (failed > 0) {
        toast.error(
          `Failed to retry downloads from ${failed} of ${filesByUser.size} users`,
        );
      }
    } catch (error) {
      toastError(error);
    } finally {
      setRetrying(false);
      refresh();
    }
  };

  const cancelAll = async (option) => {
    setCancelling(true);

    try {
      await transfersLibrary.cancelAll({ direction, filter: option });
    } catch (error) {
      toastError(error);
    } finally {
      setCancelling(false);
      refresh();
    }
  };

  const removeAll = async (option) => {
    setRemoving(true);

    try {
      await transfersLibrary.clearCompleted({ direction, filter: option });
    } catch (error) {
      toastError(error);
    } finally {
      setRemoving(false);
      refresh();
    }
  };

  if (connecting) {
    return <LoaderSegment />;
  }

  const pagination =
    totalPages > 1 ? (
      <div className="transfers-pagination">
        <Pagination
          activePage={Math.floor(offset / PAGE_SIZE) + 1}
          boundaryRange={1}
          onPageChange={(event, data) =>
            setOffset((data.activePage - 1) * PAGE_SIZE)
          }
          siblingRange={1}
          size="mini"
          totalPages={totalPages}
        />
      </div>
    ) : null;

  const narrowed = Boolean(username || search);
  const noun = `${filter === 'all' ? '' : `${filter} `}${direction}s`;

  return (
    <>
      <TransfersHeader
        cancelling={cancelling}
        counts={filterCounts}
        direction={direction}
        narrowed={narrowed}
        onCancelAll={cancelAll}
        onRemoveAll={removeAll}
        onRetryAll={retryAll}
        removing={removing}
        retrying={retrying}
        server={server}
      >
        <TransfersSearch
          direction={direction}
          onClearUser={() => filterByUser(undefined)}
          onSearchTextChange={setSearchText}
          onToggleBreakdown={() =>
            updateUrl(
              { view: showBreakdown ? undefined : 'breakdown' },
              { push: true },
            )
          }
          searchText={searchText}
          showBreakdown={showBreakdown}
          username={username}
        />
      </TransfersHeader>
      {showBreakdown && (
        <TransferBreakdown
          direction={direction}
          filter={filter}
          onSelect={selectBreakdownRow}
          search={search}
          username={username}
        />
      )}
      {(filterCounts.all > 0 || narrowed) && (
        <Menu
          className="transfers-filter-menu"
          pointing
          secondary
          size="small"
        >
          {filterOptions.map(({ text, value }) => (
            <Menu.Item
              active={filter === value}
              key={value}
              onClick={() => changeFilter(value)}
            >
              {text}
              <Label size="tiny">{filterCounts[value].toLocaleString()}</Label>
            </Menu.Item>
          ))}
          <Menu.Menu position="right">
            <Menu.Item>
              <Loader
                active={loading}
                inline
                size="tiny"
              />
              <span className="transfers-showing">
                {rows === 0
                  ? `Showing 0 of ${total.toLocaleString()}`
                  : `Showing ${(offset + 1).toLocaleString()}–${(
                      offset + rows
                    ).toLocaleString()} of ${total.toLocaleString()}`}
              </span>
            </Menu.Item>
          </Menu.Menu>
        </Menu>
      )}
      {pagination}
      {users.length === 0 ? (
        <PlaceholderSegment
          caption={narrowed ? `No ${noun} match` : `No ${noun} to display`}
          icon={direction}
        />
      ) : (
        users.map((user) => (
          <TransferGroup
            direction={direction}
            key={user.username}
            onFilterUser={username ? undefined : filterByUser}
            onRefresh={refresh}
            user={user}
          />
        ))
      )}
      {pagination}
    </>
  );
};

export default Transfers;
