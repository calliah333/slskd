import './Browse.css';
import { poll } from '../../lib/polling';
import * as users from '../../lib/users';
import PlaceholderSegment from '../Shared/PlaceholderSegment';
import DirectoryTree from './DirectoryTree';
import Selection from './Selection';
import React, { Component } from 'react';
import { withRouter } from 'react-router-dom';
import { toast } from 'react-toastify';
import { Card, Icon, Input, Loader, Segment } from 'semantic-ui-react';

// v1 stored every file of the last browse, which could be hundreds of MB to
// clone back in on every visit. v2 stores only directory metadata; the files
// stay in the server's browse cache. drop v1 data without ever reading it
const openBrowseDb = () =>
  new Promise((resolve, reject) => {
    const req = indexedDB.open('slskd-browse', 2);
    req.onupgradeneeded = ({ target }) => {
      const db = target.result;
      if (db.objectStoreNames.contains('browse')) {
        db.deleteObjectStore('browse');
      }

      db.createObjectStore('browse');
    };

    req.onsuccess = ({ target }) => resolve(target.result);
    req.onerror = ({ target }) => reject(target.error);
  });

const idbPut = async (key, value) => {
  const db = await openBrowseDb();
  await new Promise((resolve, reject) => {
    const tx = db.transaction('browse', 'readwrite');
    tx.objectStore('browse').put(value, key);
    tx.oncomplete = resolve;
    tx.onerror = ({ target }) => reject(target.error);
  });
};

const idbGet = async (key) => {
  const db = await openBrowseDb();
  return new Promise((resolve, reject) => {
    const req = db.transaction('browse').objectStore('browse').get(key);
    req.onsuccess = ({ target }) => resolve(target.result ?? null);
    req.onerror = ({ target }) => reject(target.error);
  });
};

const formatBrowseSummary = ({
  directories,
  files,
  lockedDirectories,
  lockedFiles,
}) =>
  `${files + lockedFiles} files in ${directories + lockedDirectories} ` +
  `directories (including ${lockedFiles} files in ${lockedDirectories} locked directories)`;

const initialState = {
  browseError: undefined,
  browseLoading: false,
  browseState: 'idle',
  browseStatus: 0,
  directories: [],
  info: {
    directories: 0,
    files: 0,
    lockedDirectories: 0,
    lockedFiles: 0,
  },
  selected: null,
  separator: '\\',
  tree: [],
  username: '',
};

class Browse extends Component {
  constructor(props) {
    super(props);

    this.state = initialState;
    this.directoryTreeRef = React.createRef();
    this.stopStatusPolling = undefined;
    this.directoryIndex = { byName: new Map(), tree: undefined };
  }

  componentDidMount() {
    (async () => {
      await this.loadState();
      this.saveState();
    })();

    if (this.props.location.state?.user) {
      this.setState({ username: this.props.location.state.user }, this.browse);
    }

    document.addEventListener('keyup', this.keyUp, false);
  }

  componentWillUnmount() {
    this.stopStatusPolling?.();
    document.removeEventListener('keyup', this.keyUp, false);
  }

  browse = async () => {
    const username = this.inputtext.inputRef.current.value;
    this.setState({ browseError: undefined, browseState: 'pending', username });

    // progress is only meaningful while the browse is in flight
    this.stopStatusPolling?.();
    this.stopStatusPolling = poll(this.fetchStatus, 500, { immediate: false });

    try {
      // directory metadata only; files are fetched per directory on demand
      // from the server's cached copy of this browse
      const {
        directories,
        directoryCount,
        fileCount,
        lockedDirectoryCount,
        lockedFileCount,
      } = await users.browseDirectories({ username });

      // detect the path separator from the first directory name that has one
      let separator = '\\';
      for (const { name } of directories) {
        if (name.includes('\\')) {
          break;
        }

        if (name.includes('/')) {
          separator = '/';
          break;
        }
      }

      this.setState(
        {
          browseError: undefined,
          browseState: 'complete',
          directories,
          info: {
            directories: directoryCount,
            files: fileCount,
            lockedDirectories: lockedDirectoryCount,
            lockedFiles: lockedFileCount,
          },
          separator,
          tree: this.getDirectoryTree({ directories, separator }),
        },
        () => {
          this.saveState();
          setTimeout(() => this.saveTree(), 0);
        },
      );
    } catch (error) {
      this.setState({ browseError: error, browseState: 'error' });
    } finally {
      this.stopStatusPolling?.();
      this.stopStatusPolling = undefined;
    }
  };

  // the server no longer has this browse cached; fetch it from the peer again
  handleBrowseAgain = () => {
    this.inputtext.inputRef.current.value = this.state.username;
    this.browse();
  };

  clear = () => {
    this.setState(initialState, () => {
      this.saveTree();
      this.saveState();
      this.inputtext.focus();
    });
  };

  keyUp = (event) => (event.key === 'Escape' ? this.clear() : '');

  saveTree = async () => {
    try {
      const { directories, info, separator, username } = this.state;
      await idbPut('current', { directories, info, separator, username });
    } catch (error) {
      console.error(error);
    }
  };

  saveState = () => {
    this.inputtext.inputRef.current.value = this.state.username;
    this.inputtext.inputRef.current.disabled =
      this.state.browseState !== 'idle';

    try {
      const { browseError, browseState, info, selected, separator, username } =
        this.state;
      localStorage.setItem(
        'slskd-browse-state',
        JSON.stringify({
          browseError,
          browseState,
          info,
          selected,
          separator,
          username,
        }),
      );
    } catch (error) {
      console.error(error);

      // most likely cause is a huge file selection blowing past
      // localStorage's per-origin quota; the MAX_SELECTED_FILES cap in
      // Selection.jsx should prevent this in practice, but fall back to a
      // clear message rather than silently failing to save
      const isQuotaError =
        error.name === 'QuotaExceededError' || error.code === 22;
      if (isQuotaError) {
        toast.error(
          'Selection is too large to save — try selecting fewer files.',
        );
      }
    }
  };

  loadState = async () => {
    if (this.props.location.state?.user) {
      return;
    }

    try {
      const metaStr = localStorage.getItem('slskd-browse-state');
      const meta = metaStr ? JSON.parse(metaStr) : null;

      if (meta?.username) {
        this.setState({ browseLoading: true, username: meta.username }, () => {
          this.inputtext.inputRef.current.value = meta.username;
        });
      }

      const saved = await idbGet('current');

      // without saved directories there is nothing to show; leave the
      // username in place so browsing again is one click
      if (!saved) {
        this.setState({ browseLoading: false });
        return;
      }

      const { directories, info, separator, username } = saved;

      this.setState({
        ...meta,
        browseLoading: false,
        directories,
        info,
        separator,
        tree: this.getDirectoryTree({ directories, separator }),
        username,
      });
    } catch (error) {
      console.error(error);
      this.setState({ browseLoading: false });
    }
  };

  fetchStatus = async () => {
    const { browseState, username } = this.state;
    if (browseState !== 'pending') {
      return;
    }

    try {
      const response = await users.getBrowseStatus({ username });
      this.setState({ browseStatus: response.data });
    } catch {
      // status is not available until the first progress update, and is removed shortly after completion;
      // this runs on an interval, so just try again next time
    }
  };

  // one node per directory, linked to its parent by name; directories whose
  // parent isn't in the list are roots. O(N), and no node copies anything
  // beyond the directory's own metadata
  getDirectoryTree = ({ directories, separator }) => {
    if (!directories?.length || directories[0].name === undefined) {
      return [];
    }

    const byName = new Map();
    const nodes = directories.map(({ fileCount = 0, locked = false, name }) => {
      const node = {
        children: [],
        fileCount,
        locked,
        name,
        totalDirectoryCount: 0,
        totalFileCount: fileCount,
      };

      // the first directory with a given name wins, as in findDirectoryByPath
      if (!byName.has(name)) {
        byName.set(name, node);
      }

      return node;
    });

    const roots = [];
    for (const node of nodes) {
      const lastSep = node.name.lastIndexOf(separator);
      const parent =
        lastSep === -1 ? undefined : byName.get(node.name.slice(0, lastSep));

      if (parent) {
        parent.children.push(node);
      } else {
        roots.push(node);
      }
    }

    // every node is listed before its descendants in a depth-first walk, so
    // walking that order backwards totals each subtree bottom-up
    const order = [];
    const stack = [...roots];
    while (stack.length > 0) {
      const node = stack.pop();
      order.push(node);
      for (const child of node.children) {
        stack.push(child);
      }
    }

    for (let index = order.length - 1; index >= 0; index--) {
      const node = order[index];
      for (const child of node.children) {
        node.totalDirectoryCount += 1 + child.totalDirectoryCount;
        node.totalFileCount += child.totalFileCount;
      }
    }

    return roots;
  };

  // name -> node lookup, rebuilt only when the tree changes; render used to
  // walk the whole tree to find the selected directory on every update
  findDirectoryByPath = (path, tree) => {
    if (this.directoryIndex.tree !== tree) {
      const byName = new Map();
      const stack = [...tree].reverse();

      while (stack.length > 0) {
        const node = stack.pop();

        // the first match in depth-first order wins, as it did with the recursive search
        if (!byName.has(node.name)) {
          byName.set(node.name, node);
        }

        const children = node.children || [];
        for (let index = children.length - 1; index >= 0; index--) {
          stack.push(children[index]);
        }
      }

      this.directoryIndex = { byName, tree };
    }

    return this.directoryIndex.byName.get(path) ?? null;
  };

  // (event, node), as DirectoryTree calls it; a stable reference so the tree
  // doesn't re-render because this component did
  handleSelectDirectory = (_, directory) => {
    this.setState(
      {
        selected: {
          directoryName: directory.name,
          files: [],
          subdirectory: directory.children?.[0]?.name ?? null,
        },
      },
      () => this.saveState(),
    );
  };

  handleStateChange = ({ files, subdirectory }) => {
    this.setState(
      (prevState) => ({
        selected: { ...prevState.selected, files, subdirectory },
      }),
      () => this.saveState(),
    );
  };

  handleDirectoryNavigate = (path) => {
    this.directoryTreeRef.current?.navigateToDirectory(path);
  };

  renderDirectoryAction = (dir) => (
    <Icon
      link
      name="share"
      onClick={() => this.handleDirectoryNavigate(dir.name)}
      title="Navigate to this directory"
    />
  );

  handleDeselectDirectory = () => {
    this.setState({ selected: null }, () => this.saveState());
  };

  renderUsernameBar() {
    const { browseState } = this.state;
    const pending = browseState === 'pending';

    return (
      <Segment
        className="browse-segment"
        raised
      >
        <div className="browse-segment-icon">
          <Icon
            name="folder open"
            size="big"
          />
        </div>
        <Input
          action={
            !pending &&
            (browseState === 'idle'
              ? { icon: 'search', onClick: this.browse }
              : { color: 'red', icon: 'x', onClick: this.clear })
          }
          className="search-input"
          disabled={pending}
          input={
            <input
              data-lpignore="true"
              placeholder="Username"
              type="search"
            />
          }
          loading={pending}
          onKeyUp={(event) => (event.key === 'Enter' ? this.browse() : '')}
          placeholder="Username"
          ref={(input) => (this.inputtext = input)}
          size="big"
        />
      </Segment>
    );
  }

  renderTreeAndSelection(selectedDirectory) {
    const { info, selected, separator, tree, username } = this.state;

    return (
      <div className="browse-container">
        <Card
          className="browse-tree-card"
          raised
        >
          <Card.Content>
            <Card.Header>
              <Icon
                color="green"
                name="circle"
              />
              {username}
            </Card.Header>
            <Card.Meta className="browse-meta">
              <span>{formatBrowseSummary(info)}</span>
            </Card.Meta>
            <div className="browse-tree-wrapper">
              <DirectoryTree
                onSelect={this.handleSelectDirectory}
                ref={this.directoryTreeRef}
                selectedDirectoryName={selected?.directoryName}
                tree={tree}
              />
            </div>
          </Card.Content>
        </Card>
        {selectedDirectory && (
          <Selection
            defaultSelectedFiles={selected.files}
            defaultSubdirectory={selected.subdirectory}
            directorySuffix={this.renderDirectoryAction}
            locked={selectedDirectory.locked}
            name={selected.directoryName}
            node={selectedDirectory}
            onBrowseAgain={this.handleBrowseAgain}
            onClose={this.handleDeselectDirectory}
            onStateChange={this.handleStateChange}
            separator={separator}
            username={username}
          />
        )}
      </div>
    );
  }

  renderResults(selectedDirectory) {
    const { browseError, browseLoading, tree, username } = this.state;

    if (browseError) {
      return <span className="browse-error">Failed to browse {username}</span>;
    }

    const emptyTree = !(tree && tree.length > 0);
    if (emptyTree) {
      return browseLoading ? (
        <Loader
          active
          className="search-loader"
          inline="centered"
          size="big"
        >
          Loading saved results
        </Loader>
      ) : (
        <PlaceholderSegment
          caption="User is not sharing any files"
          icon="folder open"
        />
      );
    }

    return this.renderTreeAndSelection(selectedDirectory);
  }

  render() {
    const { browseState, browseStatus, selected, tree } = this.state;
    const selectedDirectory = selected
      ? this.findDirectoryByPath(selected.directoryName, tree)
      : null;
    const pending = browseState === 'pending';

    return (
      <div className="search-container">
        {this.renderUsernameBar()}
        {pending ? (
          <Loader
            active
            className="search-loader"
            inline="centered"
            size="big"
          >
            Downloaded {Math.round(browseStatus.percentComplete || 0)}% of
            Response
          </Loader>
        ) : (
          <div>{this.renderResults(selectedDirectory)}</div>
        )}
      </div>
    );
  }
}

export default withRouter(Browse);
