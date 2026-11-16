import 'react-toastify/dist/ReactToastify.css';
import './App.css';
// route stylesheets stay in the main bundle even though their routes are lazy:
// together they're ~13 KB, some routes use classes from another route's sheet
// (Browse renders Search.css classes), and this keeps the cascade order unchanged
import './Browse/Browse.css';
import './Chat/Chat.css';
import './Dashboard/Dashboard.css';
import './Search/Search.css';
import './Rooms/Rooms.css';
import './System/System.css';
import './System/Files/Files.css';
import './Transfers/Transfers.css';
import './Users/Users.css';
import { urlBase } from '../config';
import {
  createApplicationHubConnection,
  createMetricsHubConnection,
} from '../lib/hubFactory';
import * as relayAPI from '../lib/relay';
import { connect, disconnect } from '../lib/server';
import * as session from '../lib/session';
import { isPassthroughEnabled } from '../lib/token';
import AppContext from './AppContext';
import AppFooter from './AppFooter';
import LoginForm from './LoginForm';
import ErrorSegment from './Shared/ErrorSegment';
import LoaderSegment from './Shared/LoaderSegment';
import React, {
  Component,
  lazy,
  Suspense,
  useEffect,
  useMemo,
  useRef,
} from 'react';
import { Link, Redirect, Route, Switch, useLocation } from 'react-router-dom';
import { ToastContainer } from 'react-toastify';
import {
  Button,
  Header,
  Icon,
  Loader,
  Menu,
  Modal,
  Segment,
  Sidebar,
} from 'semantic-ui-react';

const initialState = {
  applicationOptions: {},
  applicationState: {},
  // false until the application hub pushes its first state; the app renders
  // before the hubs connect, so consumers that need the state wait on this
  applicationStateReceived: false,
  error: false,
  initialized: false,
  login: {
    error: undefined,
    pending: false,
  },
  retriesExhausted: false,
  transferMetrics: {},
};

// each route is its own chunk, keyed by the first path segment after urlBase
const routeLoaders = {
  browse: () => import('./Browse/Browse'),
  chat: () => import('./Chat/Chat'),
  dashboard: () => import('./Dashboard/Dashboard'),
  downloads: () => import('./Transfers/Transfers'),
  rooms: () => import('./Rooms/Rooms'),
  searches: () => import('./Search/Searches'),
  system: () => import('./System/System'),
  uploads: () => import('./Transfers/Transfers'),
  users: () => import('./Users/Users'),
};

const Browse = lazy(routeLoaders.browse);
const Chat = lazy(routeLoaders.chat);
const Dashboard = lazy(routeLoaders.dashboard);
const Rooms = lazy(routeLoaders.rooms);
const Searches = lazy(routeLoaders.searches);
const System = lazy(routeLoaders.system);
const Transfers = lazy(routeLoaders.downloads);
const Users = lazy(routeLoaders.users);

// start fetching the current route's chunk right away so it downloads while
// the session check is in flight, instead of after it
const preloadCurrentRoute = async () => {
  const segment = window.location.pathname.slice(urlBase.length).split('/')[1];
  const load = routeLoaders[segment] ?? routeLoaders.dashboard;

  try {
    await load();
  } catch {
    // the route's own import() retries when it renders; failures surface there
  }
};

// keeps a failed chunk download (e.g. a stale tab after an upgrade) from blanking the app
class RouteErrorBoundary extends Component {
  static getDerivedStateFromError(error) {
    return { error };
  }

  // navigating away clears the error
  static getDerivedStateFromProps(props, state) {
    if (props.resetKey !== state.resetKey) {
      return { error: undefined, resetKey: props.resetKey };
    }

    return null;
  }

  constructor(props) {
    super(props);

    this.state = { error: undefined, resetKey: props.resetKey };
  }

  render() {
    if (this.state.error) {
      return (
        <ErrorSegment
          caption={`${this.state.error?.message ?? this.state.error}; refresh to try again`}
        />
      );
    }

    return this.props.children;
  }
}

// memoized so the once-per-second transfer metrics update (and anything else in
// App's state that routes don't consume) doesn't re-render the active page
const AppRoutes = React.memo(
  ({
    applicationOptions,
    applicationState,
    applicationStateReceived,
    isAgent,
    theme,
  }) => {
    const { pathname } = useLocation();
    // routes only mount once init() has validated the session (or the
    // authorized application hub has reconnected), so the first check is redundant
    const isInitialCheck = useRef(true);

    // verify the session on navigation; expired sessions are logged out
    useEffect(() => {
      if (isInitialCheck.current) {
        isInitialCheck.current = false;
        return;
      }

      const check = async () => {
        try {
          await session.check();
        } catch (error) {
          console.error(error);
        }
      };

      check();
    }, [pathname]);

    const contextValue = useMemo(
      () => ({ options: applicationOptions, state: applicationState }),
      [applicationOptions, applicationState],
    );

    return (
      <AppContext.Provider value={contextValue}>
        <RouteErrorBoundary resetKey={pathname}>
          <Suspense fallback={<LoaderSegment />}>
            {isAgent ? (
              <Switch>
                <Route
                  path={`${urlBase}/system/:tab?`}
                  render={(props) => (
                    <System
                      {...props}
                      options={applicationOptions}
                      state={applicationState}
                    />
                  )}
                />
                <Redirect
                  from="*"
                  to={`${urlBase}/system`}
                />
              </Switch>
            ) : (
              <Switch>
                <Route
                  path={`${urlBase}/dashboard`}
                  render={(props) => (
                    <div className="view">
                      <Dashboard
                        server={applicationState.server}
                        {...props}
                      />
                    </div>
                  )}
                />
                <Route
                  path={`${urlBase}/searches/:id?`}
                  render={(props) => (
                    <div className="view">
                      <Searches
                        server={applicationState.server}
                        {...props}
                      />
                    </div>
                  )}
                />
                <Route
                  path={`${urlBase}/browse`}
                  render={(props) => <Browse {...props} />}
                />
                <Route
                  path={`${urlBase}/users`}
                  render={(props) => <Users {...props} />}
                />
                <Route
                  path={`${urlBase}/chat`}
                  render={(props) =>
                    // Chat reads the logged in user from the application state
                    applicationStateReceived ? (
                      <Chat
                        {...props}
                        state={applicationState}
                      />
                    ) : (
                      <LoaderSegment />
                    )
                  }
                />
                <Route
                  path={`${urlBase}/rooms`}
                  render={(props) => <Rooms {...props} />}
                />
                <Route
                  path={`${urlBase}/uploads`}
                  render={(props) => (
                    <div className="view">
                      <Transfers
                        {...props}
                        direction="upload"
                      />
                    </div>
                  )}
                />
                <Route
                  path={`${urlBase}/downloads`}
                  render={(props) => (
                    <div className="view">
                      <Transfers
                        {...props}
                        direction="download"
                        server={applicationState.server}
                      />
                    </div>
                  )}
                />
                <Route
                  path={`${urlBase}/system/:tab?`}
                  render={(props) => (
                    <System
                      {...props}
                      options={applicationOptions}
                      state={applicationState}
                      theme={theme}
                    />
                  )}
                />
                <Redirect
                  from="*"
                  to={`${urlBase}/dashboard`}
                />
              </Switch>
            )}
          </Suspense>
        </RouteErrorBoundary>
      </AppContext.Provider>
    );
  },
);

AppRoutes.displayName = 'AppRoutes';

const ModeSpecificConnectButton = ({
  connecting,
  connectionWatchdog,
  controller = {},
  mode,
  pendingReconnect,
  server,
  user,
}) => {
  // the application hub hasn't delivered the state yet, so neither the mode nor
  // the server connection is known
  if (connecting) {
    return (
      <Menu.Item>
        <Icon.Group className="menu-icon-group">
          <Icon
            color="grey"
            name="plug"
          />
          <Icon
            className="menu-icon-no-shadow"
            color="green"
            corner="bottom right"
            name="sync alternate loading"
          />
        </Icon.Group>
        Connecting
      </Menu.Item>
    );
  }

  if (mode === 'Agent') {
    const isConnected = controller?.state === 'Connected';
    const isTransitioning = ['Connecting', 'Reconnecting'].includes(
      controller?.state,
    );

    return (
      <Menu.Item
        onClick={() =>
          isConnected ? relayAPI.disconnect() : relayAPI.connect()
        }
      >
        <Icon.Group className="menu-icon-group">
          <Icon
            color={
              controller?.state === 'Connected'
                ? 'green'
                : isTransitioning
                  ? 'yellow'
                  : 'grey'
            }
            name="plug"
          />
          {!isConnected && (
            <Icon
              className="menu-icon-no-shadow"
              color="red"
              corner="bottom right"
              name="close"
            />
          )}
        </Icon.Group>
        Controller {controller?.state}
      </Menu.Item>
    );
  } else {
    if (server?.isConnected) {
      return (
        <Menu.Item onClick={() => disconnect()}>
          <Icon.Group className="menu-icon-group">
            <Icon
              color={pendingReconnect ? 'yellow' : 'green'}
              name="plug"
            />
            {user?.privileges?.isPrivileged && (
              <Icon
                className="menu-icon-no-shadow"
                color="yellow"
                corner
                name="star"
              />
            )}
          </Icon.Group>
          Connected
        </Menu.Item>
      );
    }

    // the server is disconnected, and we need to give the user some information about what the client is doing
    // options are:
    // - nothing. the client was manually disconnected, kicked off by another login, etc., and we're not trying to connect
    // - actively trying to make a connection to the server
    // - still trying to connect, but waiting for the next connection attempt
    let icon = 'close';
    let color = 'red';

    if (connectionWatchdog?.isAttemptingConnection) {
      icon = 'clock';
      color = 'yellow';
    }

    if (server?.isConnecting || server?.isLoggingIn) {
      icon = 'sync alternate loading';
      color = 'green';
    }

    return (
      <Menu.Item onClick={() => connect()}>
        <Icon.Group className="menu-icon-group">
          <Icon
            color="grey"
            name="plug"
          />
          <Icon
            className="menu-icon-no-shadow"
            color={color}
            corner="bottom right"
            name={icon}
          />
        </Icon.Group>
        Disconnected
      </Menu.Item>
    );
  }
};

class App extends Component {
  constructor(props) {
    super(props);

    this.state = initialState;
    this.hubConnections = {};
  }

  componentDidMount() {
    if (this.getSavedTheme() == null) {
      window
        .matchMedia('(prefers-color-scheme: dark)')
        .addEventListener(
          'change',
          (event) => event.matches && this.setState({ theme: 'dark' }),
        );
      window
        .matchMedia('(prefers-color-scheme: light)')
        .addEventListener(
          'change',
          (event) => event.matches && this.setState({ theme: 'light' }),
        );
    }

    preloadCurrentRoute();
    this.init();
  }

  init = async () => {
    this.setState({ initialized: false }, async () => {
      try {
        // the session check doesn't depend on the security flag: with security
        // disabled the server authenticates every request, so run both at once
        const [securityEnabled, isSessionValid] = await Promise.all([
          session.getSecurityEnabled(),
          session.check(),
        ]);

        if (!securityEnabled) {
          console.debug('application security is not enabled, per api call');
          session.enablePassthrough();
        }

        if (isSessionValid) {
          this.connectHubs();
        }

        const savedTheme = this.getSavedTheme();

        this.setState({
          error: false,
          ...(savedTheme != null && { theme: savedTheme }),
        });
      } catch (error) {
        console.error(error);
        this.setState({ error: true, retriesExhausted: true });
      } finally {
        this.setState({ initialized: true });
      }
    });
  };

  // the hubs connect in the background so the app renders as soon as the session
  // is known to be valid; hub-pushed state fills in when it arrives
  connectHubs = () => {
    const appHub = createApplicationHubConnection();
    // a re-login replaces the hubs; events from (and the stop of) the previous
    // connections must not flag the new ones as lost
    const isCurrent = () => this.hubConnections.appHub === appHub;

    appHub.on('state', (state) => {
      this.setState({
        applicationState: state,
        applicationStateReceived: true,
      });
    });

    appHub.on('options', (options) => {
      this.setState({ applicationOptions: options });
    });

    appHub.onreconnecting(
      () =>
        isCurrent() && this.setState({ error: true, retriesExhausted: false }),
    );
    appHub.onclose(
      () =>
        isCurrent() && this.setState({ error: true, retriesExhausted: true }),
    );
    appHub.onreconnected(
      () =>
        isCurrent() && this.setState({ error: false, retriesExhausted: false }),
    );

    const metricsHub = createMetricsHubConnection();

    metricsHub.on('Update', (metrics) => {
      this.setState({ transferMetrics: metrics });
    });

    const previous = this.hubConnections;
    this.hubConnections = { appHub, metricsHub };

    // the hubs are independent; connect them concurrently
    const startHub = async (hub, previousHub) => {
      try {
        await previousHub?.stop();
        await hub.start();
      } catch (error) {
        if (isCurrent()) {
          console.error(error);
          this.setState({ error: true, retriesExhausted: true });
        }
      }
    };

    startHub(appHub, previous.appHub);
    startHub(metricsHub, previous.metricsHub);
  };

  getSavedTheme = () => {
    return localStorage.getItem('slskd-theme');
  };

  toggleTheme = () => {
    this.setState((state) => {
      const newTheme = state.theme === 'dark' ? 'light' : 'dark';
      localStorage.setItem('slskd-theme', newTheme);
      return { theme: newTheme };
    });
  };

  handleLogin = (username, password, rememberMe) => {
    this.setState(
      (previousState) => ({
        login: { ...previousState.login, error: undefined, pending: true },
      }),
      async () => {
        try {
          await session.login({ password, rememberMe, username });
          this.setState(
            (previousState) => ({
              login: { ...previousState.login, error: false, pending: false },
            }),
            () => this.init(),
          );
        } catch (error) {
          this.setState((previousState) => ({
            login: { ...previousState.login, error, pending: false },
          }));
        }
      },
    );
  };

  logout = () => {
    session.logout();

    this.hubConnections?.appHub?.stop();
    this.hubConnections?.metricsHub?.stop();

    this.setState({ login: { ...initialState.login } });
  };

  render() {
    const {
      applicationOptions = {},
      applicationState = {},
      applicationStateReceived,
      error,
      initialized,
      login,
      retriesExhausted,
      theme = this.getSavedTheme() ||
        (window.matchMedia('(prefers-color-scheme: dark)').matches
          ? 'dark'
          : 'light'),
      transferMetrics = {},
    } = this.state;
    const {
      connectionWatchdog = {},
      pendingReconnect,
      pendingRestart,
      relay = {},
      server,
      shares = {},
      user,
      version = {},
    } = applicationState;
    const { current, isUpdateAvailable, latest } = version;
    const { scanPending: pendingShareRescan } = shares;

    const { controller, mode } = relay;

    if (!initialized) {
      return (
        <Loader
          active
          size="big"
        />
      );
    }

    if (error) {
      return (
        <ErrorSegment
          caption={
            <>
              <span>Lost connection to slskd</span>
              <br />
              <span>
                {retriesExhausted ? 'Refresh to reconnect' : 'Retrying...'}
              </span>
            </>
          }
          icon="attention"
          suppressPrefix
        />
      );
    }

    if (!session.isLoggedIn() && !isPassthroughEnabled()) {
      return (
        <LoginForm
          error={login.error}
          initialized={login.initialized}
          loading={login.pending}
          onLoginAttempt={this.handleLogin}
        />
      );
    }

    const isAgent = mode === 'Agent';

    if (theme === 'dark') {
      document.documentElement.classList.add(theme);
    } else {
      document.documentElement.classList.remove('dark');
    }

    return (
      <>
        <Sidebar.Pushable
          as={Segment}
          className="app"
        >
          <Sidebar
            animation="overlay"
            as={Menu}
            className="navigation"
            direction="top"
            horizontal="true"
            icon="labeled"
            inverted
            visible
            width="thin"
          >
            {version.isCanary && (
              <Menu.Item>
                <Icon
                  color="yellow"
                  name="flask"
                />
                Canary
              </Menu.Item>
            )}
            {isAgent ? (
              <Menu.Item>
                <Icon name="detective" />
                Agent Mode
              </Menu.Item>
            ) : (
              <>
                <Link to={`${urlBase}/dashboard`}>
                  <Menu.Item>
                    <Icon name="chart bar" />
                    Dashboard
                  </Menu.Item>
                </Link>
                <Link to={`${urlBase}/searches`}>
                  <Menu.Item>
                    <Icon name="search" />
                    Search
                  </Menu.Item>
                </Link>
                <Link to={`${urlBase}/downloads`}>
                  <Menu.Item>
                    <Icon name="download" />
                    Downloads
                  </Menu.Item>
                </Link>
                <Link to={`${urlBase}/uploads`}>
                  <Menu.Item>
                    <Icon name="upload" />
                    Uploads
                  </Menu.Item>
                </Link>
                <Link to={`${urlBase}/rooms`}>
                  <Menu.Item>
                    <Icon name="comments" />
                    Rooms
                  </Menu.Item>
                </Link>
                <Link to={`${urlBase}/chat`}>
                  <Menu.Item>
                    <Icon name="comment" />
                    Chat
                  </Menu.Item>
                </Link>
                <Link to={`${urlBase}/users`}>
                  <Menu.Item>
                    <Icon name="users" />
                    Users
                  </Menu.Item>
                </Link>
                <Link to={`${urlBase}/browse`}>
                  <Menu.Item>
                    <Icon name="folder open" />
                    Browse
                  </Menu.Item>
                </Link>
              </>
            )}
            <Menu
              className="right"
              inverted
            >
              <Menu.Item onClick={() => this.toggleTheme()}>
                <Icon name="theme" />
                Theme
              </Menu.Item>
              <ModeSpecificConnectButton
                connecting={!applicationStateReceived}
                connectionWatchdog={connectionWatchdog}
                controller={controller}
                mode={mode}
                pendingReconnect={pendingReconnect}
                server={server}
                user={user}
              />
              {(pendingReconnect || pendingRestart || pendingShareRescan) && (
                <Menu.Item position="right">
                  <Icon.Group className="menu-icon-group">
                    <Link to={`${urlBase}/system/info`}>
                      <Icon
                        color="yellow"
                        name="exclamation circle"
                      />
                    </Link>
                  </Icon.Group>
                  Pending Action
                </Menu.Item>
              )}
              {isUpdateAvailable && (
                <Modal
                  centered
                  closeIcon
                  size="mini"
                  trigger={
                    <Menu.Item position="right">
                      <Icon.Group className="menu-icon-group">
                        <Icon
                          color="yellow"
                          name="bullhorn"
                        />
                      </Icon.Group>
                      New Version!
                    </Menu.Item>
                  }
                >
                  <Modal.Header>New Version!</Modal.Header>
                  <Modal.Content>
                    <p>
                      You are currently running version{' '}
                      <strong>{current}</strong> while version{' '}
                      <strong>{latest}</strong> is available.
                    </p>
                  </Modal.Content>
                  <Modal.Actions>
                    <Button
                      fluid
                      href="https://github.com/slskd/slskd/releases"
                      primary
                      style={{ marginLeft: 0 }}
                    >
                      See Release Notes
                    </Button>
                  </Modal.Actions>
                </Modal>
              )}
              <Link to={`${urlBase}/system`}>
                <Menu.Item>
                  <Icon name="cogs" />
                  System
                </Menu.Item>
              </Link>
              {session.isLoggedIn() && (
                <Modal
                  actions={[
                    'Cancel',
                    {
                      content: 'Log Out',
                      key: 'done',
                      negative: true,
                      onClick: this.logout,
                    },
                  ]}
                  centered
                  content="Are you sure you want to log out?"
                  header={
                    <Header
                      content="Confirm Log Out"
                      icon="sign-out"
                    />
                  }
                  size="mini"
                  trigger={
                    <Menu.Item>
                      <Icon name="sign-out" />
                      Log Out
                    </Menu.Item>
                  }
                />
              )}
            </Menu>
          </Sidebar>
          <Sidebar.Pusher className="app-content">
            <div className="app-content-body">
              <AppRoutes
                applicationOptions={applicationOptions}
                applicationState={applicationState}
                applicationStateReceived={applicationStateReceived}
                isAgent={isAgent}
                theme={theme}
              />
            </div>
          </Sidebar.Pusher>
        </Sidebar.Pushable>
        <AppFooter
          server={server}
          transferMetrics={transferMetrics}
          user={user}
          version={version}
        />
        <ToastContainer
          autoClose={5_000}
          closeOnClick
          draggable={false}
          hideProgressBar={false}
          newestOnTop
          pauseOnFocusLoss
          pauseOnHover
          position="bottom-center"
          rtl={false}
        />
      </>
    );
  }
}

export default App;
