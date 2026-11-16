import { list } from '../../../lib/events';
import { LoaderSegment } from '../../Shared';
import React, { useEffect, useState } from 'react';
import { Icon, Loader, Pagination, Popup, Table } from 'semantic-ui-react';

const PER_PAGE = 25;

const replaceHyphensWithNonBreakingEquivalent = (string) =>
  string?.replaceAll('-', '‑');

const Events = () => {
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(0);
  const [loading, setLoading] = useState(false);
  const [events, setEvents] = useState(undefined);

  useEffect(() => {
    // a slow response for a page the user already left must not overwrite the current one
    let current = true;

    const fetch = async () => {
      setLoading(true);

      try {
        const { events: items, totalCount } = await list({
          limit: PER_PAGE,
          offset: (page - 1) * PER_PAGE,
        });

        if (!current) return;

        const tp = Math.ceil(totalCount / PER_PAGE);

        setEvents(items);
        setTotalPages(Number.isNaN(tp) ? 0 : tp);
      } catch (error) {
        console.error(error);

        if (current) {
          setEvents([]);
          setTotalPages(0);
        }
      } finally {
        if (current) setLoading(false);
      }
    };

    fetch();

    return () => {
      current = false;
    };
  }, [page]);

  // only the first load replaces the view; paging keeps the current rows visible until the next arrive
  if (events === undefined) {
    return <LoaderSegment />;
  }

  return (
    <>
      <div className="header-buttons">
        <Loader
          active={loading}
          inline
          size="small"
        />
        <Pagination
          activePage={page}
          className="header-buttons"
          onPageChange={(_, { activePage }) =>
            activePage >= 1 && setPage(activePage)
          }
          totalPages={totalPages}
        />
      </div>
      <Table
        className="events-table, unstackable"
        compact="very"
      >
        <Table.Header>
          <Table.Row>
            <Table.HeaderCell className="events-list-id">Id</Table.HeaderCell>
            <Table.HeaderCell className="events-list-timestamp">
              Timestamp
            </Table.HeaderCell>
            <Table.HeaderCell className="events-list-type">
              Type
            </Table.HeaderCell>
            <Table.HeaderCell className="events-list-data">
              Data
            </Table.HeaderCell>
          </Table.Row>
        </Table.Header>
        <Table.Body className="events-table-body">
          {events?.length === 0 ? (
            <Table.Row>
              <Table.Cell
                colSpan={99}
                style={{
                  opacity: 0.5,
                  padding: '10px !important',
                  textAlign: 'center',
                }}
              >
                No events
              </Table.Cell>
            </Table.Row>
          ) : (
            events.map((event) => (
              <Table.Row key={event.id}>
                <Table.Cell>
                  <Popup
                    content={event.id}
                    on="hover"
                    style={{ fontFamily: 'monospace', width: '400px' }}
                    trigger={<Icon name="info circle" />}
                    wide="very"
                  />
                </Table.Cell>
                <Table.Cell>
                  {replaceHyphensWithNonBreakingEquivalent(event.timestamp)}
                </Table.Cell>
                <Table.Cell>{event.type}</Table.Cell>
                <Table.Cell className="events-table-data">
                  {JSON.stringify(JSON.parse(event.data), null, 2)}
                </Table.Cell>
              </Table.Row>
            ))
          )}
        </Table.Body>
      </Table>
    </>
  );
};

export default Events;
