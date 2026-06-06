interface SparklineProps {
  /** Series in chronological order (oldest → newest). */
  values: number[];
  width?: number;
  height?: number;
  color?: string;
  /** Fixed upper bound; when omitted the series max is used. */
  max?: number;
}

/** Minimal SVG line chart for a metric series. No dependency, no axes — a glance
 *  at the trend. Renders a faint area fill under the line for legibility. */
export function Sparkline({
  values,
  width = 120,
  height = 32,
  color = "var(--swarm-primary)",
  max,
}: SparklineProps) {
  if (values.length === 0) {
    return (
      <svg width={width} height={height} role="img" aria-label="No data" aria-hidden />
    );
  }
  if (values.length === 1) values = [values[0], values[0]];

  const hi = max ?? Math.max(...values, 1);
  const lo = Math.min(...values, 0);
  const span = hi - lo || 1;
  const stepX = width / (values.length - 1);

  const points = values.map((v, i) => {
    const x = i * stepX;
    const y = height - ((v - lo) / span) * height;
    return [x, Number.isFinite(y) ? y : height] as const;
  });

  const line = points.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(" ");
  const area = `0,${height} ${line} ${width},${height}`;

  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-hidden>
      <polygon points={area} fill={color} opacity={0.1} />
      <polyline
        points={line}
        fill="none"
        stroke={color}
        strokeWidth={1.5}
        strokeLinejoin="round"
        strokeLinecap="round"
        vectorEffect="non-scaling-stroke"
      />
    </svg>
  );
}
