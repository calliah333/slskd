import { formatBytes, formatBytesAsUnit, getFileName } from '../../lib/util';
import TransferDetails from './TransferDetails';
import React, { memo, useMemo, useState } from 'react';
import {
  Button,
  Checkbox,
  Header,
  Icon,
  List,
  Popup,
  Progress,
  Table,
} from 'semantic-ui-react';

const getColor = (state) => {
  switch (state) {
    case 'InProgress':
      return 'blue';
    case 'Completed, Succeeded':
      return 'green';
    case 'Requested':
    case 'Queued, Locally':
    case 'Queued, Remotely':
    case 'Queued':
      return undefined;
    case 'Initializing':
      return 'teal';
    default:
      return 'red';
  }
};

const isRetryableState = (state) => getColor(state) === 'red';
const isQueuedState = (state) => state.includes('Queued');

const formatBytesTransferred = ({ size, transferred }) => {
  const [s, sExtension] = formatBytes(size, 1).split(' ');
  const t = formatBytesAsUnit(transferred, sExtension, 1);

  return `${t}/${s} ${sExtension}`;
};

const progressStyle = { margin: 0 };
const downloadButtonStyle = { cursor: '', margin: 0, padding: 7 };
const uploadButtonStyle = { cursor: 'unset', margin: 0, padding: 7 };
const sizeStyle = {
  alignItems: 'center',
  display: 'flex',
  justifyContent: 'space-between',
};
const popupStyle = { maxWidth: '600px' };

// the fields a row displays (directly or via TransferDetails) that change while a transfer is alive.
// id, username, filename, size and direction never change for a given id.
const ROW_FIELDS = [
  'id',
  'state',
  'bytesTransferred',
  'percentComplete',
  'placeInQueue',
  'attempts',
  'averageSpeed',
];

const isSameTransfer = (a, b) =>
  a === b || ROW_FIELDS.every((field) => a[field] === b[field]);

// polls replace every transfer object, so compare by value: only rows whose
// visible state actually changed re-render
const areRowPropsEqual = (previous, next) =>
  previous.selected === next.selected &&
  previous.onSelectionChange === next.onSelectionChange &&
  previous.onRetryRequested === next.onRetryRequested &&
  previous.onPlaceInQueueRequested === next.onPlaceInQueueRequested &&
  isSameTransfer(previous.file, next.file);

// skip the whole directory (table chrome included) when none of its rows changed
const areListPropsEqual = (previous, next) =>
  previous.directoryName === next.directoryName &&
  previous.selection === next.selection &&
  previous.onSelectAll === next.onSelectAll &&
  previous.onSelectionChange === next.onSelectionChange &&
  previous.onRetryRequested === next.onRetryRequested &&
  previous.onPlaceInQueueRequested === next.onPlaceInQueueRequested &&
  previous.files.length === next.files.length &&
  previous.files.every((file, index) =>
    isSameTransfer(file, next.files[index]),
  );

const TransferListRow = memo(
  ({
    file,
    onPlaceInQueueRequested,
    onRetryRequested,
    onSelectionChange,
    selected,
  }) => {
    const {
      attempts,
      bytesTransferred,
      direction,
      filename,
      percentComplete,
      placeInQueue,
      size,
      state,
    } = file;
    const color = getColor(state);
    const isDownload = direction === 'Download';

    const handleClick = () => {
      if (isDownload) {
        if (isRetryableState(state)) {
          return onRetryRequested(file);
        }

        if (isQueuedState(state)) {
          return onPlaceInQueueRequested(file);
        }
      }

      return undefined;
    };

    return (
      <Table.Row>
        <Table.Cell className="transferlist-selector">
          <Checkbox
            checked={selected}
            fitted
            onChange={(event, data) => onSelectionChange(file, data.checked)}
          />
        </Table.Cell>
        <Table.Cell className="transferlist-filename">
          {getFileName(filename)}
        </Table.Cell>
        <Table.Cell className="transferlist-progress">
          {state === 'InProgress' ? (
            <Progress
              color={color}
              percent={Math.round(percentComplete)}
              progress
              style={progressStyle}
            />
          ) : (
            <Button
              active={direction === 'Upload'}
              color={color ?? (attempts > 1 ? 'yellow' : undefined)}
              fluid
              onClick={handleClick}
              size="mini"
              style={
                direction === 'Upload' ? uploadButtonStyle : downloadButtonStyle
              }
            >
              {isDownload && isQueuedState(state) && <Icon name="refresh" />}
              {isDownload && isRetryableState(state) && <Icon name="redo" />}
              {state}
              {placeInQueue ? ` (#${placeInQueue})` : ''}
              {attempts > 1 ? ` (Retry #${attempts})` : ''}
            </Button>
          )}
        </Table.Cell>
        <Table.Cell className="transferlist-size">
          <div style={sizeStyle}>
            <span>
              {formatBytesTransferred({ size, transferred: bytesTransferred })}
            </span>
          </div>
        </Table.Cell>
        <Table.Cell className="transferlist-detail">
          {/* Popup mounts its content (via Portal) only while open */}
          <Popup
            content={<TransferDetails file={file} />}
            on="click"
            position="left center"
            style={popupStyle}
            trigger={
              <Icon
                color="grey"
                link
                name="info circle"
                size="small"
              />
            }
            wide="very"
          />
        </Table.Cell>
      </Table.Row>
    );
  },
  areRowPropsEqual,
);

const TransferList = ({
  directoryName,
  files,
  onPlaceInQueueRequested,
  onRetryRequested,
  onSelectAll,
  onSelectionChange,
  selection,
}) => {
  const [isFolded, setIsFolded] = useState(false);

  // sort a copy (never the prop) once per new files array, not once per render
  const sortedFiles = useMemo(
    () =>
      files
        .map((file) => ({ file, name: getFileName(file.filename) }))
        .sort((a, b) => a.name.localeCompare(b.name))
        .map(({ file }) => file),
    [files],
  );

  const allSelected =
    sortedFiles.length > 0 && sortedFiles.every((f) => selection.has(f.id));

  return (
    <div>
      <Header
        className="filelist-header"
        size="small"
      >
        <Icon
          link
          name={isFolded ? 'folder' : 'folder open'}
          onClick={() => setIsFolded((previous) => !previous)}
        />
        {directoryName}
      </Header>
      {isFolded === false ? (
        <List>
          <List.Item>
            <Table>
              <Table.Header>
                <Table.Row>
                  <Table.HeaderCell className="transferlist-selector">
                    <Checkbox
                      checked={allSelected}
                      fitted
                      onChange={(event, data) =>
                        onSelectAll(sortedFiles, data.checked)
                      }
                    />
                  </Table.HeaderCell>
                  <Table.HeaderCell className="transferlist-filename">
                    File
                  </Table.HeaderCell>
                  <Table.HeaderCell className="transferlist-progress">
                    Progress
                  </Table.HeaderCell>
                  <Table.HeaderCell className="transferlist-size">
                    Size
                  </Table.HeaderCell>
                  <Table.HeaderCell className="transferlist-detail">
                    <Icon
                      name="info circle"
                      size="small"
                    />
                  </Table.HeaderCell>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {sortedFiles.map((file) => (
                  <TransferListRow
                    file={file}
                    key={file.id}
                    onPlaceInQueueRequested={onPlaceInQueueRequested}
                    onRetryRequested={onRetryRequested}
                    onSelectionChange={onSelectionChange}
                    selected={selection.has(file.id)}
                  />
                ))}
              </Table.Body>
            </Table>
          </List.Item>
        </List>
      ) : (
        ''
      )}
    </div>
  );
};

export default memo(TransferList, areListPropsEqual);
