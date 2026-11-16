import * as transfers from '../../lib/transfers';
import TransferList from './TransferList';
import React, { memo, useCallback, useEffect, useMemo, useState } from 'react';
import { Button, Card, Icon } from 'semantic-ui-react';

const EMPTY_SELECTION = new Set();

const getFiles = (user) =>
  (user.directories ?? []).flatMap((directory) => directory.files ?? []);

const handleRetry = async (file) => {
  const { filename, size, username } = file;

  try {
    await transfers.download({ files: [{ filename, size }], username });
  } catch (error) {
    console.error(error);
  }
};

const handleFetchPlaceInQueue = async (file) => {
  const { id, username } = file;

  try {
    await transfers.getPlaceInQueue({ id, username });
  } catch (error) {
    console.error(error);
  }
};

const TransferGroup = ({ direction, onFilterUser, onRefresh, user }) => {
  const [isFolded, setIsFolded] = useState(false);
  // transfer ids; replaced (never mutated) on change so memoized rows see the difference
  const [selection, setSelection] = useState(EMPTY_SELECTION);

  // drop selected ids that are no longer on the page (removed, filtered out, paged away)
  useEffect(() => {
    setSelection((current) => {
      if (current.size === 0) {
        return current;
      }

      const next = new Set(
        getFiles(user)
          .filter((file) => current.has(file.id))
          .map((file) => file.id),
      );

      return next.size === current.size ? current : next;
    });
  }, [user]);

  const handleSelectionChange = useCallback((file, checked) => {
    setSelection((current) => {
      if (current.has(file.id) === checked) {
        return current;
      }

      const next = new Set(current);
      if (checked) next.add(file.id);
      else next.delete(file.id);

      return next;
    });
  }, []);

  const handleSelectAll = useCallback((files, checked) => {
    setSelection((current) => {
      const next = new Set(current);

      for (const file of files) {
        if (checked) next.add(file.id);
        else next.delete(file.id);
      }

      return next;
    });
  }, []);

  const selected = useMemo(() => {
    if (selection.size === 0) {
      return [];
    }

    return getFiles(user).filter((file) => selection.has(file.id));
  }, [selection, user]);

  const retrySelected = async () => {
    // one request for the whole selection; the endpoint rejects concurrent requests (429)
    const files = [
      ...new Map(
        selected.map(({ filename, size }) => [filename, { filename, size }]),
      ).values(),
    ];

    try {
      await transfers.download({ files, username: user.username });
    } catch (error) {
      console.error(error);
    }

    onRefresh();
  };

  const cancelSelected = async ({ remove = false } = {}) => {
    try {
      await Promise.all(
        selected.map((file) =>
          transfers.cancel({
            direction,
            id: file.id,
            remove,
            username: user.username,
          }),
        ),
      );
    } catch (error) {
      console.error(error);
    }

    onRefresh();
  };

  const all = selected.length > 1 ? ' Selected' : '';

  const allRetryable =
    direction === 'download' &&
    selected.every((f) => transfers.isStateRetryable(f.state));
  const anyCancellable = selected.some((f) =>
    transfers.isStateCancellable(f.state),
  );
  const allRemovable = selected.every((f) =>
    transfers.isStateRemovable(f.state),
  );

  return (
    <Card
      className="transfer-card"
      raised
    >
      <Card.Content>
        <Card.Header>
          <Icon
            link
            name={isFolded ? 'chevron right' : 'chevron down'}
            onClick={() => setIsFolded((previous) => !previous)}
          />
          {onFilterUser ? (
            <span
              className="transfer-card-username"
              onClick={() => onFilterUser(user.username)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') onFilterUser(user.username);
              }}
              role="button"
              tabIndex={0}
              title={`Show only ${direction}s ${direction === 'upload' ? 'to' : 'from'} ${user.username}`}
            >
              {user.username}
              <Icon
                className="transfer-card-username-filter"
                name="filter"
                size="small"
              />
            </span>
          ) : (
            user.username
          )}
        </Card.Header>
        {user.directories &&
          !isFolded &&
          user.directories.map((directory) => (
            <TransferList
              directoryName={directory.directory}
              files={directory.files ?? []}
              key={directory.directory}
              onPlaceInQueueRequested={handleFetchPlaceInQueue}
              onRetryRequested={handleRetry}
              onSelectAll={handleSelectAll}
              onSelectionChange={handleSelectionChange}
              selection={selection}
            />
          ))}
      </Card.Content>
      {selected.length > 0 && (
        <Card.Content extra>
          <Button.Group>
            {allRetryable && (
              <Button
                color="green"
                content={`Retry${all}`}
                icon="redo"
                onClick={retrySelected}
              />
            )}
            {allRetryable && anyCancellable && <Button.Or />}
            {anyCancellable && (
              <Button
                color="red"
                content={`Cancel${all}`}
                icon="x"
                onClick={() => cancelSelected()}
              />
            )}
            {(allRetryable || anyCancellable) && allRemovable && <Button.Or />}
            {allRemovable && (
              <Button
                content={`Remove${all}`}
                icon="trash alternate"
                onClick={() => cancelSelected({ remove: true })}
              />
            )}
          </Button.Group>
        </Card.Content>
      )}
    </Card>
  );
};

export default memo(TransferGroup);
