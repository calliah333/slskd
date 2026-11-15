import { useEffect, useRef } from 'react';

// calls `task` every `interval` milliseconds, with two guarantees setInterval doesn't give:
//   - calls never overlap; the next one is scheduled only after the previous one settles
//   - nothing runs while the document is hidden; becoming visible again triggers an immediate call
// errors thrown by `task` are logged and polling continues.
// returns a function that stops polling.
export const poll = (task, interval, { immediate = true } = {}) => {
  let stopped = false;
  let running = false;
  let timer;

  const cancelTimer = () => {
    clearTimeout(timer);
    timer = undefined;
  };

  const runTask = async () => {
    try {
      await task();
    } catch (error) {
      console.error(error);
    }
  };

  const tick = async () => {
    // a plain function so the post-await state update isn't flagged as a race;
    // running is only ever touched synchronously on the JS thread
    const settle = () => {
      running = false;

      if (!stopped && !document.hidden) {
        timer = setTimeout(tick, interval);
      }
    };

    cancelTimer();

    if (stopped || running || document.hidden) {
      return;
    }

    running = true;
    await runTask();
    settle();
  };

  const onVisibilityChange = () => {
    if (document.hidden) {
      cancelTimer();
    } else {
      tick();
    }
  };

  document.addEventListener('visibilitychange', onVisibilityChange);

  if (immediate) {
    tick();
  } else if (!document.hidden) {
    timer = setTimeout(tick, interval);
  }

  return () => {
    stopped = true;
    cancelTimer();
    document.removeEventListener('visibilitychange', onVisibilityChange);
  };
};

// hook form of poll(); always invokes the latest `callback`, and restarts
// only when `interval` or `enabled` change
export const usePolling = (
  callback,
  interval,
  { enabled = true, immediate = true } = {},
) => {
  const callbackRef = useRef(callback);
  callbackRef.current = callback;

  useEffect(() => {
    if (!enabled) {
      return undefined;
    }

    return poll(() => callbackRef.current(), interval, { immediate });
  }, [enabled, immediate, interval]);
};
