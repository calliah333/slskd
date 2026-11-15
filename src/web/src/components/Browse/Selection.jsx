import * as transfers from '../../lib/transfers';
import * as users from '../../lib/users';
import { formatBytes } from '../../lib/util';
import FileBrowser from './FileBrowser';
import React, { Component } from 'react';
import { toast } from 'react-toastify';
import { Button, Card, Icon, Label, Message } from 'semantic-ui-react';

// the selection is persisted in (small, synchronous) local storage, so limit
// it to 5k files; 5k files is also a LOT to enqueue at once
const MAX_SELECTED_FILES = 5_000;

const toastTooManyFiles = () =>
  toast.error(
    `Maximum number of selected files is limited to ${MAX_SELECTED_FILES.toLocaleString()}. Narrow your selection.`,
  );

// the server answers 404 when its cached copy of the browse is gone (expired,
// evicted, or the server restarted) or no longer has the directory
const isCacheMiss = (error) => error?.response?.status === 404;

// files as the server sends them carry only their name; qualify them with the
// directory so they can be enqueued and matched against the selection
const qualifyFiles = ({ files = [], name }, separator) =>
  files
    .slice()
    .sort((a, b) => a.filename.localeCompare(b.filename))
    .map((file) => ({
      ...file,
      filename: `${name}${separator}${file.filename}`,
    }));

// the selection is persisted as [{ filename, size }]; anything else, like the
// bare filenames saved by earlier versions, can't be enqueued and is dropped
const toSelectedFiles = (files) => {
  const selectedFiles = new Map();
  for (const file of files || []) {
    if (typeof file?.filename === 'string') {
      selectedFiles.set(file.filename, {
        filename: file.filename,
        size: file.size,
      });
    }
  }

  return selectedFiles;
};

const getInitialState = ({ defaultSelectedFiles, defaultSubdirectory }) => ({
  cacheMissing: false,
  // directory name -> { files, total, truncated } once fetched, null while
  // fetching. scoped to the selected directory, so it never grows past what
  // the user has looked at since selecting it
  directoryFiles: new Map(),
  downloadError: '',
  downloadRequest: undefined,
  expandedDirectory: defaultSubdirectory ?? null,
  // filename -> { filename, size }
  selectedFiles: toSelectedFiles(defaultSelectedFiles),
  selecting: false,
  // directory name -> every file beneath it, from a recursive fetch
  subtreeFiles: new Map(),
});

class Selection extends Component {
  constructor(props) {
    super(props);

    this.state = getInitialState(props);

    // bumped whenever the directory changes, so responses for the previous
    // one are ignored
    this.generation = 0;
  }

  componentDidMount() {
    this.loadVisibleDirectories();
  }

  componentDidUpdate(previousProps) {
    // a new node means another directory, or the same one from a new browse
    if (this.props.node !== previousProps.node) {
      this.generation++;
      this.setState(getInitialState(this.props), this.loadVisibleDirectories);
    }
  }

  componentWillUnmount() {
    this.generation++;
  }

  loadVisibleDirectories = () => {
    const { node } = this.props;
    const { expandedDirectory } = this.state;

    this.loadDirectory(node);

    const expanded = node.children.find(
      (child) => child.name === expandedDirectory,
    );
    if (expanded) {
      this.loadDirectory(expanded);
    }
  };

  updateDirectoryFiles = (name, entry) => {
    this.setState((previousState) => {
      const directoryFiles = new Map(previousState.directoryFiles);
      if (entry === undefined) {
        directoryFiles.delete(name);
      } else {
        directoryFiles.set(name, entry);
      }

      return { directoryFiles };
    });
  };

  handleFetchError = (error) => {
    if (isCacheMiss(error)) {
      this.setState({ cacheMissing: true });
      return;
    }

    console.error(error);
    toast.error(
      `Failed to fetch files: ${error?.response?.data ?? error?.message ?? error}`,
    );
  };

  // fetches the files directly in a directory (not its subdirectories)
  loadDirectory = async (directory) => {
    const { name } = directory;

    if (this.state.directoryFiles.has(name)) {
      return;
    }

    if (directory.fileCount === 0) {
      this.updateDirectoryFiles(name, {
        files: [],
        total: 0,
        truncated: false,
      });
      return;
    }

    const { separator, username } = this.props;
    const generation = this.generation;

    this.updateDirectoryFiles(name, null);

    try {
      const { directories, total, truncated } = await users.getBrowseFiles({
        directory: name,
        username,
      });

      if (generation === this.generation) {
        const [fetched] = directories ?? [];
        this.updateDirectoryFiles(name, {
          files: fetched ? qualifyFiles(fetched, separator) : [],
          total,
          truncated,
        });
      }
    } catch (error) {
      if (generation === this.generation) {
        this.updateDirectoryFiles(name, undefined);
        this.handleFetchError(error);
      }
    }
  };

  reportState = () =>
    this.props.onStateChange?.({
      files: [...this.state.selectedFiles.values()],
      subdirectory: this.state.expandedDirectory,
    });

  // applies `update` to a copy of the selection
  updateSelection = (update) => {
    this.setState((previousState) => {
      const selectedFiles = new Map(previousState.selectedFiles);
      update(selectedFiles);

      return {
        downloadError: '',
        downloadRequest: undefined,
        selectedFiles,
      };
    }, this.reportState);
  };

  // selects every file beneath a directory. this is the only place the whole
  // subtree is needed, and the server refuses to send more than one file past
  // the selection limit, so a huge directory is never materialized
  selectSubtree = async (directory) => {
    const { separator, username } = this.props;
    const { name } = directory;
    const generation = this.generation;

    this.setState({ selecting: true });

    try {
      const { directories, total, truncated } = await users.getBrowseFiles({
        directory: name,
        limit: MAX_SELECTED_FILES + 1,
        recursive: true,
        username,
      });

      if (generation !== this.generation) {
        return;
      }

      const prefix = name + separator;
      let selectedElsewhere = 0;
      for (const filename of this.state.selectedFiles.keys()) {
        if (!filename.startsWith(prefix)) {
          selectedElsewhere++;
        }
      }

      if (truncated || total + selectedElsewhere > MAX_SELECTED_FILES) {
        toastTooManyFiles();
        return;
      }

      const fetched = directories.map((fetchedDirectory) => {
        const files = qualifyFiles(fetchedDirectory, separator);
        return [
          fetchedDirectory.name,
          { files, total: files.length, truncated: false },
        ];
      });

      this.setState((previousState) => {
        const directoryFiles = new Map(previousState.directoryFiles);
        const selectedFiles = new Map(previousState.selectedFiles);
        const files = [];

        for (const [directoryName, entry] of fetched) {
          if (!directoryFiles.get(directoryName)) {
            directoryFiles.set(directoryName, entry);
          }

          for (const file of entry.files) {
            files.push(file);
            selectedFiles.set(file.filename, {
              filename: file.filename,
              size: file.size,
            });
          }
        }

        return {
          directoryFiles,
          downloadError: '',
          downloadRequest: undefined,
          selectedFiles,
          subtreeFiles: new Map(previousState.subtreeFiles).set(name, files),
        };
      }, this.reportState);
    } catch (error) {
      if (generation === this.generation) {
        this.handleFetchError(error);
      }
    } finally {
      if (generation === this.generation) {
        this.setState({ selecting: false });
      }
    }
  };

  handleFileToggle = (file, checked) => {
    if (checked && this.state.selectedFiles.size >= MAX_SELECTED_FILES) {
      toastTooManyFiles();
      return;
    }

    this.updateSelection((selectedFiles) => {
      if (checked) {
        selectedFiles.set(file.filename, {
          filename: file.filename,
          size: file.size,
        });
      } else {
        selectedFiles.delete(file.filename);
      }
    });
  };

  // (de)selects every file beneath a directory; used for both the selected
  // directory itself and its subdirectories
  handleDirectoryToggle = (directory, checked) => {
    if (checked) {
      if (directory.totalFileCount > 0) {
        this.selectSubtree(directory);
      }

      return;
    }

    const prefix = directory.name + this.props.separator;
    this.updateSelection((selectedFiles) => {
      for (const filename of selectedFiles.keys()) {
        if (filename.startsWith(prefix)) {
          selectedFiles.delete(filename);
        }
      }
    });
  };

  handleExpandedDirectoryChange = (name) => {
    this.setState({ expandedDirectory: name }, () => {
      this.reportState();

      const expanded = this.props.node.children.find(
        (child) => child.name === name,
      );
      if (expanded) {
        this.loadDirectory(expanded);
      }
    });
  };

  handleDownload = () => {
    const { name, separator, username } = this.props;
    const selectedFiles = [...this.state.selectedFiles.values()];
    const parent = name.split(separator).slice(0, -1).join(separator);
    const prefix = name + separator;

    this.setState({ downloadRequest: 'inProgress' }, async () => {
      try {
        const groups = new Map();
        for (const file of selectedFiles) {
          const relative = file.filename.startsWith(prefix)
            ? file.filename.slice(prefix.length)
            : file.filename;
          const parts = relative.split(separator).filter(Boolean);
          const group = parts.length > 1 ? parts[0] : '';
          if (!groups.has(group)) {
            groups.set(group, []);
          }

          groups.get(group).push(file);
        }

        const rootFiles = groups.get('') || [];
        if (rootFiles.length > 0) {
          await transfers.download({
            files: rootFiles.map(({ filename, size }) => ({ filename, size })),
            username,
          });
        }

        for (const [dirName, dirFiles] of groups) {
          if (dirName === '') {
            continue;
          }

          const destination = (prefix + dirName)
            .slice(parent.length > 0 ? parent.length + 1 : 0)
            .split(separator)
            .join('/');
          await transfers.enqueueBatch({
            files: dirFiles.map(({ filename, size }) => ({ filename, size })),
            options: { destination },
            username,
          });
        }

        this.setState({ downloadRequest: 'complete' });
      } catch (error) {
        this.setState({
          downloadError: error.response,
          downloadRequest: 'error',
        });
      }
    });
  };

  renderCacheMissing() {
    const { onBrowseAgain, username } = this.props;

    return (
      <Message
        className="browse-cache-missing"
        warning
      >
        <span>
          The server no longer has the results of this browse, so files
          can&apos;t be listed or selected. Browse {username} again to refresh
          them.
        </span>
        <Button
          compact
          content="Browse again"
          icon="refresh"
          onClick={onBrowseAgain}
        />
      </Message>
    );
  }

  render() {
    const { directorySuffix, locked, node, onClose, separator } = this.props;
    const {
      cacheMissing,
      directoryFiles,
      downloadError,
      downloadRequest,
      expandedDirectory,
      selectedFiles,
      selecting,
      subtreeFiles,
    } = this.state;

    const selectedCount = selectedFiles.size;
    let selectedBytes = 0;
    for (const file of selectedFiles.values()) {
      selectedBytes += file.size || 0;
    }

    return (
      <Card
        className="result-card"
        raised
      >
        <Card.Content>
          <div className="browse-selection-content">
            <FileBrowser
              busy={selecting}
              directoryFiles={directoryFiles}
              directorySuffix={directorySuffix}
              disabled={downloadRequest === 'inProgress' || selecting}
              expandedDirectory={expandedDirectory}
              locked={locked}
              notice={cacheMissing ? this.renderCacheMissing() : null}
              onClose={onClose}
              onDirectoryToggle={this.handleDirectoryToggle}
              onExpandedDirectoryChange={this.handleExpandedDirectoryChange}
              onFileToggle={this.handleFileToggle}
              rootDirectory={node}
              selectedFiles={selectedFiles}
              separator={separator}
              subtreeFiles={subtreeFiles}
            />
          </div>
        </Card.Content>
        {selectedCount > 0 && (
          <Card.Content extra>
            <span>
              <Button
                color="green"
                content="Download"
                disabled={downloadRequest === 'inProgress'}
                icon="download"
                label={{
                  as: 'a',
                  basic: false,
                  content: `${selectedCount} file${selectedCount === 1 ? '' : 's'}, ${formatBytes(selectedBytes)}`,
                }}
                labelPosition="right"
                onClick={this.handleDownload}
              />
              {downloadRequest === 'inProgress' && (
                <Icon
                  loading
                  name="circle notch"
                  size="large"
                />
              )}
              {downloadRequest === 'complete' && (
                <Icon
                  color="green"
                  name="checkmark"
                  size="large"
                />
              )}
              {downloadRequest === 'error' && (
                <span>
                  <Icon
                    color="red"
                    name="x"
                    size="large"
                  />
                  <Label>
                    {downloadError?.data
                      ? `${downloadError.data} (HTTP ${downloadError.status} ${downloadError.statusText})`
                      : 'Download failed'}
                  </Label>
                </span>
              )}
            </span>
          </Card.Content>
        )}
      </Card>
    );
  }
}

export default Selection;
