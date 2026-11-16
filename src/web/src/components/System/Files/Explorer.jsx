import { deleteDirectory, deleteFile, list } from '../../../lib/files';
import { formatBytes, formatDate } from '../../../lib/util';
import { LoaderSegment } from '../../Shared';
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Button, Header, Icon, Modal, Table } from 'semantic-ui-react';

// rows are rendered in pages; a directory with tens of thousands of entries
// otherwise blocks the page while every row mounts
const ROW_PAGE = 500;

// one collator for every comparison; localeCompare with options builds a new
// collator per call, which dominates sorting large directories
const nameCollator = new Intl.Collator(undefined, {
  numeric: true,
  sensitivity: 'base',
});

const DeleteIcon = ({ onClick }) => (
  <Icon
    color="red"
    name="trash alternate"
    onClick={onClick}
    style={{ cursor: 'pointer' }}
  />
);

const FileRow = React.memo(({ entry, onDelete, remoteFileManagement }) => {
  const { fullName, length, modifiedAt, name } = entry;

  return (
    <Table.Row>
      <Table.Cell>
        <Icon name="file outline" />
        {name}
      </Table.Cell>
      <Table.Cell>{modifiedAt ? formatDate(modifiedAt) : ''}</Table.Cell>
      <Table.Cell>{length ? formatBytes(length) : ''}</Table.Cell>
      <Table.Cell>
        {remoteFileManagement ? (
          <DeleteIcon onClick={() => onDelete({ fullName, type: 'file' })} />
        ) : null}
      </Table.Cell>
    </Table.Row>
  );
});

const DirectoryRow = React.memo(
  ({
    deletable = true,
    entry,
    onDelete,
    onOpen = () => {},
    remoteFileManagement,
  }) => {
    const { fullName, modifiedAt, name } = entry;

    return (
      <Table.Row>
        <Table.Cell
          onClick={() => onOpen(name)}
          style={{ cursor: 'pointer' }}
        >
          <Icon name="folder" />
          {name}
        </Table.Cell>
        <Table.Cell>{modifiedAt ? formatDate(modifiedAt) : ''}</Table.Cell>
        <Table.Cell />
        <Table.Cell>
          {remoteFileManagement && deletable ? (
            <DeleteIcon
              onClick={() => onDelete({ fullName, type: 'directory' })}
            />
          ) : (
            ''
          )}
        </Table.Cell>
      </Table.Row>
    );
  },
);

const parentEntry = { fullName: '..', name: '..' };

const sortItems = (items, sortColumn, sortDirection) => {
  if (!items || items.length === 0) return items;

  const direction = sortDirection === 'ascending' ? 1 : -1;

  if (sortColumn === 'date') {
    // parse each date once rather than on every comparison
    return items
      .map((item) => ({
        item,
        time: item.modifiedAt ? new Date(item.modifiedAt).getTime() : 0,
      }))
      .sort((a, b) => direction * (a.time - b.time))
      .map(({ item }) => item);
  }

  if (sortColumn === 'name') {
    return [...items].sort(
      (a, b) => direction * nameCollator.compare(a.name, b.name),
    );
  }

  return items;
};

const Explorer = ({ remoteFileManagement, root }) => {
  const [directory, setDirectory] = useState({ directories: [], files: [] });
  const [subdirectory, setSubdirectory] = useState([]);
  const [loading, setLoading] = useState(false);
  const [sortColumn, setSortColumn] = useState('name');
  const [sortDirection, setSortDirection] = useState('ascending');
  const [rowLimit, setRowLimit] = useState(ROW_PAGE);
  const [pendingDelete, setPendingDelete] = useState(null);

  const fetch = async () => {
    setLoading(true);

    try {
      const directoryResult = await list({
        root,
        subdirectory: subdirectory.join('/'),
      });
      setDirectory(directoryResult);
    } catch (error) {
      console.error(error);
      setDirectory({ directories: [], files: [] });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    setRowLimit(ROW_PAGE);
    fetch();
  }, [subdirectory]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    setSubdirectory([]);
  }, [root]);

  const select = useCallback(
    (path) => setSubdirectory((current) => [...current, path]),
    [],
  );

  const upOneSubdirectory = useCallback(
    () => setSubdirectory((current) => current.slice(0, -1)),
    [],
  );

  const confirmDelete = async () => {
    const { fullName, type } = pendingDelete;
    const remove = type === 'file' ? deleteFile : deleteDirectory;

    await remove({
      path: `${subdirectory.join('/')}/${fullName}`,
      root,
    });
    fetch();
  };

  const handleSort = (column) => {
    if (sortColumn === column) {
      setSortDirection(
        sortDirection === 'ascending' ? 'descending' : 'ascending',
      );
    } else {
      setSortColumn(column);
      setSortDirection('ascending');
    }
  };

  const sortedDirectories = useMemo(
    () => sortItems(directory?.directories, sortColumn, sortDirection) ?? [],
    [directory, sortColumn, sortDirection],
  );

  const sortedFiles = useMemo(
    () => sortItems(directory?.files, sortColumn, sortDirection) ?? [],
    [directory, sortColumn, sortDirection],
  );

  if (loading) {
    return <LoaderSegment />;
  }

  const total = sortedDirectories.length + sortedFiles.length;

  return (
    <>
      <Header
        className="explorer-working-directory"
        size="small"
      >
        <Icon name="folder open" />
        {'/' + root + '/' + subdirectory.join('/')}
      </Header>
      <Table
        className="unstackable"
        size="large"
      >
        <Table.Header>
          <Table.Row>
            <Table.HeaderCell
              className="explorer-list-name"
              onClick={() => handleSort('name')}
              style={{ cursor: 'pointer' }}
            >
              Name
              {sortColumn === 'name' && (
                <Icon
                  name={
                    sortDirection === 'ascending'
                      ? 'chevron up'
                      : 'chevron down'
                  }
                />
              )}
            </Table.HeaderCell>
            <Table.HeaderCell
              className="explorer-list-date"
              onClick={() => handleSort('date')}
              style={{ cursor: 'pointer' }}
            >
              Date Modified
              {sortColumn === 'date' && (
                <Icon
                  name={
                    sortDirection === 'ascending'
                      ? 'chevron up'
                      : 'chevron down'
                  }
                />
              )}
            </Table.HeaderCell>
            <Table.HeaderCell className="explorer-list-size">
              Size
            </Table.HeaderCell>
            <Table.HeaderCell className="explorer-list-action" />
          </Table.Row>
        </Table.Header>
        <Table.Body>
          {total === 0 ? (
            <Table.Row>
              <Table.Cell
                colSpan={99}
                style={{
                  opacity: 0.5,
                  padding: '10px !important',
                  textAlign: 'center',
                }}
              >
                No files or directories
              </Table.Cell>
            </Table.Row>
          ) : (
            <>
              {subdirectory.length > 0 && (
                <DirectoryRow
                  deletable={false}
                  entry={parentEntry}
                  onOpen={upOneSubdirectory}
                  remoteFileManagement={remoteFileManagement}
                />
              )}
              {sortedDirectories.slice(0, rowLimit).map((d) => (
                <DirectoryRow
                  entry={d}
                  key={d.name}
                  onDelete={setPendingDelete}
                  onOpen={select}
                  remoteFileManagement={remoteFileManagement}
                />
              ))}
              {sortedFiles
                .slice(0, Math.max(0, rowLimit - sortedDirectories.length))
                .map((f) => (
                  <FileRow
                    entry={f}
                    key={f.name}
                    onDelete={setPendingDelete}
                    remoteFileManagement={remoteFileManagement}
                  />
                ))}
              {total > rowLimit && (
                <Table.Row>
                  <Table.Cell
                    colSpan={99}
                    style={{ textAlign: 'center' }}
                  >
                    Showing {rowLimit.toLocaleString()} of{' '}
                    {total.toLocaleString()}{' '}
                    <Button
                      basic
                      compact
                      content="Show more"
                      onClick={() => setRowLimit(rowLimit + ROW_PAGE)}
                      size="mini"
                    />
                  </Table.Cell>
                </Table.Row>
              )}
            </>
          )}
        </Table.Body>
      </Table>
      {/* one confirmation modal shared by every row */}
      <Modal
        actions={[
          'Cancel',
          {
            content: 'Delete',
            key: 'done',
            negative: true,
            onClick: confirmDelete,
          },
        ]}
        centered
        content={
          pendingDelete &&
          `Are you sure you want to delete ${pendingDelete.type} '${pendingDelete.fullName}'?`
        }
        header={
          <Header
            content={
              pendingDelete?.type === 'file'
                ? 'Confirm File Delete'
                : 'Confirm Directory Delete'
            }
            icon="trash alternate"
          />
        }
        onClose={() => setPendingDelete(null)}
        open={pendingDelete !== null}
        size="small"
      />
    </>
  );
};

export default Explorer;
