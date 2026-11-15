import './Users.css';
import { activeUserInfoKey } from '../../config';
import * as users from '../../lib/users';
import PlaceholderSegment from '../Shared/PlaceholderSegment';
import User from './User';
import React, { useEffect, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { Icon, Input, Item, Loader, Segment } from 'semantic-ui-react';

const getError = (error) => error?.response?.data ?? error?.message ?? error;

const Users = () => {
  const location = useLocation();
  const inputRef = useRef();
  const [usernameInput, setUsernameInput] = useState();
  // the nonce makes looking up the same username again (e.g. after an error) refetch
  const [lookup, setLookup] = useState({ nonce: 0, username: undefined });
  const [result, setResult] = useState(undefined);

  const setInputText = (text) => {
    inputRef.current.inputRef.current.value = text;
  };

  const setInputFocus = () => {
    inputRef.current.focus();
  };

  const lookUp = (username) => {
    const trimmed = username?.trim();

    if (trimmed) {
      setLookup(({ nonce }) => ({ nonce: nonce + 1, username: trimmed }));
    }
  };

  const clear = () => {
    localStorage.removeItem(activeUserInfoKey);
    setLookup(({ nonce }) => ({ nonce: nonce + 1, username: undefined }));
    setResult(undefined);
    setInputText('');
    setInputFocus();
  };

  const keyUp = (event) => (event.key === 'Escape' ? clear() : '');

  useEffect(() => {
    document.addEventListener('keyup', keyUp, false);

    const storedUsername =
      location.state?.user || localStorage.getItem(activeUserInfoKey);

    if (storedUsername) {
      lookUp(storedUsername);
      setInputText(storedUsername);
    }

    return () => {
      document.removeEventListener('keyup', keyUp, false);
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const { username } = lookup;

    if (!username) {
      return undefined;
    }

    // responses for a previous lookup must not overwrite this one
    let current = true;
    const update = (patch) => {
      if (current) {
        setResult((previous) => ({ ...previous, ...patch }));
      }
    };

    setResult({
      endpoint: undefined,
      info: undefined,
      infoError: undefined,
      infoPending: true,
      status: undefined,
      statusError: undefined,
      statusPending: true,
      username,
    });

    // status and endpoint come from the server and are fast; info requires a connection to the
    // peer and can take seconds or fail when the user is offline. each is shown as soon as it
    // arrives, and a failure of one doesn't hide the others
    (async () => {
      try {
        const status = await users.getStatus({ username });
        localStorage.setItem(activeUserInfoKey, username);
        update({ status: status.data, statusPending: false });
      } catch (error) {
        update({ statusError: getError(error), statusPending: false });
      }
    })();

    (async () => {
      try {
        const endpoint = await users.getEndpoint({ username });
        update({ endpoint: endpoint.data });
      } catch {
        // offline users have no endpoint; info explains why
      }
    })();

    (async () => {
      try {
        const info = await users.getInfo({ username });
        update({ info: info.data, infoPending: false });
      } catch (error) {
        update({ infoError: getError(error), infoPending: false });
      }
    })();

    return () => {
      current = false;
    };
  }, [lookup]);

  const statusPending = result?.statusPending ?? false;
  const hasUser = result?.status !== undefined;

  return (
    <div className="users-container">
      <Segment
        className="users-segment"
        raised
      >
        <div className="users-segment-icon">
          <Icon
            name="users"
            size="big"
          />
        </div>
        <Input
          action={
            !statusPending &&
            (hasUser
              ? { color: 'red', icon: 'x', onClick: clear }
              : {
                  icon: 'search',
                  onClick: () => lookUp(usernameInput),
                })
          }
          className="users-input"
          disabled={statusPending}
          input={
            <input
              data-lpignore="true"
              disabled={hasUser || statusPending}
              placeholder="Username"
              type="search"
            />
          }
          loading={statusPending}
          onChange={(event) => setUsernameInput(event.target.value)}
          onKeyUp={(event) =>
            event.key === 'Enter' ? lookUp(event.target.value) : ''
          }
          placeholder="Username"
          ref={inputRef}
          size="big"
        />
      </Segment>
      {statusPending ? (
        <Loader
          active
          className="search-loader"
          inline="centered"
          size="big"
        />
      ) : (
        <div>
          {result?.statusError ? (
            <span>
              Failed to retrieve information for {result.username}:{' '}
              {String(result.statusError)}
            </span>
          ) : hasUser ? (
            <Segment
              className="users-user"
              raised
            >
              <Item.Group>
                <User
                  {...result.info}
                  {...result.status}
                  {...result.endpoint}
                  infoError={result.infoError}
                  infoPending={result.infoPending}
                  username={result.username}
                />
              </Item.Group>
            </Segment>
          ) : (
            <PlaceholderSegment
              caption="No user info to display"
              icon="users"
            />
          )}
        </div>
      )}
    </div>
  );
};

export default Users;
