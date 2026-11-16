import { poll } from '../../lib/polling';
import * as transfersLibrary from '../../lib/transfers';
import { formatBytes, formatDate, getFileName } from '../../lib/util';
import React, { useEffect, useState } from 'react';
import { Dropdown, Loader, Segment, Table } from 'semantic-ui-react';

const LIMIT = 100;

const byOptions = [
  { key: 'directory', text: 'Directories', value: 'directory' },
  { key: 'file', text: 'Files', value: 'file' },
  { key: 'user', text: 'Users', value: 'user' },
];

const sortOptions = [
  { key: 'users', text: 'Most users', value: 'users' },
  { key: 'transfers', text: 'Most transfers', value: 'transfers' },
  { key: 'bytes', text: 'Most data', value: 'bytes' },
  { key: 'recent', text: 'Most recent', value: 'recent' },
];

const KeyCell = ({ by, value }) => {
  if (by === 'file') {
    const name = getFileName(value);
    const directory = value.slice(0, value.length - name.length);

    return (
      <>
        <span className="transfer-breakdown-muted">{directory}</span>
        {name}
      </>
    );
  }

  return value || <i>(root)</i>;
};

/**
 * Aggregates the transfers matching the current filter and query by directory, file, or user;
 * e.g. "which of my albums do the most people download" or "what did this user take".
 * Clicking a row narrows the transfer list to it via onSelect.
 */
const TransferBreakdown = ({
  direction,
  filter,
  onSelect,
  search,
  username,
}) => {
  const [by, setBy] = useState('directory');
  const [sort, setSort] = useState('users');
  const [result, setResult] = useState(undefined);
  const [error, setError] = useState(undefined);

  const key = `${direction}:${filter}:${username}:${search}:${by}:${sort}`;

  useEffect(() => {
    let current = true;

    // aggregates scan every matching row, so this refreshes slowly rather than every second
    const stop = poll(async () => {
      try {
        const response = await transfersLibrary.getSummary({
          by,
          direction,
          filter,
          limit: LIMIT,
          search,
          sort,
          username,
        });

        if (current) {
          setResult({ ...response, key });
          setError(undefined);
        }
      } catch (fetchError) {
        if (current) {
          setError(fetchError?.response?.data ?? fetchError?.message);
        }
      }
    }, 15_000);

    return () => {
      current = false;
      stop();
    };
  }, [by, direction, filter, key, search, sort, username]);

  const loading = result?.key !== key;
  const rows = result?.rows ?? [];
  const peopleLabel = direction === 'upload' ? 'Users' : 'Peers';

  return (
    <Segment className="transfer-breakdown">
      <div className="transfer-breakdown-controls">
        <span>
          Group by{' '}
          <Dropdown
            inline
            onChange={(_, { value }) => setBy(value)}
            options={byOptions}
            value={by}
          />
        </span>
        <span>
          sorted by{' '}
          <Dropdown
            inline
            onChange={(_, { value }) => setSort(value)}
            options={sortOptions}
            value={sort}
          />
        </span>
        <Loader
          active={loading}
          inline
          size="tiny"
        />
        {result && (
          <span className="transfer-breakdown-muted transfer-breakdown-total">
            {result.groups > rows.length
              ? `Top ${rows.length.toLocaleString()} of ${result.groups.toLocaleString()} groups`
              : `${result.groups.toLocaleString()} groups`}
            {` · ${result.transfers.toLocaleString()} transfers`}
          </span>
        )}
      </div>
      {error && <div className="transfer-breakdown-error">{String(error)}</div>}
      {rows.length > 0 && (
        <div className="transfer-breakdown-table">
          <Table
            compact
            selectable
            size="small"
            unstackable
          >
            <Table.Header>
              <Table.Row>
                <Table.HeaderCell>
                  {byOptions.find((option) => option.value === by)?.text}
                </Table.HeaderCell>
                <Table.HeaderCell textAlign="right">
                  {peopleLabel}
                </Table.HeaderCell>
                <Table.HeaderCell textAlign="right">Transfers</Table.HeaderCell>
                <Table.HeaderCell textAlign="right">Succeeded</Table.HeaderCell>
                <Table.HeaderCell textAlign="right">Data</Table.HeaderCell>
                <Table.HeaderCell>Last activity</Table.HeaderCell>
              </Table.Row>
            </Table.Header>
            <Table.Body>
              {rows.map((row) => (
                <Table.Row
                  className="transfer-breakdown-row"
                  key={row.key}
                  onClick={() => onSelect({ by, key: row.key })}
                  title={`Show these ${direction}s`}
                >
                  <Table.Cell className="transfer-breakdown-key">
                    <KeyCell
                      by={by}
                      value={row.key}
                    />
                  </Table.Cell>
                  <Table.Cell textAlign="right">
                    {row.users.toLocaleString()}
                  </Table.Cell>
                  <Table.Cell textAlign="right">
                    {row.transfers.toLocaleString()}
                  </Table.Cell>
                  <Table.Cell textAlign="right">
                    {row.succeeded.toLocaleString()}
                  </Table.Cell>
                  <Table.Cell textAlign="right">
                    {formatBytes(row.bytes)}
                  </Table.Cell>
                  <Table.Cell>{formatDate(row.lastActivityAt)}</Table.Cell>
                </Table.Row>
              ))}
            </Table.Body>
          </Table>
        </div>
      )}
      {!loading && !error && rows.length === 0 && (
        <div className="transfer-breakdown-muted">Nothing matches</div>
      )}
    </Segment>
  );
};

export default TransferBreakdown;
