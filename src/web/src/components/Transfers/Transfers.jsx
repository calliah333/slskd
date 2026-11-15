import './Transfers.css';
import { poll } from '../../lib/polling';
import * as transfersLibrary from '../../lib/transfers';
import { LoaderSegment, PlaceholderSegment } from '../Shared';
import TransferGroup from './TransferGroup';
import TransfersHeader from './TransfersHeader';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
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

const Transfers = ({ direction, server }) => {
  const [connecting, setConnecting] = useState(true);
  const [page, setPage] = useState(undefined);
  const [filter, setFilter] = useState('all');
  const [offset, setOffset] = useState(0);
  const [refreshToken, setRefreshToken] = useState(0);
  const [previousDirection, setPreviousDirection] = useState(direction);

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

  const key = `${direction}:${filter}:${offset}`;

  useEffect(() => {
    let current = true;

    // poll() never overlaps calls, pauses while the tab is hidden and refetches on return.
    // any change to the request (or an explicit refresh) restarts it immediately; responses
    // from the previous poller are discarded via `current`
    const stop = poll(async () => {
      try {
        const response = await transfersLibrary.getPage({
          direction,
          filter,
          limit: PAGE_SIZE,
          offset,
        });

        if (current) {
          setPage({ ...response, key });
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
  }, [direction, filter, key, offset, refreshToken]);

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

        for (const { filename, size, username } of files) {
          if (!filesByUser.has(username)) filesByUser.set(username, new Map());
          filesByUser.get(username).set(filename, { filename, size });
        }
      }

      // one request per user, sequentially: the enqueue endpoint rejects concurrent requests (429)
      let failed = 0;

      for (const [username, files] of filesByUser) {
        try {
          await transfersLibrary.download({
            files: [...files.values()],
            username,
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

  return (
    <>
      <TransfersHeader
        cancelling={cancelling}
        counts={filterCounts}
        direction={direction}
        onCancelAll={cancelAll}
        onRemoveAll={removeAll}
        onRetryAll={retryAll}
        removing={removing}
        retrying={retrying}
        server={server}
      />
      {filterCounts.all > 0 && (
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
          caption={`No ${filter === 'all' ? '' : `${filter} `}${direction}s to display`}
          icon={direction}
        />
      ) : (
        users.map((user) => (
          <TransferGroup
            direction={direction}
            key={user.username}
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
