# Swarm

Distributed task and pipeline orchestrator (.NET cluster + React dashboard).

## Design Context

Strategic design direction lives in [PRODUCT.md](./PRODUCT.md) at the project root. Read it before any UI work.

- **Register:** product (ops dashboard primary; marketing surface may come later)
- **Personality:** precise, calm, capable
- **References:** observability density (Grafana-like embedded views) + workflow orchestration (Airflow-like pipelines, charts, canvas)
- **Anti-references:** generic SaaS blue dashboards, consumer chrome, legacy gray admin

Visual tokens and component specs belong in `DESIGN.md` when present. The current frontend is a placeholder; new UI should be designed from scratch against PRODUCT.md, not extended from the existing layout.
