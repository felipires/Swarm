import type { SVGProps } from "react";

type IconProps = SVGProps<SVGSVGElement>;

const defaults: IconProps = {
  width: 18,
  height: 18,
  viewBox: "0 0 18 18",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 1.5,
  strokeLinecap: "round",
  strokeLinejoin: "round",
  "aria-hidden": true,
};

export function IconOverview(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <rect x="2.5" y="2.5" width="5.5" height="5.5" rx="1" />
      <rect x="10" y="2.5" width="5.5" height="5.5" rx="1" />
      <rect x="2.5" y="10" width="5.5" height="5.5" rx="1" />
      <rect x="10" y="10" width="5.5" height="5.5" rx="1" />
    </svg>
  );
}

export function IconWorkflows(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <circle cx="4.5" cy="9" r="2" />
      <circle cx="13.5" cy="4.5" r="2" />
      <circle cx="13.5" cy="13.5" r="2" />
      <path d="M6.3 8.1L11.2 5.4M6.3 9.9l4.9 2.7" />
    </svg>
  );
}

export function IconObservability(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <path d="M2.5 13.5L6.5 9l3 2.5L11.5 8l4 5.5" />
      <path d="M2.5 13.5h13" />
    </svg>
  );
}

export function IconSettings(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <circle cx="9" cy="9" r="2.25" />
      <path d="M9 2.5v1.5M9 14v1.5M2.5 9h1.5M14 9h1.5M4.4 4.4l1.1 1.1M12.5 12.5l1.1 1.1M4.4 13.6l1.1-1.1M12.5 5.5l1.1-1.1" />
    </svg>
  );
}

export function IconAlert(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <path d="M9 3.5l5.5 9.5H3.5L9 3.5z" />
      <path d="M9 8v2.5M9 12.5h.01" />
    </svg>
  );
}

export function IconSearch(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <circle cx="8" cy="8" r="4.25" />
      <path d="M11.25 11.25L15 15" />
    </svg>
  );
}

export function IconCluster(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <circle cx="9" cy="5" r="2" />
      <circle cx="4.5" cy="13" r="2" />
      <circle cx="13.5" cy="13" r="2" />
      <path d="M8.1 6.6L5.4 11.2M9.9 6.6l2.7 4.6" />
    </svg>
  );
}

export function IconNodes(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <rect x="3" y="4" width="12" height="3" rx="0.75" />
      <rect x="3" y="8.5" width="12" height="3" rx="0.75" />
      <rect x="3" y="13" width="12" height="3" rx="0.75" />
    </svg>
  );
}

export function IconTasks(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <path d="M3 4.5l1.5 1.5L7 3.5" />
      <path d="M3 11l1.5 1.5L7 10" />
      <path d="M10 5h5M10 11h5" />
    </svg>
  );
}

export function IconRefresh(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <path d="M14.5 5.5a6 6 0 10.9 5" />
      <path d="M14.5 2.5v3h-3" />
    </svg>
  );
}

export function IconTrash(props: IconProps) {
  return (
    <svg {...defaults} {...props}>
      <path d="M3.5 5h11M7 5V3.5h4V5M5 5l.6 9a1 1 0 001 1h4.8a1 1 0 001-1L13 5" />
      <path d="M7.5 7.5v5M10.5 7.5v5" />
    </svg>
  );
}

export function IconChevron(props: IconProps & { direction?: "left" | "right" }) {
  const { direction = "left", ...rest } = props;
  return (
    <svg {...defaults} {...rest}>
      {direction === "left" ? <path d="M11 4L6 9l5 5" /> : <path d="M7 4l5 5-5 5" />}
    </svg>
  );
}
