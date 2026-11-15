import * as transfers from '../../lib/transfers';
import { getDirectoryContents } from '../../lib/users';
import { formatBytes, getDirectoryName } from '../../lib/util';
import FileList from '../Shared/FileList';
import React, { PureComponent } from 'react';
import { toast } from 'react-toastify';
import { Button, Card, Icon, Label } from 'semantic-ui-react';

// groups files by directory; push rather than concat so large directories
// don't copy their file list once per file
const buildTree = (response) => {
  const { files = [], lockedFiles = [] } = response;
  const tree = {};

  const add = (selectable) => {
    const directory = getDirectoryName(selectable.filename);

    if (tree[directory] === undefined) {
      tree[directory] = [selectable];
    } else {
      tree[directory].push(selectable);
    }
  };

  for (const file of files) {
    add({ selected: false, ...file });
  }

  for (const file of lockedFiles) {
    add({ selected: false, ...file, locked: true });
  }

  return tree;
};

class Response extends PureComponent {
  constructor(props) {
    super(props);

    this.state = {
      downloadError: '',
      downloadRequest: undefined,
      fetchingDirectoryContents: false,
      isFolded: this.props.isInitiallyFolded,
      tree: buildTree(this.props.response),
    };
  }

  componentDidUpdate(previousProps) {
    // most updates hand down the very same response object; only fall back to
    // a deep comparison (which preserves file selections) when it's a new one
    if (
      this.props.response !== previousProps.response &&
      JSON.stringify(this.props.response) !==
        JSON.stringify(previousProps.response)
    ) {
      this.setState({ tree: buildTree(this.props.response) });
    }

    if (this.props.isInitiallyFolded !== previousProps.isInitiallyFolded) {
      this.setState({ isFolded: this.props.isInitiallyFolded });
    }
  }

  handleFileSelectionChange = (file, state) => {
    file.selected = state;
    this.setState((previousState) => ({
      downloadError: '',
      downloadRequest: undefined,
      // a new object so PureComponent sees the (in-place) selection change
      tree: { ...previousState.tree },
    }));
  };

  download = (username, files) => {
    this.setState({ downloadRequest: 'inProgress' }, async () => {
      try {
        const requests = (files || []).map(({ filename, size }) => ({
          filename,
          size,
        }));
        await transfers.download({ files: requests, username });

        this.setState({ downloadRequest: 'complete' });
      } catch (error) {
        this.setState({
          downloadError: error.response,
          downloadRequest: 'error',
        });
      }
    });
  };

  getFullDirectory = async (username, directory) => {
    this.setState({ fetchingDirectoryContents: true });

    try {
      const oldTree = { ...this.state.tree };
      const oldFiles = oldTree[directory];

      try {
        // some clients might send more than one directory in the response,
        // if the requested directory contains subdirectories. the root directory
        // is always first, and for now we'll only display the contents of that.
        const allDirectories = await getDirectoryContents({
          directory,
          username,
        });
        const theRootDirectory = allDirectories?.[0];

        // some clients might send an empty response for some reason
        if (!theRootDirectory) {
          throw new Error('No directories were included in the response');
        }

        const { files, name } = theRootDirectory;

        // the api returns file names only, so we need to prepend the directory
        // to make it look like a search result.  we also need to preserve
        // any file selections, so check the old files and assign accordingly
        const fixedFiles = files.map((file) => ({
          ...file,
          filename: `${directory}\\${file.filename}`,
          selected:
            oldFiles.find(
              (f) => f.filename === `${directory}\\${file.filename}`,
            )?.selected ?? false,
        }));

        oldTree[name] = fixedFiles;
        this.setState({ tree: { ...oldTree } });
      } catch (error) {
        throw new Error(`Failed to process directory response: ${error}`, {
          cause: error,
        });
      }
    } catch (error) {
      console.error(error);
      toast.error(error?.response?.data ?? error?.message ?? error);
    } finally {
      this.setState({ fetchingDirectoryContents: false });
    }
  };

  handleToggleFolded = () => {
    this.setState((previousState) => ({ isFolded: !previousState.isFolded }));
  };

  render() {
    const { response } = this.props;
    const free = response.hasFreeUploadSlot;

    const {
      downloadError,
      downloadRequest,
      fetchingDirectoryContents,
      isFolded,
      tree,
    } = this.state;

    const selectedFiles = Object.values(tree)
      .flat()
      .filter((f) => f.selected);

    const selectedSize = formatBytes(
      selectedFiles.reduce((total, f) => total + f.size, 0),
    );

    return (
      <Card
        className="result-card"
        raised
      >
        <Card.Content>
          <Card.Header>
            <Icon
              link
              name={isFolded ? 'chevron right' : 'chevron down'}
              onClick={this.handleToggleFolded}
            />
            <Icon
              color={free ? 'green' : 'yellow'}
              name="circle"
            />
            {response.username}
            <Icon
              className="close-button"
              color="red"
              link
              name="close"
              onClick={() => this.props.onHide(response.username)}
            />
          </Card.Header>
          <Card.Meta className="result-meta">
            <span>
              Upload Speed: {formatBytes(response.uploadSpeed)}/s, Free Upload
              Slot: {free ? 'YES' : 'NO'}, Queue Length: {response.queueLength}
            </span>
          </Card.Meta>
          {((!isFolded && Object.keys(tree)) || []).map((directory) => (
            <FileList
              directoryName={directory}
              disabled={downloadRequest === 'inProgress'}
              files={tree[directory]}
              footer={
                <button
                  disabled={fetchingDirectoryContents}
                  onClick={() =>
                    this.getFullDirectory(response.username, directory)
                  }
                  style={{
                    backgroundColor: 'transparent',
                    border: 'none',
                    cursor: 'pointer',
                    width: '100%',
                  }}
                  type="button"
                >
                  <Icon
                    loading={fetchingDirectoryContents}
                    name={fetchingDirectoryContents ? 'circle notch' : 'search'}
                  />
                  Search for Additional Files in This Directory
                </button>
              }
              key={directory}
              locked={tree[directory].find((file) => file.locked)}
              onSelectionChange={this.handleFileSelectionChange}
            />
          ))}
        </Card.Content>
        {selectedFiles.length > 0 && (
          <Card.Content extra>
            <span>
              <Button
                color="green"
                content="Download"
                disabled={
                  this.props.disabled || downloadRequest === 'inProgress'
                }
                icon="download"
                label={{
                  as: 'a',
                  basic: false,
                  content: `${selectedFiles.length} file${selectedFiles.length === 1 ? '' : 's'}, ${selectedSize}`,
                }}
                labelPosition="right"
                onClick={() => this.download(response.username, selectedFiles)}
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

export default Response;
