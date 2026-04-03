# Claude Code Instructions

## Language
- Use **British English** in all code, comments, log messages, and documentation. e.g. `summarise` not `summarize`, `normalise` not `normalize`, `initialisation` not `initialization`, `colour` not `color`. Exception: .NET framework method names (e.g. `JsonSerializer`, `InitializeAsync`) must keep their original spelling.

## Git Workflow
- **Never push unless explicitly asked.** Commit locally, then wait for the user to say "push".
- **For .cs changes:** always run `dotnet test VectorSearch.IntegrationTests` before committing. All tests must pass. Do not commit if any test is failing.
- **For .cs changes:** always run `dotnet format VectorSearch.Api.sln --severity warn` before committing. The pre-push hook enforces this, but fixing it before the commit avoids a blocked push.
- **For Terraform-only changes:** do not run dotnet tests. Instead run `terraform fmt -recursive` and `terraform validate` via Docker (see Terraform section below).

## Commit Messages
Use conventional commit prefixes:
- `feat:` — new features or enhancements
- `fix:` — bug fixes
- `chore:` — changes that don't modify src or test files (build scripts, packages, etc.)
- `docs:` — documentation changes

## Architecture
- **Keep controllers thin.** HTTP concerns (routing, validation, status codes, response shaping) stay in controllers. Orchestration and business logic belong in services.
- **VectorSearch.Core must stay provider-agnostic.** Shared contracts, interfaces, and logic in Core must not depend on Redis, S3 Vectors, or Qdrant specifics. Provider-specific details go in VectorSearch.Redis and VectorSearch.S3.
- **Placement guide:**
  - Controller → route handling, request parsing, status codes, response DTOs
  - Service → use-case orchestration, sequencing calls to abstractions
  - Core → interfaces, shared models, provider-agnostic logic
  - Provider project → Redis, S3, Qdrant, Bedrock integration details

## Testing
- Aim for roughly **70% unit tests, 30% integration tests**.
- Use unit tests for business logic, orchestration, mapping, validation, and provider-agnostic behavior.
- Use integration tests to verify minimum end-to-end functionality — key 200 OK happy paths and a small set of critical failure paths (400, 404) where behavior matters.
- Do not use integration tests as a substitute for focused unit coverage.

## Terraform
- Always run `terraform fmt -recursive` and `terraform validate` before committing any Terraform change.
- Run both via Docker (local Terraform may not work). Use `MSYS_NO_PATHCONV=1` to prevent Git Bash from mangling paths:
  ```
  MSYS_NO_PATHCONV=1 docker run --rm -v "c:/src/dotnet-rag-agent/infra:/workspace" -w /workspace hashicorp/terraform:1.5.0 fmt -recursive
  MSYS_NO_PATHCONV=1 docker run --rm -v "c:/src/dotnet-rag-agent/infra:/workspace" -w /workspace hashicorp/terraform:1.5.0 validate
  ```

## Roadmap (Next Integrations)
- Single-tool-calling agent: user question → agent decides to run semantic search → grounded answer with sources
- Wire Bedrock text generation into the same endpoint (LLM-generated answer, grounded on retrieved sources)
- Ingestion agent: watches new docs, chunks, embeds, and updates the vector store automatically
- Evaluation agent: runs a test question set and scores retrieval + answer quality (hit@k, groundedness, hallucination rate)
