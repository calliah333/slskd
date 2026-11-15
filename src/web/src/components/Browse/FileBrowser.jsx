import {
  formatAttributes,
  formatBytes,
  formatSeconds,
  getFileName,
} from '../../lib/util';
import React, { useEffect, useMemo, useState } from 'react';
import {
  Button,
  Checkbox,
  Header,
  Icon,
  Loader,
  Table,
} from 'semantic-ui-react';

const formatDirCaption = (fileCount, dirCount) => {
  const parts = [];
  if (fileCount > 0) {
    parts.push(`${fileCount} file${fileCount === 1 ? '' : 's'}`);
  }

  if (dirCount > 0) {
    parts.push(`${dirCount} director${dirCount === 1 ? 'y' : 'ies'}`);
  }

  return parts.length > 0 ? parts.join(', ') : '0 files';
};

const AUDIO_EXTENSIONS = new Set([
  'aac',
  'aif',
  'aiff',
  'alac',
  'ape',
  'flac',
  'm4a',
  'mp3',
  'ogg',
  'opus',
  'wav',
  'wma',
]);
const VIDEO_EXTENSIONS = new Set([
  'avi',
  'flv',
  'm4v',
  'mkv',
  'mov',
  'mp4',
  'webm',
  'wmv',
]);

const getFileIcon = (filename) => {
  const ext = filename.split('.').pop()?.toLowerCase();

  if (AUDIO_EXTENSIONS.has(ext)) return 'file audio outline';
  if (VIDEO_EXTENSIONS.has(ext)) return 'file video outline';
  return 'file outline';
};

const getAttributes = (files) => {
  if (!files || files.length === 0) return '';
  if (files.every((f) => f.isVariableBitRate)) return 'VBR';
  if (files.some((f) => f.isVariableBitRate)) return 'Mixed';

  const bitRates = new Set(files.map((f) => f.bitRate).filter(Boolean));

  if (bitRates.size === 1) return `${[...bitRates][0]} Kbps`;
  if (bitRates.size > 1) return 'Mixed';
  return '';
};

// fetched file lists never change, so their totals are computed once
const summaries = new WeakMap();

const summarize = (files) => {
  let summary = summaries.get(files);
  if (!summary) {
    let length = 0;
    let size = 0;
    for (const file of files) {
      length += file.length || 0;
      size += file.size || 0;
    }

    summary = { attributes: getAttributes(files), length, size };
    summaries.set(files, summary);
  }

  return summary;
};

// size/attributes/length of everything beneath a directory, known only once
// all of it has been fetched: a directory without subdirectories once its own
// files are loaded, or any directory whose subtree was selected
const getSummary = (dir, directoryFiles, subtreeFiles) => {
  const files =
    subtreeFiles.get(dir.name) ??
    (dir.children.length === 0
      ? directoryFiles.get(dir.name)?.files
      : undefined);

  return files ? summarize(files) : undefined;
};

const File = ({ checked, disabled, file, indent, locked, onToggle }) => (
  <Table.Row>
    <Table.Cell className="filelist-selector">
      {onToggle != null && (
        <Checkbox
          checked={checked}
          disabled={disabled}
          fitted
          onChange={(_, data) => onToggle(file, data.checked)}
        />
      )}
    </Table.Cell>
    <Table.Cell
      className="filelist-filename"
      style={indent ? { paddingLeft: '2em' } : undefined}
    >
      {locked ? (
        <Icon name="lock" />
      ) : (
        <Icon name={getFileIcon(file.filename)} />
      )}
      {getFileName(file.filename)}
    </Table.Cell>
    <Table.Cell className="filelist-size">{formatBytes(file.size)}</Table.Cell>
    <Table.Cell className="filelist-attributes">
      {formatAttributes(file)}
    </Table.Cell>
    <Table.Cell className="filelist-length">
      {formatSeconds(file.length)}
    </Table.Cell>
  </Table.Row>
);

// a placeholder while a directory's files are fetched, or a note about rows
// that aren't shown
const NoticeRow = ({ children, indent }) => (
  <Table.Row>
    <Table.Cell className="filelist-selector" />
    <Table.Cell
      className="filelist-filename"
      colSpan={4}
      style={indent ? { paddingLeft: '2em' } : undefined}
    >
      {children}
    </Table.Cell>
  </Table.Row>
);

// rows are rendered in pages; a directory with thousands of subdirectories or
// files otherwise blocks the page while every row mounts
const ROW_PAGE = 500;

const ShowMoreRow = ({ indent, onShowMore, shown, total }) => (
  <NoticeRow indent={indent}>
    Showing {shown.toLocaleString()} of {total.toLocaleString()}{' '}
    <Button
      basic
      compact
      content="Show more"
      onClick={onShowMore}
      size="mini"
    />
  </NoticeRow>
);

// the first `limit` files directly in a directory, once fetched; selectable
// only when onToggle is given
const DirectoryFiles = ({
  disabled,
  entry,
  indent,
  limit,
  locked,
  onToggle,
  selectedFiles,
}) => {
  if (entry === null) {
    return (
      <NoticeRow indent={indent}>
        <Loader
          active
          inline
          size="mini"
        />{' '}
        Loading files
      </NoticeRow>
    );
  }

  if (!entry) {
    return null;
  }

  return (
    <>
      {entry.files.slice(0, limit).map((file) => (
        <File
          checked={selectedFiles?.has(file.filename) ?? false}
          disabled={disabled}
          file={file}
          indent={indent}
          key={file.filename}
          locked={locked}
          onToggle={onToggle}
        />
      ))}
      {entry.truncated && limit >= entry.files.length && (
        <NoticeRow indent={indent}>
          Showing the first {entry.files.length.toLocaleString()} of{' '}
          {entry.total.toLocaleString()} files
        </NoticeRow>
      )}
    </>
  );
};

const DirectoryRow = ({
  checked,
  dir,
  directorySuffix,
  disabled,
  indent,
  isExpanded,
  onExpand,
  onToggle,
  sep,
  summary,
}) => {
  const dirName = dir.name.split(sep).pop();
  const interactive = onExpand != null;
  const folderIcon = indent
    ? 'folder outline'
    : isExpanded
      ? 'folder open'
      : 'folder';

  return (
    <Table.Row
      className={interactive ? 'browse-folderlist-row' : undefined}
      onClick={interactive ? () => onExpand(dir.name) : undefined}
    >
      <Table.Cell
        className="filelist-selector"
        onClick={interactive ? (e) => e.stopPropagation() : undefined}
      >
        {onToggle != null && (
          <Checkbox
            checked={checked}
            disabled={disabled || dir.totalFileCount === 0}
            fitted
            onChange={(_, data) => onToggle(dir, data.checked)}
          />
        )}
      </Table.Cell>
      <Table.Cell
        className={
          indent ? 'filelist-filename filelist-indent' : 'filelist-filename'
        }
      >
        <Icon name={folderIcon} />
        {dirName}
        <span className="browse-folderlist-caption">
          {formatDirCaption(dir.totalFileCount, dir.totalDirectoryCount)}
        </span>
        {directorySuffix && (
          <span
            className="browse-folderlist-suffix"
            onClick={interactive ? (e) => e.stopPropagation() : undefined}
            onKeyDown={interactive ? (e) => e.stopPropagation() : undefined}
            role={interactive ? 'presentation' : undefined}
          >
            {directorySuffix(dir)}
          </span>
        )}
      </Table.Cell>
      <Table.Cell className="filelist-size">
        {summary ? formatBytes(summary.size) : ''}
      </Table.Cell>
      <Table.Cell className="filelist-attributes">
        {summary?.attributes ?? ''}
      </Table.Cell>
      <Table.Cell className="filelist-length">
        {summary?.length > 0 ? formatSeconds(summary.length) : ''}
      </Table.Cell>
    </Table.Row>
  );
};

// a subdirectory of the selected directory; when expanded, lists its own
// subdirectories and the files directly inside it, fetched on demand
const Directory = ({
  checked,
  dir,
  directoryFiles,
  directorySuffix,
  disabled,
  isExpanded,
  onExpand,
  onToggle,
  sep,
  subtreeFiles,
}) => {
  const [rowLimit, setRowLimit] = useState(ROW_PAGE);
  const entry = directoryFiles.get(dir.name);
  const rowCount = dir.children.length + (entry?.files.length ?? 0);

  return (
    <>
      <DirectoryRow
        checked={checked}
        dir={dir}
        directorySuffix={directorySuffix}
        disabled={disabled}
        isExpanded={isExpanded}
        onExpand={onExpand}
        onToggle={onToggle}
        sep={sep}
        summary={getSummary(dir, directoryFiles, subtreeFiles)}
      />
      {isExpanded && (
        <>
          {dir.children.slice(0, rowLimit).map((subChild) => (
            <DirectoryRow
              dir={subChild}
              directorySuffix={directorySuffix}
              indent
              key={subChild.name}
              sep={sep}
              summary={getSummary(subChild, directoryFiles, subtreeFiles)}
            />
          ))}
          <DirectoryFiles
            entry={entry}
            indent
            limit={Math.max(0, rowLimit - dir.children.length)}
          />
          {rowCount > rowLimit && (
            <ShowMoreRow
              indent
              onShowMore={() => setRowLimit(rowLimit + ROW_PAGE)}
              shown={rowLimit}
              total={rowCount}
            />
          )}
        </>
      )}
    </>
  );
};

const FileBrowser = ({
  busy,
  directoryFiles,
  directorySuffix,
  disabled,
  expandedDirectory,
  locked,
  notice,
  onClose,
  onDirectoryToggle,
  onExpandedDirectoryChange,
  onFileToggle,
  rootDirectory,
  selectedFiles,
  separator,
  subtreeFiles,
}) => {
  const [folded, setFolded] = useState(false);
  const [rowLimit, setRowLimit] = useState(ROW_PAGE);

  const directoryName = rootDirectory?.name || '';
  const sep = separator || '\\';
  const childDirs = rootDirectory?.children ?? [];
  const totalFileCount = rootDirectory?.totalFileCount ?? 0;
  const rootEntry = directoryFiles.get(directoryName);
  const rowCount = childDirs.length + (rootEntry?.files.length ?? 0);

  useEffect(() => setRowLimit(ROW_PAGE), [directoryName]);

  // count selected files per child directory in one pass over the selection
  // (at most MAX_SELECTED_FILES), so each child's checkbox is a lookup
  const selectedByChild = useMemo(() => {
    const counts = new Map();
    const prefix = directoryName + sep;
    for (const filename of selectedFiles.keys()) {
      const index = filename.startsWith(prefix)
        ? filename.indexOf(sep, prefix.length)
        : -1;
      if (index !== -1) {
        const child = filename.slice(0, index);
        counts.set(child, (counts.get(child) ?? 0) + 1);
      }
    }

    return counts;
  }, [directoryName, selectedFiles, sep]);

  const allSelected =
    totalFileCount > 0 && selectedFiles.size >= totalFileCount;

  const handleSelectAll = (_, data) =>
    onDirectoryToggle(rootDirectory, data.checked);

  const handleExpandDirectory = (name) => {
    onExpandedDirectoryChange(expandedDirectory === name ? null : name);
  };

  const hasContent =
    (rootDirectory?.fileCount ?? 0) > 0 || childDirs.length > 0;

  return (
    <div className={locked ? 'filebrowser-locked' : undefined}>
      <Header
        className="filelist-header"
        size="small"
      >
        <div>
          <Icon
            link={!locked}
            name={locked ? 'lock' : folded ? 'folder' : 'folder open'}
            onClick={() => !locked && setFolded(!folded)}
            size="large"
          />
          {directoryName}
          {busy && (
            <Loader
              active
              inline
              size="mini"
            />
          )}
          {Boolean(onClose) && (
            <Icon
              className="close-button"
              color="red"
              link
              name="close"
              onClick={() => onClose()}
            />
          )}
        </div>
      </Header>
      {notice}
      {!folded && hasContent && (
        <Table>
          <Table.Header>
            <Table.Row>
              <Table.HeaderCell className="filelist-selector">
                <Checkbox
                  checked={allSelected}
                  disabled={disabled || totalFileCount === 0}
                  fitted
                  onChange={handleSelectAll}
                />
              </Table.HeaderCell>
              <Table.HeaderCell className="filelist-filename">
                {childDirs.length > 0 ? 'Name' : 'File'}
              </Table.HeaderCell>
              <Table.HeaderCell className="filelist-size">
                Size
              </Table.HeaderCell>
              <Table.HeaderCell className="filelist-attributes">
                Attributes
              </Table.HeaderCell>
              <Table.HeaderCell className="filelist-length">
                Length
              </Table.HeaderCell>
            </Table.Row>
          </Table.Header>
          <Table.Body>
            {childDirs.slice(0, rowLimit).map((child) => (
              <Directory
                checked={
                  child.totalFileCount > 0 &&
                  (selectedByChild.get(child.name) ?? 0) >= child.totalFileCount
                }
                dir={child}
                directoryFiles={directoryFiles}
                directorySuffix={directorySuffix}
                disabled={disabled}
                isExpanded={expandedDirectory === child.name}
                key={child.name}
                onExpand={handleExpandDirectory}
                onToggle={onDirectoryToggle}
                sep={sep}
                subtreeFiles={subtreeFiles}
              />
            ))}
            <DirectoryFiles
              disabled={disabled}
              entry={rootEntry}
              limit={Math.max(0, rowLimit - childDirs.length)}
              locked={locked}
              onToggle={onFileToggle}
              selectedFiles={selectedFiles}
            />
            {rowCount > rowLimit && (
              <ShowMoreRow
                onShowMore={() => setRowLimit(rowLimit + ROW_PAGE)}
                shown={rowLimit}
                total={rowCount}
              />
            )}
          </Table.Body>
        </Table>
      )}
    </div>
  );
};

export default React.memo(FileBrowser);
