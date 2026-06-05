import { useQuery } from "@tanstack/react-query";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import { absoluteTime } from "../../utils/time";

interface ScheduleChipProps {
  pipelineId: string;
}

/** Shows the first enabled schedule's cron expression as a chip, with the next
 *  fire time in the tooltip. Renders nothing if the pipeline runs manually. */
export function ScheduleChip({ pipelineId }: ScheduleChipProps) {
  const { data } = useQuery({
    queryKey: queryKeys.pipelineSchedules(pipelineId),
    queryFn: () => apiClient.getSchedules(pipelineId),
  });

  const active = data?.find((s) => s.enabled);
  if (!active) return null;

  const next = active.nextFireAt
    ? `Next run ${absoluteTime(active.nextFireAt)} (${active.timeZoneId})`
    : `Timezone ${active.timeZoneId}`;

  return (
    <span
      className="rounded bg-[var(--swarm-primary-subtle)] px-1.5 py-0.5 font-mono text-xs text-[var(--swarm-ink)]"
      title={next}
    >
      {active.cronExpression}
    </span>
  );
}
