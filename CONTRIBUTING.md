# Contributing to Swarm

Thank you for your interest in contributing to Swarm! This document provides
guidelines and instructions for contributing to the project.

## Code of Conduct

This project adheres to a Code of Conduct. By participating, you are expected to
uphold this code. Please report unacceptable behavior to legal@swarm.

## How to Contribute

### Reporting Bugs

Before submitting a bug report, please check the [issue tracker](../../issues) to
avoid duplicates.

**When filing a bug report, include:**
- A clear, descriptive title
- Description of the exact steps to reproduce the problem
- Expected vs. actual behavior
- Environment details (OS, .NET version, database, etc.)
- Relevant logs or error messages
- Screenshots or diagrams if applicable

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When suggesting an enhancement:
- Use a clear, descriptive title
- Provide a step-by-step description of the enhancement
- Explain why this enhancement would be useful to Swarm users
- List any similar features in other tools

### Pull Requests

1. **Create a fork** and work in a feature branch (`feature/description` or `fix/description`)
2. **Follow coding standards** (see Engineering Standards in ROADMAP.md)
3. **Write tests** for your changes using xUnit
4. **Update documentation** if your change affects user-facing behavior
5. **Keep commits focused** — one logical change per commit with clear messages
6. **Link to issues** — reference related issues in your PR description

### Development Setup

1. Clone the repository
2. Review the README.md for project structure
3. Consult ROADMAP.md for architecture and engineering standards
4. Build: `dotnet build`
5. Test: `dotnet test`
6. Run locally: `docker-compose up` (see README.md)

### Code Standards

The Swarm project follows principles outlined in the ROADMAP.md file:

- **SOLID Principles** — applied pragmatically, not dogmatically
- **KISS** — simplest design that satisfies requirements wins
- **YAGNI** — build for requirements in hand, not imagined futures
- **Interface Guidelines** — interfaces exist for boundaries and plugin points only
- **Naming** — clear, specific names; async methods end in `Async`
- **Comments** — explain *why*, not *what*; rename identifiers instead

### Testing Requirements

- **Unit tests** — pure logic with no external dependencies (xUnit, `Swarm.*.Tests`)
- **Integration tests** — code touching real Postgres/RabbitMQ (Testcontainers)
- **Coverage targets** — >85% on pure logic, happy + failure paths for boundaries

Run tests:
```bash
dotnet test
```

### Commit Message Guidelines

Use clear, descriptive commit messages:

```
[AREA] Concise summary (50 chars or less)

Longer explanation if needed, wrapped at 72 characters.
Reference issue #123 if applicable.

- Bullet point for individual changes
- Another bullet if needed
```

Examples:
- `[Core] Add value resolution system for task config`
- `[Fix] Correct TaskInstance FSM transition validation`
- `[Tests] Add integration tests for OutboxPublisher`

### Documentation

- Update README.md if adding user-facing features
- Update ROADMAP.md architectural decisions and phasing if proposing architectural changes
- Add XML docs to public APIs (`ITaskHandler`, `HandlerSchema`, etc.)
- Document any new configuration options in appsettings.json

### License

By contributing, you agree that your contributions will be licensed under the
Functional Software License (FSL). After the FSL expires, contributions will be
available under Apache License 2.0.

## Questions?

- Check README.md for project overview
- Review ROADMAP.md for architectural context
- Open a discussion issue for design questions
- Contact: hello@swarm

Thank you for making Swarm better!
