import { useOutletContext } from "react-router-dom";
import type { ClusterPulse } from "../../hooks/useClusterPulse";

export interface ShellContext {
  pulse: ClusterPulse;
}

export function useShellContext(): ShellContext {
  return useOutletContext<ShellContext>();
}

interface RouteViewProps {
  Page: (props: { pulse: ClusterPulse }) => JSX.Element;
}

export function RouteView({ Page }: RouteViewProps) {
  const { pulse } = useShellContext();
  return <Page pulse={pulse} />;
}
