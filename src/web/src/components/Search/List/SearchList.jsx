import ErrorSegment from '../../Shared/ErrorSegment';
import Switch from '../../Shared/Switch';
import SearchListRow from './SearchListRow';
import React, { useMemo } from 'react';
import { Card, Icon, Loader, Table } from 'semantic-ui-react';

const SearchList = ({
  connecting = false,
  error = undefined,
  onRemove = () => {},
  onStop = () => {},
  searches = {},
}) => {
  // hub updates replace one search at a time (~2/s while one runs) without
  // changing which searches exist, and a search's start time never changes,
  // so only re-sort when searches are added or removed
  const ids = Object.keys(searches);
  const idsKey = ids.join(',');

  const order = useMemo(
    () =>
      ids
        .map((id) => [id, Date.parse(searches[id].startedAt)])
        .sort((a, b) => b[1] - a[1])
        .map(([id]) => id),
    [idsKey], // eslint-disable-line react-hooks/exhaustive-deps
  );

  return (
    <Card
      className="search-list-card"
      raised
    >
      <Card.Content>
        <div className="search-list-wrapper">
          <Switch
            connecting={
              connecting && (
                <Loader
                  active
                  inline="centered"
                  size="small"
                />
              )
            }
            error={error && <ErrorSegment caption={error} />}
          >
            <Table
              className="unstackable"
              size="large"
            >
              <Table.Header>
                <Table.Row>
                  <Table.HeaderCell className="search-list-action">
                    <Icon name="info circle" />
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-phrase">
                    Search
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-files">
                    Files
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-locked">
                    Locked
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-responses">
                    Responses
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-started">
                    Ended
                  </Table.HeaderCell>
                  <Table.HeaderCell className="search-list-action" />
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {order.map((id) => (
                  <SearchListRow
                    key={id}
                    onRemove={onRemove}
                    onStop={onStop}
                    search={searches[id]}
                  />
                ))}
              </Table.Body>
            </Table>
          </Switch>
        </div>
      </Card.Content>
    </Card>
  );
};

export default SearchList;
