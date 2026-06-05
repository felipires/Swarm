# Node ↔ Cluster trust model

> **Status:** descriptive (roadmap P4-3). This document records *what Swarm
> currently trusts* and the threats that follow, so operators understand what
> they are running. It does **not** pick an authentication mechanism — that
> choice belongs to P4-1, which is deferred per architectural decision **D7**.
> Until then: **single-operator / trusted-network deployments only.**

## 1. Actors and channels

| Actor | Talks to | Channel | Transport (dev) |
|-------|----------|---------|-----------------|
| **Operator / REST client** | Cluster | REST API (controllers) | HTTP :5001, HTTPS :5002 |
| **Node** (worker) | Cluster | gRPC — `RegisterNode`, `RecordHeartbeat` | HTTP/2 cleartext (h2c) :5000 |
| **Node** ↔ **Cluster** | RabbitMQ | task / result / claim / log queues | AMQP :5672 |

Kestrel binds these ports to `localhost` in development
([Program.cs](../src/Swarm.Cluster/Program.cs)); `scripts/docker-compose.yml`
exposes :5000 (gRPC) on the host. There is **no TLS** on the gRPC (:5000) or HTTP
REST (:5001) listeners; :5002 offers HTTPS but enforces no authentication on top.

## 2. What is trusted implicitly today

This is the current reality, by design (D7) — not a list of accidental bugs.

1. **The gRPC `api_key` field is accepted but never validated.** Both
   `RegisterNodeRequest` and `RecordHeartbeatRequest` carry an `api_key`
   ([nodes.proto](../src/Swarm.Cluster/Protos/nodes.proto)), and the Node sends
   the value from its `ApiKey` config. The Cluster's
   `NodeService.RegisterNodeAsync` takes the parameter but does not check, store,
   or log it; `RecordHeartbeat` never reads it at all. Any value (including blank)
   is accepted.
2. **Node identity is a bare GUID with no proof of possession.** A Node is
   identified solely by the `node_id` it presents. The Cluster verifies the GUID
   parses and that a row exists — nothing ties the caller to the Node that
   originally registered that ID. Anyone who learns a valid `node_id` can:
   - send heartbeats as that Node (keep it "Online" or force it "Offline");
   - **drain that Node's pending env-secret operations** from the heartbeat
     response, before the real Node acks them;
   - receive that Node's overlay tags and tagged-queue subscriptions.
3. **Registration hands out cluster-wide RabbitMQ credentials.** The
   `RegisterNode` response returns `queue_host/port/user/password`
   ([NodeService](../src/Swarm.Cluster/Services/NodeService.cs)) — the shared
   broker account from Cluster config. Any caller that can reach :5000 and call
   `RegisterNode` obtains broker creds good for every queue.
4. **The REST API enforces no authentication.** `UseAuthentication()` /
   `UseAuthorization()` are in the pipeline but no authentication handler is
   registered and no controller carries `[Authorize]`. Every endpoint — list/
   create/dispatch tasks, enumerate nodes, PATCH tags, set Node env secrets, read
   logs, trigger pipelines — is reachable unauthenticated. (Only `/health` is
   explicitly anonymous; everything else is implicitly so.)
5. **No transport encryption on the worker/operator dev ports** (:5000 h2c,
   :5001 HTTP). Credentials and task payloads cross these in cleartext.

## 3. Concrete threats

Given §2, an attacker with network reach to the Cluster ports or the broker can:

| # | Threat | Vector |
|---|--------|--------|
| T1 | **Node impersonation** | Heartbeat with a known `node_id` → control its online status, drain its env-secret ops, harvest its tags/subscriptions |
| T2 | **Capability inflation** | `RegisterNode` advertising fabricated `handlers` → become an eligible target for task types it can't actually run |
| T3 | **Task-result poisoning** | With broker creds, publish to `task-results` → mark instances Completed/Failed with attacker-chosen output |
| T4 | **False claims** | Publish to `task-claims` → bind shared-queue instances to an attacker Node |
| T5 | **Env-secret exfiltration** | Impersonate a Node's heartbeat to receive `pending_env_ops` (plaintext until acked) destined for that Node |
| T6 | **Cross-Node queue reads** | Subscribe to any `tasks.*` queue with the shared creds → read task payloads intended for other Nodes |
| T7 | **Broker blast radius** | The single shared RabbitMQ account means one leaked registration response compromises all queues |
| T8 | **Unauthorized control-plane actions** | Unauthenticated REST → dispatch arbitrary tasks, set Node env secrets, trigger pipelines, read all logs |

Task-plane secrets are partially insulated: Tier-2 Node env values are AES-256-GCM
encrypted at rest and never leave the Node (P1-5a), and `:secret` values are
redacted in logs (P4-2a). The exposure in T5 is the **delivery window** — the
Cluster holds an env op in plaintext until the Node acks it.

## 4. Mitigation options (for P4-1 to choose among)

Not prescriptive — recorded so the eventual decision has a starting point. These
are complementary, not mutually exclusive.

| Option | Addresses | Trade-offs |
|--------|-----------|-----------|
| **mTLS between Node and Cluster** | T1, T2, T5, partial T8 | Strong identity + transport encryption; needs cert issuance/rotation (a small PKI or SPIFFE-style issuer) |
| **Short-lived JWT issued at registration** | T1, T5 | Node proves identity per heartbeat; lighter than mTLS but needs a signing key + token refresh; doesn't encrypt transport on its own |
| **Node allowlist + validated `api_key`** | T1, T2 (entry control) | Cheapest first step — actually validate the `api_key` already on the wire and gate registration on an allowlist; coarse, no per-message identity |
| **Per-Node RabbitMQ credentials / vhosts** | T3, T4, T6, T7 | Scopes a Node to only its own queues; needs broker user provisioning at registration |
| **REST authentication (API keys / OIDC / RBAC)** | T8 | The operator-facing half; this is the core of P4-1 |
| **TLS on :5000 / :5001** | T5, eavesdropping | Straightforward; pairs with any of the above |

## 5. Safe-deployment guidance (until P4-1)

- Run Cluster and Nodes on a **private network**; do not expose :5000/:5001/:5002
  or :5672 to untrusted clients.
- Treat anyone with network access to the Cluster as a full operator.
- Override all system-plane creds via environment variables (see
  [getting-started.md](getting-started.md)); rotate the shared RabbitMQ account.
- Set `SWARM_NODE_MACHINE_KEY` per Node and back it up so encrypted task secrets
  survive rebuilds.

This mirrors the "Known Limitations (Pre-P4)" in [SECURITY.md](../SECURITY.md).
