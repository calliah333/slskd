// must run first: sets where lazily loaded chunks are fetched from
// eslint-disable-next-line import/no-unassigned-import
import './publicPath';
import 'semantic-ui-less/semantic.less';
import App from './components/App';
import React from 'react';
import ReactDOM from 'react-dom';
import { BrowserRouter as Router } from 'react-router-dom';

ReactDOM.render(
  <Router>
    <App />
  </Router>,
  document.querySelector('#root'),
);
