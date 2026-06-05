import { useEffect, useState } from "react";

/** Returns a timestamp that advances every `intervalMs`, so relative-time
 *  cells re-render without re-fetching. Pauses when the tab is hidden. */
export function useTicker(intervalMs = 5_000): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    let id: number;
    const start = () => {
      id = window.setInterval(() => setNow(Date.now()), intervalMs);
    };
    const stop = () => window.clearInterval(id);

    const onVisibility = () => {
      stop();
      if (document.visibilityState === "visible") {
        setNow(Date.now());
        start();
      }
    };

    start();
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      stop();
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [intervalMs]);

  return now;
}
