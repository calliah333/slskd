import { Div, Nbsp } from '../Shared';
import ShrinkableDropdownButton from '../Shared/ShrinkableDropdownButton';
import React, { useState } from 'react';
import { Icon, Segment } from 'semantic-ui-react';

// option values are the server-side filters accepted by the bulk endpoints
const retryOptions = [
  { key: 'errored', text: 'Errored', value: 'errored' },
  { key: 'cancelled', text: 'Cancelled', value: 'cancelled' },
  { key: 'all', text: 'All', value: 'all' },
];

const cancelOptions = [
  { key: 'all', text: 'All', value: 'all' },
  { key: 'queued', text: 'Queued', value: 'queued' },
  { key: 'inProgress', text: 'In Progress', value: 'inprogress' },
];

const removeOptions = [
  { key: 'succeeded', text: 'Succeeded', value: 'succeeded' },
  { key: 'errored', text: 'Errored', value: 'errored' },
  { key: 'cancelled', text: 'Cancelled', value: 'cancelled' },
  { key: 'completed', text: 'Completed', value: 'completed' },
];

const textOf = (options, value) =>
  options.find((option) => option.value === value)?.text ?? value;

const TransfersHeader = ({
  cancelling = false,
  counts,
  direction,
  onCancelAll,
  onRemoveAll,
  onRetryAll,
  removing = false,
  retrying = false,
  server = { isConnected: true },
}) => {
  const [removeOption, setRemoveOption] = useState('succeeded');
  const [cancelOption, setCancelOption] = useState('all');
  const [retryOption, setRetryOption] = useState('errored');

  const retryText = textOf(retryOptions, retryOption);
  const cancelText = textOf(cancelOptions, cancelOption);
  const removeText = textOf(removeOptions, removeOption);

  const empty = counts.all === 0;
  const working = retrying || cancelling || removing;

  return (
    <Segment
      className="transfers-header-segment"
      raised
    >
      <div className="transfers-segment-icon">
        <Icon
          name={direction}
          size="big"
        />
      </div>
      <Div
        className="transfers-header-buttons"
        hidden={empty}
      >
        <ShrinkableDropdownButton
          color="green"
          disabled={
            working ||
            counts.errored + counts.cancelled === 0 ||
            !server.isConnected
          }
          hidden={direction === 'upload'}
          icon="redo"
          loading={retrying}
          mediaQuery="(max-width: 715px)"
          onChange={(_, data) => setRetryOption(data.value)}
          onClick={() => onRetryAll(retryOption)}
          options={retryOptions}
        >
          {`Retry ${retryOption === 'all' ? retryText : `All ${retryText}`}`}
        </ShrinkableDropdownButton>
        <Nbsp />
        <ShrinkableDropdownButton
          color="red"
          disabled={working || counts.active === 0}
          icon="x"
          loading={cancelling}
          mediaQuery="(max-width: 715px)"
          onChange={(_, data) => setCancelOption(data.value)}
          onClick={() => onCancelAll(cancelOption)}
          options={cancelOptions}
        >
          {`Cancel ${cancelOption === 'all' ? cancelText : `All ${cancelText}`}`}
        </ShrinkableDropdownButton>
        <Nbsp />
        <ShrinkableDropdownButton
          disabled={working || counts.completed === 0}
          icon="trash alternate"
          loading={removing}
          mediaQuery="(max-width: 715px)"
          onChange={(_, data) => setRemoveOption(data.value)}
          onClick={() => onRemoveAll(removeOption)}
          options={removeOptions}
        >
          {`Remove All ${removeText}`}
        </ShrinkableDropdownButton>
      </Div>
    </Segment>
  );
};

export default TransfersHeader;
