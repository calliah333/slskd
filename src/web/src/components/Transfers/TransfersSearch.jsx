import React from 'react';
import { Button, Icon, Input, Label } from 'semantic-ui-react';

/**
 * The search box, the active user filter, and the breakdown toggle shown in the transfers header.
 */
const TransfersSearch = ({
  direction,
  onClearUser,
  onSearchTextChange,
  onToggleBreakdown,
  searchText,
  showBreakdown,
  username,
}) => {
  const preposition = direction === 'upload' ? 'to' : 'from';

  return (
    <div className="transfers-search">
      <Input
        action={
          searchText
            ? {
                icon: 'x',
                onClick: () => onSearchTextChange(''),
                title: 'Clear search',
              }
            : undefined
        }
        className="transfers-search-input"
        icon={searchText ? undefined : 'search'}
        iconPosition={searchText ? undefined : 'left'}
        onChange={(_, { value }) => onSearchTextChange(value)}
        placeholder="Filter by file, folder, or user"
        value={searchText}
      />
      {username && (
        <Label
          className="transfers-user-label"
          size="large"
          title={`Only ${direction}s ${preposition} ${username}`}
        >
          <Icon name="user" />
          {username}
          <Icon
            name="delete"
            onClick={onClearUser}
          />
        </Label>
      )}
      <Button
        active={showBreakdown}
        content="Breakdown"
        icon="chart bar"
        onClick={onToggleBreakdown}
        title="Who transfers what, grouped by folder, file, or user"
      />
    </div>
  );
};

export default TransfersSearch;
