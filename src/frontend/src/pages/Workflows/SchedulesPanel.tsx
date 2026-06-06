import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { Schedule } from "../../store/store";
import { absoluteTime, relativeTime } from "../../utils/time";

interface SchedulesPanelProps {
  pipelineId: string;
}

// A pragmatic subset; the backend accepts any IANA id. These cover the common ops cases.
const TIMEZONES = ["UTC", "America/New_York", "America/Sao_Paulo", "Europe/London", "Europe/Berlin", "Asia/Tokyo"];

const fieldClass =
  "rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-2 py-1.5 text-sm text-[var(--swarm-ink)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25";

interface DraftState {
  cronExpression: string;
  timeZoneId: string;
  enabled: boolean;
}

const EMPTY_DRAFT: DraftState = { cronExpression: "", timeZoneId: "UTC", enabled: true };

function ScheduleForm({
  initial,
  submitting,
  error,
  onSubmit,
  onCancel,
}: {
  initial: DraftState;
  submitting: boolean;
  error: string | null;
  onSubmit: (d: DraftState) => void;
  onCancel: () => void;
}) {
  const [draft, setDraft] = useState<DraftState>(initial);
  const valid = draft.cronExpression.trim() !== "";

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        if (valid) onSubmit(draft);
      }}
      className="flex flex-wrap items-end gap-3 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-3"
    >
      <label className="flex flex-col gap-1">
        <span className="text-xs font-medium text-[var(--swarm-muted)]">Cron expression</span>
        <input
          value={draft.cronExpression}
          onChange={(e) => setDraft({ ...draft, cronExpression: e.target.value })}
          placeholder="0 2 * * *"
          spellCheck={false}
          className={`${fieldClass} w-40 font-mono`}
        />
      </label>

      <label className="flex flex-col gap-1">
        <span className="text-xs font-medium text-[var(--swarm-muted)]">Timezone</span>
        <select
          value={draft.timeZoneId}
          onChange={(e) => setDraft({ ...draft, timeZoneId: e.target.value })}
          className={fieldClass}
        >
          {TIMEZONES.map((tz) => (
            <option key={tz} value={tz}>
              {tz}
            </option>
          ))}
        </select>
      </label>

      <label className="flex items-center gap-2 pb-1.5 text-sm text-[var(--swarm-ink)]">
        <input
          type="checkbox"
          checked={draft.enabled}
          onChange={(e) => setDraft({ ...draft, enabled: e.target.checked })}
          className="h-4 w-4 accent-[var(--swarm-primary)]"
        />
        Enabled
      </label>

      <div className="flex items-center gap-2">
        <button
          type="submit"
          disabled={!valid || submitting}
          className="rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          {submitting ? "Saving…" : "Save schedule"}
        </button>
        <button
          type="button"
          onClick={onCancel}
          className="rounded-md px-3 py-1.5 text-sm font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Cancel
        </button>
      </div>

      {error && (
        <p role="alert" className="w-full text-sm text-[var(--swarm-danger)]">
          {error}
        </p>
      )}
    </form>
  );
}

function ScheduleRow({
  schedule,
  pipelineId,
  now,
}: {
  schedule: Schedule;
  pipelineId: string;
  now: number;
}) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const key = queryKeys.pipelineSchedules(pipelineId);
  const invalidate = () => queryClient.invalidateQueries({ queryKey: key });

  const toggle = useMutation({
    mutationFn: () => apiClient.updateSchedule(schedule.id, { enabled: !schedule.enabled }),
    onSuccess: invalidate,
  });

  const update = useMutation({
    mutationFn: (d: DraftState) =>
      apiClient.updateSchedule(schedule.id, {
        cronExpression: d.cronExpression.trim(),
        timeZoneId: d.timeZoneId,
        enabled: d.enabled,
      }),
    onSuccess: () => {
      setEditing(false);
      invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: () => apiClient.deleteSchedule(schedule.id),
    onSuccess: invalidate,
  });

  if (editing) {
    return (
      <ScheduleForm
        initial={{
          cronExpression: schedule.cronExpression,
          timeZoneId: schedule.timeZoneId,
          enabled: schedule.enabled,
        }}
        submitting={update.isPending}
        error={update.isError ? "The cluster rejected this cron expression or timezone." : null}
        onSubmit={(d) => update.mutate(d)}
        onCancel={() => setEditing(false)}
      />
    );
  }

  return (
    <li className="flex flex-wrap items-center gap-x-4 gap-y-1 py-2 text-sm">
      <code className="rounded bg-[var(--swarm-surface-raised)] px-1.5 py-0.5 font-mono text-xs text-[var(--swarm-ink)]">
        {schedule.cronExpression}
      </code>
      <span className="text-[var(--swarm-muted)]">{schedule.timeZoneId}</span>
      <button
        type="button"
        onClick={() => toggle.mutate()}
        disabled={toggle.isPending}
        className="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
        style={{
          transitionDuration: "var(--swarm-duration)",
          background: schedule.enabled ? "var(--swarm-success-subtle)" : "var(--swarm-surface-raised)",
          color: schedule.enabled ? "var(--swarm-success)" : "var(--swarm-muted)",
        }}
        aria-label={schedule.enabled ? "Disable schedule" : "Enable schedule"}
      >
        <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden />
        {schedule.enabled ? "Enabled" : "Disabled"}
      </button>
      {schedule.nextFireAt && schedule.enabled && (
        <span className="text-xs text-[var(--swarm-muted)]" title={absoluteTime(schedule.nextFireAt)}>
          next {relativeTime(schedule.nextFireAt, now)}
        </span>
      )}
      <div className="ml-auto flex items-center gap-1">
        <button
          type="button"
          onClick={() => setEditing(true)}
          className="rounded-md px-2 py-1 text-xs font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Edit
        </button>
        <button
          type="button"
          onClick={() => remove.mutate()}
          disabled={remove.isPending}
          className="rounded-md px-2 py-1 text-xs font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-danger-subtle)] hover:text-[var(--swarm-danger)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-50"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Delete
        </button>
      </div>
    </li>
  );
}

export function SchedulesPanel({ pipelineId }: SchedulesPanelProps) {
  const now = Date.now();
  const queryClient = useQueryClient();
  const [creating, setCreating] = useState(false);
  const key = queryKeys.pipelineSchedules(pipelineId);

  const query = useQuery({
    queryKey: key,
    queryFn: () => apiClient.getSchedules(pipelineId),
  });

  const create = useMutation({
    mutationFn: (d: DraftState) =>
      apiClient.createSchedule(pipelineId, {
        cronExpression: d.cronExpression.trim(),
        timeZoneId: d.timeZoneId,
        enabled: d.enabled,
      }),
    onSuccess: () => {
      setCreating(false);
      queryClient.invalidateQueries({ queryKey: key });
    },
  });

  const schedules = query.data ?? [];

  return (
    <div className="space-y-2">
      {query.isLoading ? (
        <div className="h-8 w-full animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none" />
      ) : schedules.length === 0 && !creating ? (
        <p className="text-sm text-[var(--swarm-muted)]">
          No schedules. This pipeline runs only when triggered manually.
        </p>
      ) : (
        <ul className="divide-y divide-[var(--swarm-border)]">
          {schedules.map((s) => (
            <ScheduleRow key={s.id} schedule={s} pipelineId={pipelineId} now={now} />
          ))}
        </ul>
      )}

      {creating ? (
        <ScheduleForm
          initial={EMPTY_DRAFT}
          submitting={create.isPending}
          error={create.isError ? "The cluster rejected this cron expression or timezone." : null}
          onSubmit={(d) => create.mutate(d)}
          onCancel={() => setCreating(false)}
        />
      ) : (
        <button
          type="button"
          onClick={() => setCreating(true)}
          className="rounded-md px-2 py-1 text-xs font-medium text-[var(--swarm-primary)] transition-colors hover:bg-[var(--swarm-primary-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          + Add schedule
        </button>
      )}
    </div>
  );
}
