Review the current change or the file(s) I've indicated against the architecture and testing conventions for this repo. Flag any violations and suggest fixes.

## Architecture Rules
- Controllers must be thin: routing, request validation, status codes, response shaping only. Orchestration belongs in services.
- RagAgent.Core must stay provider-agnostic. No Redis, S3, Qdrant, or Bedrock references in Core.
- Provider-specific implementation details belong in RagAgent.Redis or RagAgent.Agents.
- Do not leak provider-specific types into Core or controllers.

## Placement Guide
| Layer | Responsibility |
|---|---|
| Controller | Route handling, request parsing, status codes, response DTOs |
| Service | Use-case orchestration, sequencing calls to abstractions |
| Core | Interfaces, shared models, provider-agnostic logic |
| Provider project | Redis, S3, Qdrant, Bedrock, external system details |

## Testing Rules
- Target ~70% unit tests, ~30% integration tests.
- Unit tests: business logic, orchestration, mapping, validation, provider-agnostic behaviour.
- Integration tests: key 200 OK happy paths + a small set of critical failure paths (400, 404). Not a substitute for unit coverage.

## Change Checklist
1. Can any controller code be moved into a service?
2. Does any shared logic belong in RagAgent.Core instead of a provider project?
3. Are unit tests added or updated for the changed logic?
4. Are integration tests scoped to end-to-end API behaviour only?
5. Does the change couple controllers or Core to a specific provider?

For each checklist item, report: ✓ pass, ✗ violation (with file and line), or — not applicable.
