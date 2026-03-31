# VectorSearch.Api — Design Rules

## Controllers
- Handle routing, request validation, status codes, and response shaping only.
- Delegate all orchestration and business logic to services — never put flow decisions in a controller.
- Use request DTOs for input; map to Core models via the static mapper classes before passing to services.

## Services
- Orchestrate calls to Core abstractions (`IVectorService`, `IEmbeddingService`, `IAgentAnswerService`, etc.).
- Services must not depend on provider-specific types — only on Core interfaces.
- `InMemoryConversationStore` is the in-process conversation store. It exposes `Subscribe()` returning `IAsyncEnumerable<ConversationEvent>` — use this to observe conversation lifecycle events without coupling to the store internals.

## Dependency Injection
- Register services in `Program.cs`. Keep registrations grouped and commented by concern.
- `InMemoryConversationStore` is a singleton (conversation state is shared across requests).

## Testing
- Unit tests live in `VectorSearch.UnitTests` and reference this project.
- Integration tests use `VectorSearchWebApplicationFactory` with Testcontainers (Redis, Qdrant).
- Prefer unit tests for orchestration logic; use integration tests only for end-to-end API wiring.
