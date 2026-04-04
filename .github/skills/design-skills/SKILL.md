---
name: design-skills
description: 'Use for ASP.NET Core API architecture and testing conventions in this repo: keep controllers thin, preserve provider-agnostic logic in RagAgent.Core, prefer unit tests for business logic, and use integration tests to verify minimum end-to-end API behavior like 200 OK happy paths.'
---

# Design Skills

## When to Use
- Adding or refactoring controllers, services, or request flows
- Deciding whether logic belongs in the API layer, Core, or provider-specific projects
- Adding test coverage for endpoints, orchestration logic, or provider adapters
- Reviewing changes for architecture drift

## Architecture Rules
- Keep controllers thin. Controllers should handle routing, request validation, status codes, and response shaping.
- Move orchestration and business logic into services.
- Keep RagAgent.Core provider-agnostic. Core should contain shared contracts, models, and logic that work across Redis, S3 Vectors, and Qdrant.
- Put provider-specific implementation details in provider projects such as RagAgent.Redis and RagAgent.Agents.
- Avoid leaking provider-specific types or assumptions into Core or controllers.

## Testing Rules
- Aim for roughly 70% unit tests and 30% integration tests.
- Use unit tests for business logic, orchestration, mapping, validation, and provider-agnostic behavior.
- Use integration tests to verify minimum end-to-end functionality.
- Integration tests should cover key API happy paths with 200 OK responses and a small number of critical failure paths like 400 Bad Request or 404 Not Found where behavior is important.
- Do not use integration tests as a substitute for focused unit coverage.

## Placement Guide
- Controller concern: route handling, request parsing, status code decisions, response DTOs.
- Service concern: use-case orchestration, sequencing calls to abstractions, cross-cutting flow decisions.
- Core concern: interfaces, shared models, provider-agnostic logic.
- Provider concern: Redis, S3 Vectors, Qdrant, Bedrock, and external system integration details.

## Change Checklist
1. Check whether controller code can be moved into a service.
2. Check whether shared logic belongs in RagAgent.Core instead of a provider project.
3. Add or update unit tests for the changed logic.
4. Add or update integration tests only for key end-to-end API behavior.
5. Verify the change does not couple controllers or Core to a specific provider.