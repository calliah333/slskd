import { getResponsePage } from '../../../lib/searches';
import ErrorSegment from '../../Shared/ErrorSegment';
import LoaderSegment from '../../Shared/LoaderSegment';
import Switch from '../../Shared/Switch';
import Response from '../Response';
import SearchDetailHeader from './SearchDetailHeader';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Checkbox, Dropdown, Input, Segment } from 'semantic-ui-react';

const sortDropdownOptions = [
  {
    key: 'uploadSpeed',
    text: 'Upload Speed (Fastest to Slowest)',
    value: 'uploadSpeed',
  },
  {
    key: 'queueLength',
    text: 'Queue Depth (Least to Most)',
    value: 'queueLength',
  },
];

// responses are filtered, sorted and paged by the server; fetch this many at a time
const pageSize = 5;

const emptyResults = { matchedCount: 0, responses: [], totalCount: 0 };

const SearchDetail = ({
  creating,
  disabled,
  onCreate,
  onRemove,
  onStop,
  removing,
  search,
  stopping,
}) => {
  const { fileCount, id, isComplete, lockedFileCount, responseCount, state } =
    search;

  const [fetched, setFetched] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState(undefined);

  // the responses loaded so far (a prefix of the matching responses, in order), and the counts for the current query
  const [results, setResults] = useState(emptyResults);

  // filters and sorting options
  const [resultSort, setResultSort] = useState('uploadSpeed');
  const [hideLocked, setHideLocked] = useState(true);
  const [hideNoFreeSlots, setHideNoFreeSlots] = useState(false);
  const [foldResults, setFoldResults] = useState(false);
  const [resultFilters, setResultFilters] = useState('');
  const [appliedResultFilters, setAppliedResultFilters] = useState('');

  // hiding a user drops their response locally, without refetching; later
  // requests exclude them so paging picks up where the loaded responses end
  const hiddenUsernames = useRef([]);

  // bumped whenever loaded responses are discarded, so that responses to
  // requests made before then are ignored
  const generation = useRef(0);

  const getPage = useCallback(
    (offset) =>
      getResponsePage({
        filter: appliedResultFilters,
        hiddenUsernames: hiddenUsernames.current,
        hideLocked,
        hideNoFreeSlots,
        id,
        limit: pageSize,
        offset,
        sortBy: resultSort,
      }),
    [appliedResultFilters, hideLocked, hideNoFreeSlots, id, resultSort],
  );

  // when a user uses the action buttons, we will *probably* re-use this component,
  // but with a new search ID.  clear everything to prepare for the transition
  const reset = useCallback(() => {
    generation.current += 1;
    hiddenUsernames.current = [];
    setFetched(false);
    setLoadingMore(false);
    setError(undefined);
    setResults(emptyResults);
  }, []);

  // navigating to another search may re-use this component
  const previousId = useRef(id);

  useEffect(() => {
    if (previousId.current !== id) {
      previousId.current = id;
      reset();
    }
  }, [id, reset]);

  // fetch the first page once the search is complete, and again whenever the
  // query changes.  the previous results stay up until the new ones arrive.
  useEffect(() => {
    if (!isComplete) {
      return;
    }

    generation.current += 1;
    const current = generation.current;

    const get = async () => {
      try {
        const page = await getPage(0);

        if (generation.current === current) {
          setResults(page);
          setFetched(true);
        }
      } catch (getError) {
        if (generation.current === current) {
          setError(getError);
        }
      }
    };

    get();
  }, [getPage, isComplete]);

  // filtering scans every file of every response on the server; wait for
  // typing to pause instead of querying on each keystroke
  useEffect(() => {
    const timer = setTimeout(() => setAppliedResultFilters(resultFilters), 200);
    return () => clearTimeout(timer);
  }, [resultFilters]);

  const showMore = async () => {
    const current = generation.current;
    setLoadingMore(true);

    try {
      const page = await getPage(results.responses.length);

      if (generation.current === current) {
        setResults((previous) => ({
          ...page,
          responses: [...previous.responses, ...page.responses],
        }));
      }
    } catch (getError) {
      if (generation.current === current) {
        setError(getError);
      }
    } finally {
      // only one request at a time (the button is disabled), so always clear it
      setLoadingMore(false);
    }
  };

  const hideResult = useCallback((username) => {
    hiddenUsernames.current = [...hiddenUsernames.current, username];

    setResults((previous) => {
      const responses = previous.responses.filter(
        (r) => r.username !== username,
      );

      return {
        ...previous,
        matchedCount:
          previous.matchedCount -
          (previous.responses.length - responses.length),
        responses,
      };
    });
  }, []);

  const create = async ({ navigate, search: searchForCreate }) => {
    reset();
    onCreate({ navigate, search: searchForCreate });
  };

  const remove = async () => {
    reset();
    onRemove(search);
  };

  const loading = isComplete && !fetched;
  const filteredCount = results.totalCount - results.matchedCount;
  const remainingCount = results.matchedCount - results.responses.length;
  const loaded = !removing && !creating && !loading;

  if (error) {
    return <ErrorSegment caption={error?.message ?? error} />;
  }

  return (
    <>
      <SearchDetailHeader
        creating={creating}
        disabled={disabled}
        loaded={loaded}
        loading={loading}
        onCreate={create}
        onRemove={remove}
        onStop={onStop}
        removing={removing}
        search={search}
        stopping={stopping}
      />
      <Switch
        loading={loading && <LoaderSegment />}
        searching={
          !isComplete && (
            <LoaderSegment>
              {state === 'InProgress'
                ? `Found ${fileCount} files ${
                    lockedFileCount > 0
                      ? `(plus ${lockedFileCount} locked) `
                      : ''
                  }from ${responseCount} users`
                : 'Loading results...'}
            </LoaderSegment>
          )
        }
      >
        {loaded && (
          <Segment
            className="search-options"
            raised
          >
            <Dropdown
              button
              className="search-options-sort icon"
              floating
              icon="sort"
              labeled
              onChange={(_event, { value }) => setResultSort(value)}
              options={sortDropdownOptions}
              text={
                sortDropdownOptions.find((o) => o.value === resultSort).text
              }
            />
            <div className="search-option-toggles">
              <Checkbox
                checked={hideLocked}
                className="search-options-hide-locked"
                label="Hide Locked Results"
                onChange={() => setHideLocked(!hideLocked)}
                toggle
              />
              <Checkbox
                checked={hideNoFreeSlots}
                className="search-options-hide-no-slots"
                label="Hide Results with No Free Slots"
                onChange={() => setHideNoFreeSlots(!hideNoFreeSlots)}
                toggle
              />
              <Checkbox
                checked={foldResults}
                className="search-options-fold-results"
                label="Fold Results"
                onChange={() => setFoldResults(!foldResults)}
                toggle
              />
            </div>
            <Input
              action={
                Boolean(resultFilters) && {
                  color: 'red',
                  icon: 'x',
                  onClick: () => setResultFilters(''),
                }
              }
              className="search-filter"
              label={{ content: 'Filter', icon: 'filter' }}
              onChange={(_event, data) => setResultFilters(data.value)}
              placeholder="
                lackluster container -bothersome iscbr|isvbr islossless|islossy 
                minbitrate:320 minbitdepth:24 minfilesize:10 minfilesinfolder:8 minlength:5000
              "
              value={resultFilters}
            />
          </Segment>
        )}
        {loaded &&
          results.responses.map((r) => (
            <Response
              disabled={disabled}
              isInitiallyFolded={foldResults}
              key={r.username}
              onHide={hideResult}
              response={r}
            />
          ))}
        {loaded &&
          (remainingCount > 0 ? (
            <Button
              className="showmore-button"
              disabled={loadingMore}
              fluid
              loading={loadingMore}
              onClick={showMore}
              primary
              size="large"
            >
              Show {remainingCount > pageSize ? pageSize : remainingCount} More
              Results{' '}
              {`(${remainingCount} remaining, ${filteredCount} hidden by filter(s))`}
            </Button>
          ) : filteredCount > 0 ? (
            <Button
              className="showmore-button"
              disabled
              fluid
              size="large"
            >{`All results shown. ${filteredCount} results hidden by filter(s)`}</Button>
          ) : (
            ''
          ))}
      </Switch>
    </>
  );
};

// the parent re-renders on every hub update for any search (~2/s while one runs)
export default React.memo(SearchDetail);
