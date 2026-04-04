# Mastering Agentic AI in .NET — Learning Roadmap

A progressive roadmap for building real agentic AI skills using Semantic Kernel and this repo as a working lab.

## What you've already built

- Single-tool-calling agent (question → semantic search → grounded answer)
- Semantic Kernel plugins: `SemanticSearchPlugin`, `IndexingPlugin`
- Auto function-calling via `FunctionChoiceBehavior.Auto()`
- Function invocation filter for logging, guardrails, and topK normalization
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
- **Done**: `VectorSearch.S3/Prompts/GroundedAnswer.yaml` loaded via `KernelFunctionYaml.FromPromptYaml()`.

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
- **Done**: `ResearcherAgent` → `WriterAgent` sequential handoff in `MultiAgentAnswerService`. Plugins called directly due to Bedrock caveat below.

> ⚠️ **Bedrock caveat:** The SK Bedrock connector does not support `FunctionChoiceBehavior` for Claude models (microsoft/semantic-kernel#9750 — closed but not fixed in SK directly; maintainers deferred to the AWS SDK's `IChatClient`). Do **not** rely on `FunctionChoiceBehavior.Auto/Required` to dispatch tools at runtime — it silently does nothing and the agent returns a plain-text response with empty sources. Call plugins directly in code, inject results into chat history, then invoke the LLM for synthesis.

### ~~3.2 Agent with a Critic / Self-Reflection~~ ✅

~~Add a review loop where a second agent scores the first agent's output.~~

- ~~Critic agent checks: Are citations real? Is the answer grounded? Is it relevant?~~
- ~~If the critic rejects, loop back to the researcher with feedback~~
- ~~Cap at 2-3 iterations to avoid runaway loops~~
- **Done**: `CriticAgent` evaluates citation validity deterministically (postId existence check) then uses the LLM for relevance and groundedness. `MultiAgentAnswerService` runs up to 3 writer passes, passing critic feedback into each retry. `AgentAnswerResult.Iterations` (and `AskResponseDto.Iterations`) exposes how many passes were needed.

### ~~3.3 SK Process Framework (Nested Steps)~~ ✅

~~Rewrite the orchestration as an SK `Process` with discrete steps.~~

- ~~Step 1: Retrieve sources → Step 2: Generate answer → Step 3: Validate → Step 4: Respond~~
- ~~Each step is independently testable and observable~~
- ~~Add branching: if validation fails, loop back to Step 2 with adjusted prompt~~
- ~~**Why**: The Process framework is SK's answer to complex agent workflows. Learning it early gives you a structured way to build production agent pipelines.~~
- **Done**: `ProcessAnswerService` implements `IAgentAnswerService` as a `KernelProcess` with four discrete steps: `ResearchStep` → `WriteStep` → `CriticStep` → `OutputStep`. The process branches: critic approval routes to output; revision routes back to `WriteStep.ReviseAsync`. A `WriteStepState.Iteration` counter caps loops at 3. `ProcessResultHolder` (scoped) bridges the fire-and-forget process back to the request/response pattern.

---

## Phase 4 — Autonomous Agents

### 4.1 Ingestion Agent

Build an agent that watches for new content and autonomously indexes it.

- Background service that polls for new posts on a timer
- Chunks content, generates embeddings, upserts into the vector store
- Uses the existing `IndexingPlugin` but runs autonomously, not on user request
- **Why**: Not all agents are user-facing. Background autonomous agents are a huge enterprise use case (data pipelines, monitoring, ETL).

### 4.2 Evaluation Agent

Build an agent that scores retrieval and answer quality.

- Define a test question set with expected answers / source IDs
- Agent runs each question, compares results, and computes metrics:
  - **Hit@k**: Did the correct source appear in top-k results?
  - **Groundedness**: Is every claim in the answer backed by a retrieved source?
  - **Hallucination rate**: Does the answer contain claims not in any source?
- Output a score report as structured JSON
- **Why**: You can't improve what you can't measure. Evaluation is the most underrated agentic skill.

---

## Phase 5 — Production Patterns

### 5.1 Guardrails and Safety

Add defense-in-depth to the agent pipeline.

- Input guardrails: prompt injection detection, PII filtering, topic scoping
- Output guardrails: content filtering, citation verification, response length limits
- Implement as SK `IPromptRenderFilter` (input) and `IFunctionInvocationFilter` (output)
- **Why**: Enterprise AI requires safety layers. Building them with SK's filter pipeline is the idiomatic .NET approach.

### 5.2 Observability and Tracing

Add end-to-end traces to the agent execution.

- Integrate OpenTelemetry with SK's built-in instrumentation
- Trace: user question → tool calls → LLM invocations → final response
- Export to a local Aspire dashboard or Jaeger for visualization
- **Why**: When agents misbehave in production, traces are how you debug them. This is essential operational skill.

### 5.3 Streaming Responses

Switch the agent endpoint from batch to streaming.

- Use `IAsyncEnumerable<string>` and `GetStreamingChatMessageContentsAsync`
- Stream partial answers to the client via SSE or chunked transfer
- **Why**: Users expect real-time feedback from AI. Streaming is the standard UX pattern.

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
| 8 | Ingestion agent (4.1) | Existing IndexingPlugin |
| 9 | Guardrails (5.1) | Existing filters |
| 10 | Observability (5.2) | All phases |
| 11 | Streaming (5.3) | All phases |
| 12 | Process framework (3.3) | Phase 3 |

Start at the top, ship each one, then move on. Every item is implementable in this repo.
