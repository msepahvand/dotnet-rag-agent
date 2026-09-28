# Agentic AI in .NET — Learning Roadmap

A progressive roadmap for building agentic AI skills using this repository as a working lab. The current application architecture uses Microsoft.Extensions.AI and Microsoft Agent Framework; historical Semantic Kernel exercises below describe earlier implementation steps, not current runtime dependencies.

> **Current-code note:** completed entries preserve the learning history, not a guarantee that the named classes, APIs, or integrations still exist. Some early Semantic Kernel implementations were intentionally replaced during modernisation. For the maintained architecture and live API/deployment guidance, use the [README](README.md) and [documentation index](docs/README.md).

## What you've already built

- Single-tool-calling researcher (question → semantic search → grounded answer)
- `search_posts` exposed as an MEAI `AIFunction` to a MAF `ChatClientAgent`
- Function invocation through Microsoft.Extensions.AI; user `topK` is clamped and multiple search results are merged
- Bedrock-backed embeddings and text generation
- Provider-agnostic core with S3 Vectors / Qdrant implementations

This is a strong foundation. Everything below builds directly on it.

---

## Phase 1 — Strengthen the Single-Agent Loop

### ~~1.1 Structured Output Contract~~ ✅

~~Force the LLM to return a validated JSON shape instead of free-form text.~~

- ~~Define a response contract: `{ answer, citations: [{ postId, quote }], grounded: bool }`~~
- ~~Use SK's `JsonSchemaResponseFormat` or Bedrock's `response_format` to constrain the model~~
- ~~Validate before returning from the API; fall back to the deterministic answer on parse failure~~
- **Done**: `StructuredLlmAnswer` in `GroundedAgentAnswerService` parses `answer`/`citations`/`grounded` with fallback to deterministic answer.

### ~~1.2 Prompt Templates as Prompt Functions~~ ✅

~~Replace the inline prompt string in `GroundedAgentAnswerService` with a reusable YAML/Handlebars prompt function.~~

- ~~Create a `/Prompts` folder with `.yaml` prompt config + `.txt` Handlebars template~~
- ~~Load via `kernel.CreateFunctionFromPromptYaml()`~~
- ~~Keep citation instructions, persona, and grounding rules in one versioned place~~
- **Done**: `RagAgent.Agents/Prompts/GroundedAnswer.yaml` loaded via `KernelFunctionYaml.FromPromptYaml()`.

### ~~1.3 Chat History and Multi-Turn Conversations~~ ✅

~~Add stateful conversation support to the `/api/agent/ask` endpoint.~~

- ~~Maintain a `ChatHistory` per session (in-memory or Redis-backed)~~
- ~~Pass history into the kernel invocation so follow-up questions have context~~
- ~~Add a `/api/agent/conversations` endpoint to manage sessions~~
- **Done**: `InMemoryConversationStore`, `ConversationsController` (list/get/delete), history passed into `GroundedAgentAnswerService.AnswerAsync`.

---

## Phase 2 — Multi-Tool Agent

### ~~2.1 Give the Agent More Tools~~ ✅

~~Register multiple plugins and let the model pick which to call.~~

- ~~Add a `SummarisePlugin` — takes a post ID and returns a condensed summary~~
- ~~Add a `ComparePostsPlugin` — takes two post IDs and returns a comparison~~
- ~~Register all plugins in the kernel; the agent decides the tool chain per question~~
- **Done**: `SummarisePlugin` (`summarise_post`) and `ComparePostsPlugin` (`compare_posts`) added alongside `SemanticSearchPlugin`. All three registered on the kernel in `GroundedAgentAnswerService`.

### ~~2.2 Required vs. Auto vs. None — Function Choice Strategies~~ ✅

~~Experiment with all three `FunctionChoiceBehavior` modes.~~

- ~~`Auto()` — model decides (current behavior)~~
- ~~`Required()` — model must call at least one function (useful for retrieval-first flows)~~
- ~~`None()` — pure chat, no tools (useful for final-answer generation after retrieval)~~
- ~~Chain them: first invocation with `Required` for retrieval, second with `None` for synthesis~~
- **Done**: `GroundedAgentAnswerService` uses a two-pass approach — Pass 1 with `Required(autoInvoke: true)` forces tool use; Pass 2 with `None()` synthesises the structured JSON answer.

---

## Phase 3 — Multi-Agent Orchestration

### ~~3.1 Researcher + Writer Pattern~~ ✅

~~Split the current monolithic agent into two collaborating agents.~~

- ~~**Researcher agent**: has access to `SemanticSearchPlugin`. Retrieves and ranks sources.~~
- ~~**Writer agent**: receives sources from researcher. Produces the final grounded answer.~~
- ~~Orchestrate with SK's `AgentGroupChat` or a simple sequential handoff in code~~
- **Current implementation**: `ResearcherAgent` retrieves through the `search_posts` tool and hands merged sources to `WriterAgent`; the batch flow is orchestrated by `AgentAnswerWorkflowService`.

> **Historical Bedrock caveat — resolved by the migration:** the old SK Bedrock connector did not support `FunctionChoiceBehavior` for Claude models (microsoft/semantic-kernel#9750). The current researcher uses a MAF `ChatClientAgent` with MEAI `AIFunction` function invocation and a direct-search fallback; the old SK advice to call plugins directly is not the current implementation.

### ~~3.2 Agent with a Critic / Self-Reflection~~ ✅

~~Add a review loop where a second agent scores the first agent's output.~~

- ~~Critic agent checks: Are citations real? Is the answer grounded? Is it relevant?~~
- ~~If the critic rejects, pass its feedback to the writer for a revision~~
- ~~Cap at 2-3 iterations to avoid runaway loops~~
- **Current implementation**: `CriticAgent` checks citation validity deterministically, then uses the LLM for relevance and groundedness. The MAF workflow runs up to three writer passes and passes critic feedback into revisions; `AgentAnswerResult.Iterations` exposes the pass count.

### ~~3.3 Microsoft Agent Framework Workflows~~ ✅

~~Replace the Semantic Kernel Process orchestration with a typed Microsoft Agent Framework Workflow.~~

- ~~Research → Write → Critic → Output, with the third draft routed directly to Output~~
- ~~Keep iteration state in workflow messages so each request has isolated state~~
- ~~Use MAF `ChatClientAgent`s for research, writing and critique; preserve the Core interfaces and API contracts~~
- **Done**: `AgentAnswerWorkflowService` executes a Microsoft Agent Framework Workflow with typed executors and a three-draft cap. Critic approval routes to output; rejected drafts return to the writer; the third draft bypasses the critic. Research uses a bounded `search_posts` tool and falls back to the original question if the model does not call it.

---

## Phase 4 — Autonomous Agents

### ~~4.1 Ingestion Agent~~ ✅

~~Build an agent that watches for new content and autonomously indexes it.~~

- ~~Background service that polls for new posts on a timer~~
- ~~Chunks content, generates embeddings, upserts into the vector store~~
- ~~Uses the existing `PostIndexingService` but runs autonomously, not on user request~~
- ~~**Why**: Not all agents are user-facing. Background autonomous agents are a huge enterprise use case (data pipelines, monitoring, ETL).~~
- **Done**: `IngestionBackgroundService` polls HackerNews on a configurable timer, embeddings via `PostIndexingService` (Channel-based streaming with backpressure, max 3 concurrent), tracks indexed IDs in `IngestionTracker` to avoid re-indexing. `IndexingStartupService` seeds the tracker on startup.

### ~~4.2 Evaluation Agent~~ ✅

~~Build an agent that scores retrieval and answer quality.~~

- ~~Define a test question set with expected answers / source IDs~~
- ~~Agent runs each question, compares results, and computes metrics:~~
  - ~~**Hit@k**: Did the correct source appear in top-k results?~~
  - ~~**Groundedness**: Is every claim in the answer backed by a retrieved source?~~
  - ~~**Hallucination rate**: Does the answer contain claims not in any source?~~
- ~~Output a score report as structured JSON~~
- ~~**Why**: You can't improve what you can't measure. Evaluation is the most underrated agentic skill.~~
- **Done**: `EvaluationAgent` runs a built-in or caller-supplied question set through the full pipeline and returns hit@k, groundedness, citation validity, and latency metrics. Exposed via `POST /api/agent/evaluate`.

---

## Phase 5 — Production Patterns

### ~~5.1 Guardrails and Safety~~ ✅

~~Add defense-in-depth to the agent pipeline.~~

- ~~Input guardrails: prompt injection detection, PII filtering, topic scoping~~
- ~~Output guardrails: content filtering, citation verification, response length limits~~
- ~~Implement as SK `IPromptRenderFilter` (input) and `IFunctionInvocationFilter` (output)~~
- ~~**Why**: Enterprise AI requires safety layers. Building them with SK's filter pipeline is the idiomatic .NET approach.~~
- **Done**: `InputGuardrailFilter` (`IPromptRenderFilter`) checks injection phrases, PII (email, credit card, phone), and topic scope. `OutputGuardrailFilter` (`IFunctionInvocationFilter`) logs oversized tool results and harmful terms. `AgentOrchestrationService` and `AgentStreamingService` validate input via shared `AgentPipelineGuardrails` and truncate output at 3,000 chars.

### ~~5.2 Observability and Tracing~~ ✅

~~Add end-to-end traces to the agent execution.~~

- ~~Integrate OpenTelemetry with SK's built-in instrumentation~~
- ~~Trace: user question → tool calls → LLM invocations → final response~~
- ~~Export to a local Aspire dashboard or Jaeger for visualization~~
- ~~**Why**: When agents misbehave in production, traces are how you debug them. This is essential operational skill.~~
- **Done**: `AgentActivitySource` emits `agent.ask` and `agent.stream` spans with tags (`conversation.id`, `rag.top_k`, `rag.grounded`, etc.). OTLP exporter configured via `OpenTelemetry__OtlpEndpoint`. Local: Jaeger at http://localhost:16686 via `docker-compose up`. Production: ADOT Collector sidecar on ECS Fargate forwards to AWS X-Ray.

### ~~5.3 Streaming Responses~~ ✅

~~Switch the agent endpoint from batch to streaming.~~

- ~~Use `IAsyncEnumerable<string>` and `GetStreamingChatMessageContentsAsync`~~
- ~~Stream partial answers to the client via SSE or chunked transfer~~
- ~~**Why**: Users expect real-time feedback from AI. Streaming is the standard UX pattern.~~
- **Done**: `AgentStreamingService` pipelines `ResearcherAgent` → `WriterAgent.StreamAsync` (skipping the critic loop). `POST /api/agent/ask/stream` returns SSE frames: `status`, `sources`, `token`, `done`, `error`. ~1 s to first token vs ~10 s for the batch endpoint (one LLM call vs two–three).

---

## Recommended Learning Order

| Order | Topic | Builds On |
|-------|-------|-----------|
| 1 | Structured output (1.1) | Current agent |
| 2 | Prompt templates (1.2) | Current agent |
| 3 | Multi-turn chat (1.3) | Current agent |
| 4 | Multi-tool agent (2.1, 2.2) | Phase 1 |
| 5 | Researcher + Writer (3.1) | Phase 2 |
| 6 | Self-reflection loop (3.2) | 3.1 |
| 7 | Evaluation agent (4.2) | Phase 3 |
| 8 | Ingestion agent (4.1) | Existing PostIndexingService |
| 9 | Guardrails (5.1) | Existing filters |
| 10 | Observability (5.2) | All phases |
| 11 | Streaming (5.3) | All phases |
| 12 | Microsoft Agent Framework workflow (3.3) | Phase 3 |

Start at the top, ship each one, then move on. Every item is implementable in this repo.
