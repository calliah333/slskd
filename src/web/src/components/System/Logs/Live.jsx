import '../System.css';
import { createLogsHubConnection } from '../../../lib/hubFactory';
import { LoaderSegment } from '../../Shared';
import LogTable from './LogTable';
import { Component } from 'react';

const initialState = {
  connected: false,
  logs: [],
};

const maxLogs = 500;

class Live extends Component {
  constructor(props) {
    super(props);

    this.state = initialState;
  }

  componentDidMount() {
    const logsHub = createLogsHubConnection();
    this.logsHub = logsHub;

    logsHub.on('buffer', (buffer) => {
      this.setState({
        connected: true,
        logs: buffer.reverse().slice(0, maxLogs).map(this.withId),
      });
    });

    // debug logging can emit hundreds of lines a second; collect them and render at most a few times a second
    logsHub.on('log', (log) => {
      this.pending.push(this.withId(log));

      if (this.flushTimer === undefined) {
        this.flushTimer = setTimeout(this.flush, 250);
      }
    });

    logsHub.onreconnecting(() => this.setState({ connected: false }));
    logsHub.onclose(() => this.setState({ connected: false }));
    logsHub.onreconnected(() => this.setState({ connected: true }));

    logsHub.start();
  }

  componentWillUnmount() {
    clearTimeout(this.flushTimer);
    this.logsHub?.stop();
  }

  nextId = 0;

  pending = [];

  flushTimer = undefined;

  withId = (log) => ({ ...log, id: this.nextId++ });

  flush = () => {
    const records = this.pending.reverse();
    this.pending = [];
    this.flushTimer = undefined;

    this.setState((previousState) => ({
      connected: true,
      logs: records.concat(previousState.logs).slice(0, maxLogs),
    }));
  };

  render() {
    const { connected, logs } = this.state;

    return (
      <div className="logs">
        {!connected && <LoaderSegment />}
        {connected && <LogTable logs={logs} />}
      </div>
    );
  }
}

export default Live;
