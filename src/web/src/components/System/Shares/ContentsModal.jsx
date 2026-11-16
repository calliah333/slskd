import { browse } from '../../../lib/shares';
import { LoaderSegment, Switch } from '../../Shared';
import CodeEditor from '../../Shared/CodeEditor';
import { useVirtualizer } from '@tanstack/react-virtual';
import React, { useEffect, useRef, useState } from 'react';
import { Button, Icon, Modal } from 'semantic-ui-react';

// shares with more lines than this skip CodeMirror and render a virtualized
// list instead; building and mounting a multi-megabyte document blocks the page
const MAX_EDITOR_LINES = 20_000;
const LINE_H = 20;
const SCROLL_HEIGHT = 'calc(80vh - 10em)';

const filenameCollator = new Intl.Collator();

// mirrors the @uiw/react-codemirror light and dark themes
const listThemes = {
  dark: { background: '#282c34', color: '#abb2bf' },
  light: { background: '#fff', color: '#000' },
};

const buildLines = (result, localPath, remotePath) => {
  const lines = [];

  for (const directory of result) {
    lines.push(directory.name.replace(remotePath, localPath));

    const directoryFilesOrderedByFilename = directory.files.sort(
      (file1, file2) =>
        filenameCollator.compare(file1.filename, file2.filename),
    );

    for (const file of directoryFilesOrderedByFilename) {
      lines.push('\t' + file.filename.replace(remotePath, ''));
    }

    lines.push('');
  }

  return lines;
};

const LineList = ({ lines, theme }) => {
  const parentRef = useRef(null);

  const virtualizer = useVirtualizer({
    count: lines.length,
    estimateSize: () => LINE_H,
    getScrollElement: () => parentRef.current,
    overscan: 30,
  });

  return (
    <div
      ref={parentRef}
      style={{
        ...(listThemes[theme] ?? listThemes.light),
        fontFamily: 'monospace',
        height: SCROLL_HEIGHT,
        overflow: 'auto',
      }}
    >
      <div
        style={{
          height: virtualizer.getTotalSize(),
          position: 'relative',
        }}
      >
        {virtualizer.getVirtualItems().map((vi) => (
          <div
            key={vi.key}
            style={{
              height: LINE_H,
              left: 0,
              lineHeight: `${LINE_H}px`,
              paddingLeft: 6,
              paddingRight: 6,
              position: 'absolute',
              tabSize: 4,
              top: vi.start,
              whiteSpace: 'pre',
            }}
          >
            {lines[vi.index]}
          </div>
        ))}
      </div>
    </div>
  );
};

const ContentsModal = ({ onClose, share, theme }) => {
  const [loading, setLoading] = useState(true);
  const [contents, setContents] = useState();

  const { id, localPath, remotePath } = share || {};

  useEffect(() => {
    const fetch = async () => {
      setLoading(true);

      let result;

      try {
        result = await browse({ id });
      } catch (error) {
        console.error(error);
        setContents(error?.response?.data ?? error?.message ?? String(error));
        setLoading(false);
        return;
      }

      const lines = buildLines(result, localPath, remotePath);

      setContents(lines.length > MAX_EDITOR_LINES ? lines : lines.join('\n'));
      setLoading(false);
    };

    if (id) {
      fetch();
    } else {
      setLoading(true);
      setContents();
    }
  }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  const large = Array.isArray(contents);

  return (
    <Modal
      onClose={onClose}
      open={share}
      size="large"
    >
      <Modal.Header>
        <Icon name="folder" />
        {localPath}
      </Modal.Header>
      <Modal.Content
        className="share-ls-content"
        scrolling={!large}
      >
        <Switch loading={loading && <LoaderSegment className="modal-loader" />}>
          {large ? (
            <LineList
              lines={contents}
              theme={theme}
            />
          ) : (
            <CodeEditor
              basicSetup={false}
              editable={false}
              style={{ minHeight: 500 }}
              theme={theme}
              value={contents || ''}
            />
          )}
        </Switch>
      </Modal.Content>
      <Modal.Actions>
        <Button onClick={onClose}>Close</Button>
      </Modal.Actions>
    </Modal>
  );
};

export default ContentsModal;
