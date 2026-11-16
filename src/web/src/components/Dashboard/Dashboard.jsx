import './Dashboard.css';
import * as reports from '../../lib/reports';
import HistoricalStatistics from './HistoricalStatistics';
import SearchBar from './SearchBar';
import React, { useEffect, useMemo, useState } from 'react';

const HISTORY_RANGES = [
  { label: '24h', days: 1, buckets: 24 },
  { label: '7d', days: 7, buckets: 84 },
  { label: '30d', days: 30, buckets: 60 },
  { label: '90d', days: 90, buckets: 90 },
  { label: '180d', days: 180, buckets: 90 },
  { label: '1y', days: 365, buckets: 100 },
  { label: 'All', days: null, buckets: 100 },
];

// shared empty values keep prop identities stable while a request is pending,
// so sections that memoize on them don't recompute when a sibling arrives
const EMPTY_LIST = [];
const EMPTY_OBJECT = {};

// one entry per report; each section renders as soon as its own requests settle
const REPORTS = [
  {
    fallback: EMPTY_OBJECT,
    fetch: ({ end, start }) => reports.getSummary({ end, start }),
    key: 'summary',
  },
  {
    fallback: EMPTY_OBJECT,
    fetch: ({ buckets, end, start }) =>
      reports.getHistogram({ buckets, end, start }),
    key: 'histogram',
  },
  {
    fallback: EMPTY_LIST,
    fetch: ({ end, start }) =>
      reports.getLeaderboard({ direction: 'Upload', end, start }),
    key: 'uploadLeaderboard',
  },
  {
    fallback: EMPTY_LIST,
    fetch: ({ end, start }) =>
      reports.getLeaderboard({ direction: 'Download', end, start }),
    key: 'downloadLeaderboard',
  },
  {
    fallback: EMPTY_LIST,
    fetch: ({ end, start }) => reports.getTopDirectories({ end, start }),
    key: 'directories',
  },
  {
    fallback: EMPTY_LIST,
    fetch: ({ end, start }) =>
      reports.getExceptionPareto({ direction: 'Upload', end, start }),
    key: 'uploadPareto',
  },
  {
    fallback: EMPTY_LIST,
    fetch: ({ end, start }) =>
      reports.getExceptionPareto({ direction: 'Download', end, start }),
    key: 'downloadPareto',
  },
  {
    fallback: EMPTY_LIST,
    fetch: ({ end, start }) =>
      reports.getExceptions({ direction: 'Upload', end, start }),
    key: 'uploadRecent',
  },
  {
    fallback: EMPTY_LIST,
    fetch: ({ end, start }) =>
      reports.getExceptions({ direction: 'Download', end, start }),
    key: 'downloadRecent',
  },
];

const Dashboard = ({ server } = {}) => {
  const [historyLabel, setHistoryLabel] = useState('30d');
  const [historyTab, setHistoryTab] = useState(0);
  // responses are tagged with the parameters they were fetched for; values from
  // any other range are treated as pending, so a range change never shows stale data
  const [results, setResults] = useState({ parameters: null, values: {} });

  const historyParameters = useMemo(() => {
    const range =
      HISTORY_RANGES.find((r) => r.label === historyLabel) ?? HISTORY_RANGES[2];
    const now = new Date();
    return {
      buckets: range.buckets,
      end: now.toISOString(),
      start:
        range.days != null
          ? new Date(now - range.days * 86_400_000).toISOString()
          : new Date(0).toISOString(),
    };
  }, [historyLabel]);

  useEffect(() => {
    // cleared when the range changes (or the dashboard unmounts) so late
    // responses for the previous range are dropped
    let active = true;

    const parameters = {
      buckets: historyParameters.buckets,
      end: new Date(historyParameters.end),
      start: historyParameters.start
        ? new Date(historyParameters.start)
        : undefined,
    };

    const load = async ({ fallback, fetch, key }) => {
      let value;

      try {
        value = await fetch(parameters);
      } catch (error) {
        console.error(error);
        value = fallback;
      }

      if (!active) return;

      setResults((previous) => ({
        parameters: historyParameters,
        values: {
          ...(previous.parameters === historyParameters ? previous.values : {}),
          [key]: value,
        },
      }));
    };

    for (const report of REPORTS) {
      load(report);
    }

    return () => {
      active = false;
    };
  }, [historyParameters]);

  const { data, loading } = useMemo(() => {
    const values =
      results.parameters === historyParameters ? results.values : {};
    const pending = (key) => !(key in values);
    const value = (key) =>
      values[key] ?? REPORTS.find((report) => report.key === key).fallback;

    return {
      data: {
        directories: value('directories'),
        exceptions: {
          download: {
            pareto: value('downloadPareto'),
            recent: value('downloadRecent'),
          },
          upload: {
            pareto: value('uploadPareto'),
            recent: value('uploadRecent'),
          },
        },
        histogram: value('histogram'),
        leaderboard: {
          download: value('downloadLeaderboard'),
          upload: value('uploadLeaderboard'),
        },
        summary: value('summary'),
      },
      loading: {
        directories: pending('directories'),
        exceptions: {
          pareto: pending('uploadPareto') || pending('downloadPareto'),
          recent: pending('uploadRecent') || pending('downloadRecent'),
        },
        histogram: pending('histogram'),
        leaderboard: {
          download: pending('downloadLeaderboard'),
          upload: pending('uploadLeaderboard'),
        },
        summary: pending('summary'),
      },
    };
  }, [historyParameters, results]);

  return (
    <div className="dashboard">
      <SearchBar server={server} />
      <HistoricalStatistics
        activeTab={historyTab}
        directories={data.directories}
        exceptions={data.exceptions}
        histogram={data.histogram}
        historyEnd={historyParameters.end}
        historyLabel={historyLabel}
        historyRanges={HISTORY_RANGES}
        historyStart={historyParameters.start}
        leaderboard={data.leaderboard}
        loading={loading}
        onHistoryRangeSelect={(label) => setHistoryLabel(label)}
        onTabChange={setHistoryTab}
        summary={data.summary}
      />
    </div>
  );
};

export default Dashboard;
