# Product

## Register

product

## Users

Mixed technical team: platform engineers and data engineers who operate Swarm day to day. They work in long sessions, often with several tools open, checking cluster health, authoring or adjusting pipelines, dispatching tasks, and tracing failures. A marketing site may come later; the primary surface today is the operational dashboard and the embedded observability views inside it.

## Product Purpose

Swarm is a distributed task and pipeline orchestrator. The cluster coordinates nodes, schedules, and workflow runs across RabbitMQ and Postgres. Success means an operator can answer three questions without hunting: what is running, whether the cluster is healthy, and why something failed. Over time the UI must support pipeline authoring (workflow canvas, charts) alongside monitoring, in the spirit of observability tools and orchestration platforms like Airflow, without sacrificing the calm density needed for embedded dashboards.

## Brand Personality

Precise, calm, capable. The interface should feel like a reliable instrument: quiet confidence for long ops sessions, not a product demo. Copy is direct and specific. Status and hierarchy do the emotional work; decoration stays secondary.

## Anti-references

- Generic SaaS dashboards: blue gradient heroes, identical card grids, buzzword copy, hero metrics.
- Consumer-style UI: playful illustrations, oversized rounded chrome, marketing-first layouts on operational screens.
- Legacy enterprise admin: flat gray tables, cramped density, no empty states, alert-driven error handling.
- Observability cosplay without workflow literacy: charts and gauges that do not connect to real pipeline state.
- Airflow UI clutter without information hierarchy: every panel shouting equally.

## Design Principles

1. **Status at a glance** — Node health, active runs, and failures must be readable within seconds of opening any screen.
2. **Calm density** — Show enough operational truth for power users without visual noise; support hours-long sessions.
3. **Workflow literacy** — Pipelines, schedules, and dispatch are first-class peers of monitoring, not bolted-on tabs.
4. **Trace without leaving** — From a failed run to the relevant node logs and execution history in as few clicks as possible.
5. **Instrument, not billboard** — Design serves orchestration work; reserve expressive brand moments for a future marketing surface, not the ops shell.

## Accessibility & Inclusion

WCAG 2.1 AA as the baseline across the dashboard. Pursue AAA where it matters most: log text, status badges, and error states that operators rely on under pressure. Respect `prefers-reduced-motion` (no essential information gated on animation). Keyboard navigation for tabs, tables, dispatch actions, and the future workflow canvas. Color is never the sole carrier of status meaning.
