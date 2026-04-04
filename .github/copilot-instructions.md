- For commit messages, use Use chore:, feat:, fix:, docs commit prefixes

- chore: for changes that don't modify src or test files, such as updating build scripts, package.json, etc.

- feat: for new features or enhancements to existing features.

- fix: for bug fixes.

- docs: for documentation changes, such as updating README.md or adding comments to code.

- Keep controllers thin: HTTP concerns stay in controllers; orchestration and business logic belong in services.

- Favor provider-agnostic design in RagAgent.Core. Shared contracts and logic in Core should not depend on Redis, S3 Vectors, or Qdrant specifics.

- Test strategy: aim for roughly 70% unit tests and 30% integration tests.

- Integration tests should verify core minimum functionality and API wiring, especially successful 200 OK flows for key endpoints and a small set of critical unhappy paths.

- For Terraform changes, always run `terraform fmt -recursive` in `infra/` before finishing.

- Validate Terraform via Docker because local Terraform validation may not work in this repo environment: `docker run --rm -v "<repo>\\infra:/workspace" -w /workspace hashicorp/terraform:1.5.0 validate`.

- Next integrations roadmap:

- Add a single-tool-calling agent first: user question → agent decides to run semantic search → returns grounded answer with sources.

- Wire Bedrock text generation into the same endpoint so the final answer is LLM-generated while still grounded on retrieved sources.

- Add a second ingestion agent: watches new docs, chunks them, embeds them, and updates the vector store automatically.

- Add an evaluation agent: runs a test question set and scores retrieval + answer quality (hit@k, groundedness, hallucination rate).