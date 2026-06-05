# Swarm

A standalone task-orchestration platform. Run a **Cluster** (control plane) and a
fleet of **Nodes** (workers); Nodes reach external systems — databases, APIs,
internal services — over the network. Tasks are defined, scheduled, configured,
and routed centrally; work executes on the Nodes. Think Airflow, but you author
handlers against the `Swarm.Node.Sdk` and deploy them into Node containers.

- **Base Node container** — ships with built-in `http@1`, `sql@1`, `webhook@1`
  handlers; configure entirely through task definitions and environment variables.
- **Custom Node image** — reference `Swarm.Node.Sdk`, write your own
  `ITaskHandler` implementations, build `FROM` the base image.

## Quick start

```bash
docker compose -f scripts/docker-compose.yml up --build
```

See **[docs/getting-started.md](docs/getting-started.md)** for configuration, the
full environment-variable reference, and running from source.

## Security

No enforced authentication yet (decision D7). Single-operator / trusted-network
deployments only until the P4 security phase lands. See
[docs/trust-model.md](docs/trust-model.md) and [SECURITY.md](SECURITY.md).

## Status

Phases 1–3 (foundation, correctness, orchestration) complete; Phase 4 (production
quality) in progress. See `.claude/ROADMAP.md` for the engineering backlog.
