# Security Policy

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, please email security@swarm with:

- A description of the vulnerability
- Steps to reproduce (if applicable)
- Affected versions
- Potential impact
- Any known mitigations or workarounds

Please include as much detail as possible to help us understand and reproduce
the issue. You should receive a response within 48 hours.

## Disclosure Policy

1. **Initial Response**: We will acknowledge receipt of your report within 48 hours
2. **Investigation**: We will investigate the vulnerability and determine scope
3. **Fix Development**: We will work to develop and test a fix
4. **Coordinated Release**: We will coordinate a release timeline with you
5. **Public Disclosure**: After a fix is released, we will disclose the vulnerability
   in security advisories

## Supported Versions

Currently supported versions for security updates:

| Version | Status  | End of Support |
| ------- | ------- | -------------- |
| 1.0.x   | Current | TBD            |
| 0.x.x   | Beta    | Unsupported    |

## Security Considerations for Swarm

### Multi-Tenant Deployments

If deploying Swarm in a shared/multi-tenant environment, be aware of the following:

- **Authentication (P4-1)**: Auth is deferred per architectural decision D7. Single-operator
  deployments only are supported until P4-1 is complete.
- **Node Isolation**: Nodes can be tagged and isolated by environment/region via `SWARM_TAG_*`
  and overlay tags (P2-5).
- **Task Config Security**: Task-level secrets are stored in Node-local encrypted stores
  (Tier 2) with AES-256-GCM and never sent to Cluster (P1-5a).
- **Trust Model**: Currently no mTLS or Node-to-Cluster auth. The full threat model
  — what is trusted implicitly, the concrete threats, and the mitigation options
  deferred to P4-1 — is documented in [docs/trust-model.md](docs/trust-model.md).

### Known Limitations (Pre-P4)

Until P4 (Security) phase completes:

1. No API authentication is enforced
2. No role-based access control
3. All task results and logs are viewable by any client with network access
4. Secrets in task config are visible to any Cluster operator
5. No audit logging of administrative actions

For production use cases, network isolation (VPC, internal networks only) is
strongly recommended.

### Docker Image Naming

Docker images follow the pattern `swarm/<component>:latest`:

```dockerfile
FROM swarm/node:latest
```

### Cryptography

- **Value Resolution**: AES-256-GCM for Node-local encrypted stores (P1-5a)
- **HMAC**: SHA-256 for Webhook handler request signing (P1-5)

### Dependencies

Security updates for all NuGet and npm dependencies are prioritized. Check the
lock files (`packages.lock.json`, `package-lock.json`) for current versions.

To report vulnerabilities in dependencies:

1. Check for available updates: `dotnet list package --vulnerable`
2. Report to the dependency maintainers directly
3. If there's a workaround needed in Swarm, report to security@swarmhq.com

## Security Roadmap

Phase 4 (Security) includes:

- **P4-1**: Operator-facing auth model with RBAC
- **P4-2**: Secrets management (remove hardcoded config)
- **P4-2a**: Serilog secret redaction in logs
- **P4-3**: Formal Node-to-Cluster trust model documentation

See ROADMAP.md for detailed specifications and timeline.

## Contact

- Security vulnerabilities: security@swarm
- General inquiries: hello@swarm

Thank you for helping keep Swarm secure!
