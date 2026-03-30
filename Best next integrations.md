# Best Next Integrations

Semantic Kernel opportunities that fit the current architecture.

## 1. Semantic Search Plugin

Turn semantic search into a real Semantic Kernel plugin.

- Add plugin functions like SearchPosts(question, topK) and GetPostSnippet(postId)
- Let the model decide when to call tools instead of forcing a fixed flow
- Keep controller and API contracts unchanged while moving tool semantics into plugin methods

## 2. Prompt Function for Grounded Answers

Move grounded answer composition into a kernel prompt function.

- Replace inline prompt strings with a reusable prompt function/template
- Keep strict citation instructions (for example [PostId: N]) in one central place
- Make prompt iteration and A/B testing easier without changing orchestration code

## 3. Function Invocation Filters

Use Semantic Kernel invocation filters for safety and observability.

- Log tool call names, duration, and high-level outcomes
- Enforce guardrails like max topK and argument normalization centrally
- Keep cross-cutting concerns out of controllers and business services

## 4. Indexing Plugin

Expose indexing actions as controlled plugin tools.

- Add functions like IndexPost(postId) and IndexRecentPosts(count)
- Reuse existing indexing and embedding services behind plugin methods
- Support agent-driven ingestion scenarios while preserving service boundaries

## 5. Structured Output Contract

Require a structured grounded-answer payload from the model.

- Define a contract with answer, citations, and grounded fields
- Validate output shape before returning API responses
- Reduce response-format drift and simplify downstream handling
