import '../System.css';
import { memo } from 'react';
import { Table } from 'semantic-ui-react';

const abbreviations = {
  Debug: 'DBG',
  Error: 'ERR',
  Fatal: 'FTL',
  Information: 'INF',
  Verbose: 'VRB',
  Warning: 'WRN',
};

export const formatTimestamp = (timestamp) => {
  const date = new Date(timestamp);
  return `${date.getHours().toString().padStart(2, '0')}:${date.getMinutes().toString().padStart(2, '0')}:${date.getSeconds().toString().padStart(2, '0')}`; // eslint-disable-line max-len
};

// rows are immutable once received; memoized so a new live line renders one row, not all of them
const LogRow = memo(({ log }) => (
  <Table.Row
    disabled={log.level === 'Debug' || log.level === 'Verbose'}
    negative={log.level === 'Error' || log.level === 'Fatal'}
    warning={log.level === 'Warning'}
  >
    <Table.Cell>{formatTimestamp(log.timestamp)}</Table.Cell>
    <Table.Cell>{abbreviations[log.level] || log.level}</Table.Cell>
    <Table.Cell className="logs-table-message">{log.message}</Table.Cell>
  </Table.Row>
));

const LogTable = ({ emptyMessage = 'No logs', logs }) => (
  <Table
    className="logs-table"
    compact="very"
  >
    <Table.Header>
      <Table.Row>
        <Table.HeaderCell>Timestamp</Table.HeaderCell>
        <Table.HeaderCell>Level</Table.HeaderCell>
        <Table.HeaderCell>Message</Table.HeaderCell>
      </Table.Row>
    </Table.Header>
    <Table.Body className="logs-table-body">
      {logs?.length === 0 ? (
        <Table.Row>
          <Table.Cell
            colSpan={99}
            style={{
              opacity: 0.5,
              padding: '10px !important',
              textAlign: 'center',
            }}
          >
            {emptyMessage}
          </Table.Cell>
        </Table.Row>
      ) : (
        logs.map((log) => (
          <LogRow
            key={log.id}
            log={log}
          />
        ))
      )}
    </Table.Body>
  </Table>
);

export default LogTable;
