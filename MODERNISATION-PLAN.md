# Modernisation Plan — .NET 10, Microsoft.Extensions.AI, Agent Framework and Bedrock AgentCore

**Status:** proposed · **Date:** 2026-09-25

## Summary

**Can we do it? Yes, for all three.** Each one also fixes a workaround we have today:

| Question | Answer | Why it's worth it here |
|---|---|---|
| Replace Semantic Kernel with **Microsoft.Extensions.AI (MEAI)**? | Yes. We already use MEAI for embeddings (`CohereEmbeddingGenerator` implements `IEmbeddingGenerator`). | The SK Bedrock connector is still `-alpha` (`1.72.0-alpha`) and doesn't support tool calling for Claude ([SK#9750](https://github.com/microsoft/semantic-kernel/issues/9750)), which is why `ResearcherAgent` calls the plugin directly. AWS's `AWSSDK.Extensions.Bedrock.MEAI` uses the Converse API and supports tool calling. |
| Replace SK Agents/Process with **Microsoft Agent Framework (MAF)**? | Yes. MAF 1.0 has been GA since April 2026 and replaces SK and AutoGen. Its graph Workflows are stable. | `Microsoft.SemanticKernel.Process.*` is still alpha. MAF Workflows do the same Research → Write → Critic loop, and they add streaming, checkpointing and human-in-the-loop. That means the streaming endpoint no longer has to skip the critic. |
| Use **Amazon Bedrock AgentCore**? | Yes, one piece at a time. Runtime, Memory, Gateway, Identity, Observability, Policy (GA March 2026) and Evaluations (GA March 2026) all work with any framework. There is a .NET SDK (`AWSSDK.BedrockAgentCore`, `AWSSDK.BedrockAgentCoreControl`) and Terraform support (`aws_bedrockagentcore_*`). | It replaces our in-memory conversation store (lost on every ECS task restart), our regex-only guardrails, our home-grown evaluation, and it gives each agent session its own isolated microVM. |

**What we're aiming for:** a thin ECS API. The agent workflow runs as a MAF Workflow on AgentCore Runtime. Models are called through MEAI `IChatClient`/`IEmbeddingGenerator` backed by Bedrock. Conversation memory lives in AgentCore Memory. Bedrock Guardrails provide safety. Traces go to AgentCore Observability, and quality is measured with AgentCore Evaluations and `Microsoft.Extensions.AI.Evaluation`. Vector storage stays on S3 Vectors.

The rules in `CLAUDE.md` still apply: **Core stays provider-agnostic**, controllers stay thin, and each phase is committed on its own with all tests passing.

---

## Current state (what we're migrating from)

| Concern | Today | File(s) |
|---|---|---|
| Runtime | .NET 8, floating `AWSSDK.* 4.0.*`, no central package management | `*/*.csproj`, `Dockerfile` (`aspnet:8.0`) |
| Chat model | SK `IChatCompletionService` via `AddBedrockChatCompletionService` (alpha connector) + `AmazonClaudeExecutionSettings` | `RagAgent.Agents/ServiceCollectionExtensions.cs`, `WriterAgent.cs`, `CriticAgent.cs` |
| Embeddings | Custom MEAI `IEmbeddingGenerator` calling Cohere Embed v3 via `InvokeModel` | `CohereEmbeddingGenerator.cs`, `EmbeddingService.cs` |
| Tools | SK `[KernelFunction]` plugins; researcher calls the plugin **directly** (no model-driven tool choice) | `SemanticSearchPlugin.cs`, `ResearcherAgent.cs`, `IndexingPlugin.cs` (only used by tests) |
| Orchestration | SK Process (alpha): Research → Write → Critic ⟲ Revise (max 3) → Output | `Process/ProcessAnswerService.cs`, `Process/Steps/*` |
| Streaming | Separate path that bypasses the Process **and the critic** | `RagAgent.Api/Services/AgentStreamingService.cs` |
| Guardrails | Regex/phrase lists in SK `IPromptRenderFilter` / `IFunctionInvocationFilter` + `GuardrailsService` | `Filters/*.cs`, `GuardrailsService.cs` |
| Conversation state | `InMemoryConversationStore` (per task, lost on restart, not shared across tasks) | `RagAgent.InMemory` |
| Observability | OTel → ADOT sidecar → X-Ray; `AddSource("Microsoft.SemanticKernel*")` | `Program.cs`, `infra/otel-collector-config.yaml` |
| Evaluation | Custom hit@k, citation validity, self-reported groundedness | `EvaluationAgent.cs` |
| Hosting | ECS Fargate (x86) behind ALB; Terraform; GitHub Actions | `infra/main.tf`, `.github/workflows/ci-cd.yml` |
| Dead weight | `GroundedAnswer.yaml` is embedded but never loaded; the Handlebars/Yaml SK packages are unused | `RagAgent.Agents.csproj` |

---

## Target architecture

```
                    ┌────────────────────────── ECS Fargate (thin API) ──────────────────────────┐
 Client ──ALB──▶    │ Controllers → Services                                                     │
                    │   /api/posts, /api/search, /api/index  (unchanged, in-process)             │
                    │   /api/agent/ask[/stream] ──▶ IAgentAnswerService ──┐                      │
                    │   IngestionBackgroundService (unchanged)            │ InvokeAgentRuntime   │
                    └─────────────────────────────────────────────────────┼──────────────────────┘
                                                                          ▼  (SigV4, streamed)
                    ┌────────────── AgentCore Runtime (ARM64 container, per-session microVM) ────┐
                    │ RagAgent.AgentHost: POST /invocations, GET /ping                           │
                    │   MAF Workflow: Research ─▶ Write ─▶ Critic ─(revise ≤3)─▶ Output          │
                    │     ResearcherAgent = ChatClientAgent + search_posts tool (model-chosen)   │
                    │     Writer / Critic = ChatClientAgent (structured output)                  │
                    │   IChatClient pipeline: Guardrails → OTel → FunctionInvocation → Bedrock   │
                    └───────┬───────────────────┬───────────────────┬────────────────────────────┘
                            │                   │                   │
                    AgentCore Memory     Bedrock Guardrails    S3 Vectors + Bedrock (Claude, Cohere)
                    (history, summaries) (PII, prompt attack,  (search_posts, directly or via
                                          grounding check)      AgentCore Gateway/MCP — Phase 8)
                            │
                    AgentCore Observability (CloudWatch GenAI) ─▶ AgentCore Evaluations (online)
```

### Project layout at the end

| Project | Change |
|---|---|
| `RagAgent.Core` | Unchanged role. Gains `IAgentRuntimeClient`-free contracts only; no AWS, MAF or SK references. |
| `RagAgent.Agents` | SK removed. MEAI + MAF only: agents, tools (`AIFunction`), workflow, chat-client middleware. |
| `RagAgent.Bedrock` *(new, optional split)* | Bedrock-specific wiring: `IChatClient`/`IEmbeddingGenerator` factories, Bedrock Guardrails middleware. Keeps `RagAgent.Agents` provider-neutral enough to unit test against fakes. |
| `RagAgent.AgentCore` *(new)* | AgentCore providers: `AgentCoreMemoryConversationStore`, `AgentCoreRuntimeAnswerService` (client that calls the runtime). |
| `RagAgent.AgentHost` *(new)* | Minimal ASP.NET Core host for AgentCore Runtime (`/invocations`, `/ping`, port 8080, `linux/arm64`). |
| `RagAgent.Api` | Thinner: agent endpoints delegate to `IAgentAnswerService`, which is in-process locally and AgentCore Runtime in production. |
| `RagAgent.InMemory`, `RagAgent.Qdrant`, `RagAgent.Redis` | Kept for local dev / integration tests. |

---

## Guiding principles

1. **Strangler, not big-bang.** Each phase ships working software behind the existing `Core` interfaces (`IAgentAnswerService`, `IWriterAgent`, `ICriticAgent`, `IConversationStore`, `IGuardrailsService`). Controllers and tests should barely notice.
2. **Measure before and after.** Phase 0 records a baseline with `/api/agent/evaluate`. Every later phase must not regress hit@k, citation validity or groundedness.
3. **Local first.** Every AWS-managed capability has a local implementation (in-memory store, regex guardrails, in-process workflow). This keeps the `Testcontainers` integration suite fast and credential-free.
4. **Config-driven switches**, e.g. `Agent:Host = InProcess | AgentCore` and `ConversationStore:Provider = InMemory | AgentCore`, the same way `VectorStore:Provider` works today.

---

## Phase 0 — Baseline and safety net *(~1 day)*

**Goal:** have a reliable way to tell whether the migration broke anything.

1. Add a checked-in evaluation set, `eval/questions.json`: 20–30 questions with `ExpectedPostIds`. Use a pinned HackerNews snapshot (reuse `TestPostService`) so results can be repeated.
2. Run `/api/agent/evaluate` against the current SK build. Save the report as `eval/baseline-sk.json` (hit@k, citation validity, groundedness, mean iterations, p50/p95 latency).
3. Add characterisation unit tests around the behaviour we must keep:
   - The critic loop ends after 3 iterations, and the final iteration skips the critic (`WriteStep.MaxIterations`).
   - Citations to postIds not in the sources are removed (`AgentOrchestrationService.SanitiseAnswer`).
   - An unparseable critic response counts as approved.
   - Unparseable writer JSON falls back to the deterministic answer.
   - SSE event order: `status → sources → status → token* → done`.
4. Remove dead code now so it doesn't get migrated: the `GroundedAnswer.yaml` embedded resource, `Microsoft.SemanticKernel.PromptTemplates.Handlebars`, `Microsoft.SemanticKernel.Yaml`. Either delete `IndexingPlugin` (only tests use it) or fold it into `PostIndexingService`.

**Done when:** the baseline report is committed and the new tests pass on the current SK code.

---

## Phase 1 — Platform refresh: .NET 10 LTS *(~1–2 days)*

**Goal:** move to the current LTS runtime and SDK before touching AI code, so framework problems aren't mixed up with AI changes.

1. Add a `global.json` pinning the .NET 10 SDK. Set `<TargetFramework>net10.0</TargetFramework>` in every project. Move the shared `TargetFramework`, `Nullable`, `ImplicitUsings` and `LangVersion` into `Directory.Build.props`.
2. Adopt **Central Package Management** (`Directory.Packages.props`, `ManagePackageVersionsCentrally`). Replace the floating `AWSSDK.* 4.0.*` versions with exact pins, and add Dependabot or Renovate.
3. Package bumps: `Microsoft.Extensions.*` 10.x, `Microsoft.AspNetCore.Mvc.Testing` 10.x, OpenTelemetry 1.1x current, `Microsoft.NET.Test.Sdk`, and optionally xUnit v3.
4. Replace **Swashbuckle** with the built-in `Microsoft.AspNetCore.OpenApi` document generation (`AddOpenApi`/`MapOpenApi`), plus Scalar or Swagger UI for the UI. Keep the `Swagger:Enabled` behaviour.
5. Dockerfile: `mcr.microsoft.com/dotnet/aspnet:10.0` and `sdk:10.0`. Consider the chiseled/distroless image (`aspnet:10.0-noble-chiseled`) and build **multi-arch** (`linux/amd64,linux/arm64`) with `docker buildx`, because AgentCore Runtime (Phase 6) is ARM64-only.
6. CI: `actions/setup-dotnet` → `10.0.x`. Also consider moving ECS to Graviton (`runtime_platform { cpu_architecture = "ARM64" }`) to cut cost.

**Done when:** `dotnet build`, `dotnet format --severity warn`, and unit and integration tests are green on .NET 10, and the ECS deploy works.

---

## Phase 2 — Model access on Microsoft.Extensions.AI *(~2–3 days)*

**Goal:** remove every `IChatCompletionService`, `Kernel` and `AmazonClaudeExecutionSettings` call from the agents. SK Process still orchestrates for now, and steps resolve agents from DI as they do today.

1. Add `AWSSDK.Extensions.Bedrock.MEAI` and `Microsoft.Extensions.AI`. Register a single `IChatClient` pipeline:
   ```csharp
   services.AddAWSService<IAmazonBedrockRuntime>();
   services.AddChatClient(sp => sp.GetRequiredService<IAmazonBedrockRuntime>().AsIChatClient(options.ChatModelId))
       .UseLogging()
       .UseOpenTelemetry(sourceName: AgentActivitySource.Name, configure: c => c.EnableSensitiveData = false)
       .UseFunctionInvocation();   // real tool calling for Claude via Converse
   ```
   If the critic should use a cheaper model, add a second keyed `IChatClient` (`AddKeyedChatClient("critic", …)`), e.g. Claude Haiku 4.5, with Sonnet for the writer. Look up the exact inference-profile IDs in the Bedrock console for the region.
2. **WriterAgent / CriticAgent:** replace `ChatHistory` with `List<Microsoft.Extensions.AI.ChatMessage>`, `GetChatMessageContentsAsync` with `GetResponseAsync`, and `GetStreamingChatMessageContentsAsync` with `GetStreamingResponseAsync`. Replace `MaxTokensToSample` with `ChatOptions.MaxOutputTokens`.
   - Note the naming clash between `RagAgent.Core.Models.ChatMessage` and MEAI's `ChatMessage`. Use an alias, or rename the Core type to `ConversationMessage`.
3. **Structured output:** try `GetResponseAsync<StructuredLlmAnswer>()` / `ChatResponseFormat.ForJsonSchema<T>()` against Bedrock Converse. If the Bedrock MEAI adapter doesn't enforce the schema, keep `AgentJsonHelpers.ExtractJson` as the fallback, which is what we have today, so there's no regression either way.
4. **Embeddings:** keep `CohereEmbeddingGenerator`, since it is already MEAI. Optionally spike `bedrockRuntime.AsIEmbeddingGenerator(modelId)` and check that it forwards Cohere's `input_type` (`search_document`/`search_query`). If it doesn't, keep ours. Wrap it with `.UseOpenTelemetry()` through `EmbeddingGeneratorBuilder`.
   - *Optional:* evaluate **Cohere Embed v4** with `output_dimension = 1024`. That keeps the S3 Vectors index dimension the same, but still needs a full re-index. Gate it on the Phase 0 eval.
5. **Guardrails:** move the regex checks out of the SK filters into a `GuardrailChatClient : DelegatingChatClient` that checks the last user message. The logic stays in a plain static class for unit tests. `GuardrailsService` keeps its API. Delete `InputGuardrailFilter`/`OutputGuardrailFilter` as SK types.
6. **SemanticSearchPlugin:** remove `[KernelFunction]` and keep `[Description]`. It becomes a plain class that later phases wrap with `AIFunctionFactory.Create`.
7. Unit tests: replace SK mocks with a small `FakeChatClient : IChatClient` that returns scripted `ChatResponse`s. This is much simpler than mocking SK.

**Done when:** no `Microsoft.SemanticKernel*` usings remain outside `Process/`, and the eval matches the Phase 0 baseline within ±5% or better.

---

## Phase 3 — Orchestration on Microsoft Agent Framework *(~3–5 days)*

**Goal:** swap SK Process for MAF agents and Workflows, remove Semantic Kernel completely, and use the same pipeline for batch and streaming.

1. Add `Microsoft.Agents.AI` and `Microsoft.Agents.AI.Workflows` (1.x, GA).
2. **Real agents:**
   - `ResearcherAgent` becomes a `ChatClientAgent` with instructions and `Tools = [AIFunctionFactory.Create(searchPlugin.SearchPostsAsync, "search_posts")]`. **The model now decides** when and how to search (query rewriting, several searches for compound questions), which removes the SK#9750 workaround. Keep a deterministic fallback: if the agent makes no tool call, call search directly so we never answer without retrieval.
   - `WriterAgent` and `CriticAgent` become `ChatClientAgent`s with instructions only (no tools) and structured output. The deterministic citation check in the critic stays as C# code *before* the LLM call.
   - Keep the `IResearcherAgent`/`IWriterAgent`/`ICriticAgent` interfaces in Core. The MAF types are implementation details in `RagAgent.Agents`.
3. **Workflow instead of SK Process** (`RagAgent.Agents/Workflow/AnswerWorkflow.cs`):
   ```
   ResearchExecutor ─▶ WriteExecutor ─▶ CriticExecutor ─┬─(approved or iteration ≥ 3)─▶ OutputExecutor
                          ▲                             │
                          └──────(revision requested)───┘
   ```
   Build it with `WorkflowBuilder`, executors per step, and a **conditional edge** on `CriticResult.Approved`. The iteration counter goes in workflow/executor state instead of `KernelProcessStepState`. Run it with `InProcessExecution`. This lets us delete `ProcessResultHolder`: the output executor yields the workflow output directly.
4. **Streaming with the critic:** have `AgentStreamingService` consume the workflow's streaming events (`AgentRunUpdateEvent` / executor events) and map them to the existing `StreamEventDto` types (`status`, `sources`, `token`, `done`). Decide on the UX:
   - *Option A (recommended):* stream the first draft's tokens, then emit a `revision` event if the critic rejects it, and stream the revised draft.
   - *Option B:* stream status events only while the critic runs, then stream the approved answer.
   Either way the streaming path now gets the same quality gate as `/ask`. Add a new `StreamEventDto` type if needed (a contract change, so update the Postman collection).
5. **Middleware instead of SK filters:** agent-run middleware for the input guardrail and span tagging, and function-invocation middleware for the old `ToolInvocationFilter` (topK normalisation, logging) and the output-size and harmful-term warnings.
6. **Conversation threads:** map `IConversationStore` history into the agent's `AgentThread` for each run. Phase 5 then swaps the store for AgentCore Memory.
7. Remove all `Microsoft.SemanticKernel*` packages and the `Process/` folder. Change the OTel source to `AddSource("Microsoft.Extensions.AI", "Microsoft.Agents.AI*", AgentActivitySource.Name)`.
8. Update `CLAUDE.md`, `RagAgent.Core/CLAUDE.md`, `README.md` and `Best next integrations.md` so they mention MAF instead of SK.

**Done when:** there are zero SK references, the workflow unit tests cover approve, revise-then-approve, max-iterations and the no-tool-call fallback, and the eval is at or above baseline (we expect hit@k to improve from model-driven query rewriting).

---

## Phase 4 — Managed safety: Amazon Bedrock Guardrails *(~1–2 days)*

**Goal:** replace the phrase lists with a managed, tunable guardrail. Keep the regex checks only as a local and offline fallback.

1. Terraform: `aws_bedrock_guardrail` + `aws_bedrock_guardrail_version` with:
   - **Prompt attack** filter (replaces the injection phrase list)
   - **Sensitive information** filters: EMAIL, PHONE, CREDIT_DEBIT_CARD_NUMBER, set to `BLOCK` on input and `ANONYMIZE` on output
   - **Denied topics**: legal, medical, financial and tax advice (replaces `OffTopicPhrases`)
   - **Contextual grounding check** (grounding + relevance thresholds). This is a managed hallucination check that complements the critic agent.
2. Add a `BedrockGuardrailChatClient : DelegatingChatClient` that calls `ApplyGuardrail` on the input, and on the output with the retrieved sources as the grounding source. Alternatively, pass `guardrailConfig` through the Converse request if the MEAI adapter exposes it via `ChatOptions.AdditionalProperties` / `RawRepresentationFactory`. Map blocked results to the existing `GuardrailException` so controllers and status codes stay the same.
3. IAM: add `bedrock:ApplyGuardrail` to the task/runtime role.
4. Config: `Guardrails:Provider = Regex | Bedrock` (`Regex` for local and integration tests).

**Done when:** the existing `GuardrailTests` cases pass against both providers (the Bedrock ones as a small opt-in integration suite), and the grounding-check score is recorded as an eval metric.

---

## Phase 5 — Durable conversations: AgentCore Memory *(~2–3 days)*

**Goal:** conversations survive restarts and scale-out, and we get long-term memory (summaries and user preferences) without running our own database.

1. Terraform: `aws_bedrockagentcore_memory` with an event expiry (e.g. 30 days). Optionally add a **summary** strategy (session summaries) and a **semantic** strategy (facts across sessions).
2. New project `RagAgent.AgentCore` with `AgentCoreMemoryConversationStore : IConversationStore` using `AWSSDK.BedrockAgentCore`:
   - `AppendAsync` → `CreateEvent` (actorId = user/tenant, sessionId = conversationId)
   - `GetHistoryAsync` → `ListEvents` (last N turns)
   - `ListConversationIdsAsync` → `ListSessions`
   - `DeleteAsync` → delete the session's events
   - `Subscribe`: AgentCore Memory has no push model. Only unit tests use `Subscribe` today, so either move it to a separate `IConversationEventStream` interface (in-memory only) or remove it. This is a Core contract change, so agree it before starting.
3. Optional: use long-term memory records (`RetrieveMemoryRecords`) as extra context for the writer, e.g. "the user previously asked about X".
4. Identity: conversations need an **actor ID**. Today there's no auth, so use a fixed actor at first. Wire real identity in Phase 7.
5. Config: `ConversationStore:Provider = InMemory | AgentCore`.

**Done when:** a conversation continues across an ECS redeploy, the `ConversationsController` endpoints behave the same, and the unit tests use a faked `IAmazonBedrockAgentCore`.

---

## Phase 6 — Agent hosting: AgentCore Runtime *(~3–4 days)*

**Goal:** run the agent workflow on AgentCore Runtime (session-isolated microVMs, sessions up to 8 hours, pay per use, built-in observability) and keep the plain CRUD/search/index API on ECS.

> **Why split rather than move everything?** AgentCore Runtime is designed around an invocation contract (`POST /invocations`, `GET /ping`) per agent session. Our posts/search/index endpoints and the `IngestionBackgroundService` are ordinary long-running API and worker workloads, and they are a better fit for ECS. Moving only the agent keeps each part in the right place.

1. New project `RagAgent.AgentHost`: a minimal ASP.NET Core app on port **8080**, `linux/arm64`:
   - `GET /ping` → `{ "status": "Healthy" }` (or `HealthyBusy` while a long run is in progress)
   - `POST /invocations` → takes `{ question, topK, conversationId }`, runs the MAF workflow, and streams SSE back (the same `StreamEventDto` shape) or returns JSON for batch
   - It reads the session from the `X-Amzn-Bedrock-AgentCore-Runtime-Session-Id` header.
   - It reuses `AddVectorSearch`, the vector store providers, and `AgentCoreMemoryConversationStore`.
2. `RagAgent.AgentCore/AgentCoreRuntimeAnswerService : IAgentAnswerService` (+ a streaming counterpart) calls `InvokeAgentRuntime` through `AWSSDK.BedrockAgentCore`. Use `runtimeSessionId = conversationId`, which must be at least 33 characters; GUIDs are 36. `Agent:Host = InProcess | AgentCore` chooses the implementation, so local dev and integration tests stay in-process.
3. Terraform:
   - A second ECR repo (or tag prefix) for the ARM64 agent image
   - `aws_bedrockagentcore_agent_runtime` (container URI, env vars, `network_mode = PUBLIC` at first, VPC later) + `aws_bedrockagentcore_agent_runtime_endpoint`
   - An IAM execution role for the runtime (Bedrock invoke, S3 Vectors, AgentCore Memory, ApplyGuardrail, CloudWatch/X-Ray)
   - `bedrock-agentcore:InvokeAgentRuntime` on the ECS task role
   - Update `infra/deploy-role-policy.json` with `bedrock-agentcore:*` control-plane actions
4. CI/CD: add a `docker buildx --platform linux/arm64` job for `RagAgent.AgentHost` and deploy it by updating the runtime's container URI (Terraform variable or `UpdateAgentRuntime`) after the push.
5. Remove the Bedrock and S3 Vectors permissions the ECS task no longer needs. Search and indexing still need them, so keep those.

**Done when:** in production `/api/agent/ask` and `/ask/stream` go through AgentCore Runtime with p95 latency within an agreed budget of baseline, and local and integration tests still run in-process.

---

## Phase 7 — Tools, identity and governance: AgentCore Gateway, Identity, Policy *(optional, ~3–5 days)*

**Goal:** make tools reusable across agents over MCP, and put auth and policy in front of them. **Do this once there's a second agent or tool consumer**, such as the ingestion or evaluation agents on the roadmap. Until then, in-process `AIFunction` tools are simpler.

1. **Gateway:** expose `search_posts` (and later `summarise_post`, `compare_posts`) as MCP tools through `aws_bedrockagentcore_gateway` + `aws_bedrockagentcore_gateway_target`. The target is either a small Lambda (`RagAgent.Tools.Lambda`, .NET 10 Native AOT) or an OpenAPI target that points at the ECS `/api/search` endpoint.
2. The agent consumes those tools via the **MCP C# SDK** (`ModelContextProtocol`) → `McpClientTool` (which is an `AIFunction`) → the `ChatClientAgent` tool list. MAF supports MCP natively, so the agent code barely changes.
3. **Identity:** add inbound JWT auth (Cognito or the corporate IdP) on the API and the runtime. That gives us a real `actorId` for Memory (Phase 5) and per-user rate limits.
4. **Policy:** add Cedar-style AgentCore Policy rules on the Gateway, e.g. "topK ≤ 10", "the ingestion agent may call index tools, the Q&A agent may not".

---

## Phase 8 — Observability: AgentCore Observability *(~1 day, can run alongside Phases 3–6)*

1. MEAI (`UseOpenTelemetry`) and MAF emit **OpenTelemetry GenAI semantic-convention** spans (`gen_ai.*`: model, tokens, tool calls). Keep sensitive-data capture off in production.
2. Enable CloudWatch **Transaction Search** and send traces through ADOT to CloudWatch. The existing sidecar and `otel-collector-config.yaml` already do most of this. Update the exporter so spans show up in the **AgentCore Observability / GenAI Observability** dashboards (sessions, traces, token usage, tool latency, error rates). The runtime-hosted agent gets this automatically.
3. Add metrics: tokens per request, critic revisions per request, guardrail blocks, grounding-check score.
4. Keep Jaeger in `docker-compose.yml` for local dev.

---

## Phase 9 — Evaluation: M.E.AI.Evaluation + AgentCore Evaluations *(~2 days)*

**Goal:** replace self-reported groundedness with independent, repeatable quality scores.

1. **Offline / CI:** extend `EvaluationAgent` with `Microsoft.Extensions.AI.Evaluation.Quality`: `RelevanceEvaluator`, `GroundednessEvaluator` (sources as context), `CompletenessEvaluator`, plus the existing deterministic hit@k and citation validity. Use `Microsoft.Extensions.AI.Evaluation.Reporting` with response caching, so CI runs are cheap and deterministic and produce an HTML report as a CI artefact.
2. Add a CI gate (nightly or on PRs that touch `RagAgent.Agents/**`) that fails if a metric drops more than X% below the committed baseline.
3. **Online:** configure **AgentCore Evaluations** on production traces (built-in evaluators such as correctness, faithfulness, tool-selection accuracy and harmfulness) with CloudWatch alarms on score drift.
4. This finishes the "Evaluation agent" item on the `CLAUDE.md` roadmap.

---

## Phase 10 — Optional extras

| Item | Recommendation |
|---|---|
| **.NET Aspire** AppHost to replace `docker-compose.yml` for local dev (Qdrant, Redis, Jaeger, API, AgentHost) and get the Aspire dashboard for OTel | Recommended: cheap, and a big improvement to local dev. |
| **`Microsoft.Extensions.VectorData`** (`VectorStoreCollection<TKey,TRecord>`) instead of our `IVectorStore` for Qdrant/Redis | Worth a spike. There's no S3 Vectors connector, so we'd write one. Only do it if it makes the code smaller. |
| **Bedrock Knowledge Bases** (managed ingestion, chunking and retrieval, which can use S3 Vectors as the store) instead of our ingestion pipeline | **Not recommended** for this repo. It removes the part we learn most from (chunking, embedding, indexing) and hides retrieval tuning. Keep it as a documented alternative. |
| **A2A protocol** (MAF + AgentCore Runtime A2A mode) | Only if we need agents to talk to other teams' agents. |
| **Ingestion agent** on EventBridge Scheduler → Lambda/AgentCore instead of `IngestionBackgroundService` | Aligns with the roadmap. Do it after Phase 7 so it can reuse the Gateway tools. |

---

## Sequencing and risk

```
P0 ─▶ P1 ─▶ P2 ─▶ P3 ─┬─▶ P4 ─┐
                      ├─▶ P5 ─┼─▶ P6 ─▶ P7 (optional)
                      └─▶ P8 ─┘        P9 (any time after P3)
```

| Risk | Mitigation |
|---|---|
| The Bedrock MEAI adapter may not support JSON-schema structured output for Claude | Keep the `ExtractJson` fallback. Alternatively, force structured output with a single "respond" tool (`ChatToolMode.RequireSpecific`). |
| MAF workflow streaming event types may change between minor versions | Pin MAF with CPM. Keep the event → `StreamEventDto` mapping in one adapter class with unit tests. |
| Latency goes up from the extra network hop (ECS → AgentCore Runtime) and from running the critic when streaming | Measure against the Phase 0 baseline. Stream early tokens (Option A in Phase 3). Use Haiku for the critic. |
| AgentCore is regional; check that `us-east-1` has every component we use | Check before Phase 5. Everything we rely on is GA in us-east-1. |
| ARM64-only runtime | Build multi-arch images from Phase 1 onwards. |
| Cost from AgentCore per-session compute, Memory events and Evaluations | Set Memory event expiry, sample online evaluations, and add AWS Budgets alerts in Terraform. |
| Removing `Subscribe` from `IConversationStore` changes a Core contract | Agree this in Phase 5 before starting. Only tests use it today. |

## Per-phase checklist (from `CLAUDE.md`)

- [ ] `dotnet format RagAgent.sln --severity warn`
- [ ] `dotnet test RagAgent.UnitTests` and `dotnet test RagAgent.IntegrationTests` are green
- [ ] For Terraform changes, `terraform fmt -recursive` and `terraform validate` via Docker
- [ ] The eval report is no worse than the previous phase's baseline
- [ ] Conventional commit (`feat:`/`chore:`/`docs:`), one phase or sub-step per commit, never pushed without being asked

## References

- [Microsoft Agent Framework 1.0 announcement](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/) · [Overview (Microsoft Learn)](https://learn.microsoft.com/en-us/agent-framework/overview/) · [Amazon Bedrock provider for MAF](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/amazon-bedrock)
- [AWSSDK.Extensions.Bedrock.MEAI (NuGet)](https://www.nuget.org/packages/AWSSDK.Extensions.Bedrock.MEAI/)
- [AgentCore Runtime Terraform resource](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/bedrockagentcore_agent_runtime) · [aws-ia/agentcore module](https://registry.terraform.io/modules/aws-ia/agentcore/aws/latest)
- [AgentCore Memory getting started](https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/memory-getting-started.html) · [IAmazonBedrockAgentCore (.NET SDK v4)](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/BedrockAgentCore/TIBedrockAgentCore.html)
- [AgentCore Evaluations and Policy announcement](https://aws.amazon.com/blogs/aws/amazon-bedrock-agentcore-adds-quality-evaluations-and-policy-controls-for-deploying-trusted-ai-agents/)
- [Cohere Embed v3/v4 on Bedrock](https://docs.aws.amazon.com/bedrock/latest/userguide/model-parameters-embed.html)
