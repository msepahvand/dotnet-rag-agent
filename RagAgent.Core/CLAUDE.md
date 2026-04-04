# RagAgent.Core — Design Rules

This project is the provider-agnostic heart of the solution. Everything here must work regardless of whether the vector store is Redis, S3 Vectors, or Qdrant.

## Rules
- **No provider-specific types or packages.** Do not reference Redis, S3, Qdrant, or Bedrock here.
- **Interfaces only for external dependencies.** Define `IEmbeddingService`, `IVectorService`, `IConversationStore`, etc. here. Implementations live in provider projects.
- **Models are shared contracts.** Types in `Models/` are used across all layers — keep them serialization-friendly and free of infrastructure concerns.
- **Unit is defined here.** `Unit` is a zero-dependency placeholder struct; prefer it over `byte` or `bool` in concurrent dictionaries used as sets.

## What belongs here
- Interfaces (`IEmbeddingService`, `IVectorService`, `IConversationStore`, `IPostService`, `IAgentAnswerService`)
- Shared models (`Post`, `ChatMessage`, `AgentAnswerResult`, `ConversationEvent`, etc.)
- Provider-agnostic utilities

## What does NOT belong here
- Any `using` for AWS SDK, Redis, Qdrant, or Semantic Kernel infrastructure
- Concrete service implementations
- HTTP or ASP.NET types
