# Modernisation Plan: .NET 10, Microsoft.Extensions.AI, Agent Framework and Bedrock AgentCore

**Status:** proposed, revision 2 (addresses review B1–B6 on PR #1) · **Date:** 2026-09-25

## Summary

**Can we do it? Yes, for all three.** Each one also removes a workaround we have today:

| Question | Answer | Why it's worth it here |
|---|---|---|
| Replace Semantic Kernel with **Microsoft.Extensions.AI (MEAI)**? | Yes. We already use MEAI for embeddings (`CohereEmbeddingGenerator` implements `IEmbeddingGenerator`). | The SK Bedrock connector is still `-alpha` (`1.72.0-alpha`) and doesn't support tool calling for Claude ([SK#9750](https://github.com/microsoft/semantic-kernel/issues/9750)), which is why `ResearcherAgent` calls the plugin directly. `AWSSDK.Extensions.Bedrock.MEAI` uses the Converse API and supports tool calling. |
| Replace SK Agents/Process with **Microsoft Agent Framework (MAF)**? | Yes. MAF 1.0 has been GA since 3 April 2026 and replaces SK and AutoGen. Its graph Workflows are stable. | `Microsoft.SemanticKernel.Process.*` is still alpha. MAF Workflows do the same Research → Write → Critic loop and add checkpointing, streaming events and human-in-the-loop. |
| Use **Amazon Bedrock AgentCore**? | Yes, one piece at a time. Runtime, Memory, Gateway, Identity, Observability, Policy (GA March 2026) and Evaluations (GA March 2026) all work with any framework, and there's a .NET SDK (`AWSSDK.BedrockAgentCore`, `AWSSDK.BedrockAgentCoreControl`) and Terraform support (`aws_bedrockagentcore_*`). | It replaces the in-memory conversation store (not shared across ECS tasks and lost on restart) and the home-grown evaluation, and it gives each agent session its own isolated microVM. |

**What we're aiming for:** a thin ECS API that owns the HTTP contract, guardrails and conversation history. It calls a **stateless** MAF Workflow hosted on AgentCore Runtime. Models are reached through MEAI `IChatClient`/`IEmbeddingGenerator` on Bedrock. History lives in AgentCore Memory. Bedrock Guardrails act at the request boundary. Traces go to AgentCore Observability, and quality is measured with M.E.AI.Evaluation and AgentCore Evaluations. S3 Vectors stays as the vector store.

The rules in `CLAUDE.md` still apply: **Core stays provider-agnostic**, controllers stay thin, and each phase is committed on its own with all tests passing.

---

## Current state (what we're migrating from)

| Concern | Today | File(s) |
|---|---|---|
| Runtime | .NET 8, floating `AWSSDK.* 4.0.*`, no central package management. `Directory.Build.props` exists (StyleCop + ruleset). | `*/*.csproj`, `Directory.Build.props`, `RagAgent.Api/Dockerfile` (`aspnet:8.0`, restore layer copies 4 csproj files only) |
| Chat model | SK `IChatCompletionService` via `AddBedrockChatCompletionService` (alpha) + `AmazonClaudeExecutionSettings` | `RagAgent.Agents/ServiceCollectionExtensions.cs`, `WriterAgent.cs`, `CriticAgent.cs` |
| Embeddings | Custom MEAI `IEmbeddingGenerator` calling Cohere Embed v3 via `InvokeModel` (1024 dims) | `CohereEmbeddingGenerator.cs`, `EmbeddingService.cs` |
| Tools | `SemanticSearchPlugin` has `[KernelFunction]`, but `ResearcherAgent` calls it **directly**, not through the kernel. `IndexingPlugin` is only used by tests. | `SemanticSearchPlugin.cs`, `ResearcherAgent.cs`, `IndexingPlugin.cs` |
| Orchestration | SK Process (alpha): Research → Write → Critic ⟲ Revise. **The 3rd draft skips the critic** (`WriteStep.MaxIterations = 3`). | `Process/ProcessAnswerService.cs`, `Process/Steps/*` |
| Streaming | Separate path: research → prose stream (no citations, no critic). `done.grounded` = `sources.Count > 0`. A guardrail violation is **HTTP 200 + a single `error` frame**. | `RagAgent.Api/Services/AgentStreamingService.cs` |
| Guardrails that actually run | `GuardrailsService.ValidateQuestion` **before** the user message is stored, which maps to 400 or the SSE `error` frame. `SanitiseAnswer` (strips citations to unknown postIds, truncates) runs after the pipeline. | `GuardrailsService.cs`, `AgentOrchestrationService.cs`, `AgentStreamingService.cs` |
| Guardrails that are **dead code** | `InputGuardrailFilter` (`IPromptRenderFilter`: no prompt functions are rendered) and `ToolInvocationFilter`/`OutputGuardrailFilter` (`IFunctionInvocationFilter`: the plugin isn't invoked through the kernel). Only the static helpers are used, via `GuardrailsService`. | `Filters/*.cs`, `ToolInvocationFilter.cs` |
| Conversation state | `InMemoryConversationStore`: per task, **30-minute sliding TTL, 40-message cap**, free-text client-supplied `conversationId` | `RagAgent.InMemory` |
| Observability | OTel → ADOT sidecar (added by `scripts/deploy-ecs.sh`, not Terraform) → X-Ray. `AddSource("Microsoft.SemanticKernel*")`. | `Program.cs`, `infra/otel-collector-config.yaml` |
| Evaluation | Hit@k, citation validity, **self-reported** groundedness (the writer grades itself), `AverageLatencyMs` only, live HN data | `EvaluationAgent.cs`, `EvaluationReport.cs` |
| Hosting | ECS Fargate (x86) behind ALB (default 60 s idle timeout). Terraform. GitHub Actions runs `infrastructure` (terraform apply) and then `deploy` on every push. | `infra/main.tf`, `.github/workflows/ci-cd.yml` |
| IAM | ECS task role grants `bedrock:InvokeModel` only, which is **not** enough for `ConverseStream` (`bedrock:InvokeModelWithResponseStream`) | `infra/main.tf` |
| Tests | The integration factory replaces `IAgentAnswerService` and `IAgentStreamingService` with stubs, so **no integration test runs the real agent pipeline**. Unit tests already cover SSE event order, critic parse fallback, citation stripping, writer raw-output fallback and the 40-message cap. | `RagAgent.IntegrationTests/VectorSearchWebApplicationFactory.cs`, `RagAgent.UnitTests/*` |

---

## Invariants: behaviour every phase must keep

Unless a phase explicitly and visibly changes one of these (with a contract note in the PR), they are **regression criteria**:

| # | Invariant |
|---|---|
| I1 | `POST /api/agent/ask` returns 400 with the guardrail reason on an input violation. `POST /ask/stream` returns **200 with one `{"type":"error"}` frame and nothing else**. |
| I2 | Input validation runs **before** the user message is appended to history. A rejected message is never stored. |
| I3 | SSE event order: `status → sources → status → token* → done`. There are no new event types on the existing route. |
| I4 | Loop topology: write → critic → revise, at most 3 drafts, **the 3rd draft skips the critic**. An unparseable critic response counts as approved. |
| I5 | Unparseable writer JSON falls back to the **raw LLM output** with `Grounded = false`. Empty output falls back to the deterministic evidence answer. |
| I6 | Citations to postIds not in the retrieved sources are stripped. Answers are truncated at `MaxAnswerLength`. |
| I7 | The client's `topK` (normalised to 1–10) is the **maximum** number of sources returned. |
| I8 | Conversation history is capped at 40 messages and expires after 30 minutes of inactivity. |
| I9 | Any free-text `conversationId` the client sends today still works. |
| I10 | No agent-path request returns 500 where it returns 200/400 today. |

---

## Target architecture

```
                 ┌─────────────────────────── ECS Fargate (API: owns contract, guardrails, history) ─┐
 Client ─ALB─▶   │ Controllers → AgentOrchestrationService / AgentStreamingService                   │
                 │   1. IGuardrailsService.ValidateQuestion(question)      (I1, I2)                  │
                 │   2. IConversationStore.GetHistory / Append(user)       (AgentCore Memory)        │
                 │   3. IAgentAnswerService.AnswerAsync(AgentAnswerRequest) ───────────┐             │
                 │   4. SanitiseAnswer + output guardrail on final answer   (I6)       │             │
                 │   5. Append(assistant); map to AskResponseDto / StreamEventDto      │             │
                 │ /api/posts, /api/search, /api/index, IngestionBackgroundService     │ in-process  │
                 └─────────────────────────────────────────────────────────────────────┼─ or ────────┘
                                                                                       ▼  InvokeAgentRuntime
                 ┌──────────── AgentCore Runtime (ARM64, per-session microVM, STATELESS) ─────────────┐
                 │ RagAgent.AgentHost: POST /invocations, GET /ping                                   │
                 │   input: question, topK, history   output: AgentAnswerResult | AgentStreamEvent*   │
                 │   MAF Workflow: Research → Write → Critic ⟲ Revise (3rd draft skips critic, I4)    │
                 │   IChatClient: OTel → FunctionInvocation → Bedrock (no guardrail middleware)       │
                 └──────────────┬─────────────────────────────────────────────────────────────────────┘
                                ▼
                     Bedrock (Claude, Cohere) + S3 Vectors
```

**Ownership rule:** the API owns guardrails, history, sanitisation and the wire format. The agent host is pure compute: it gets `question`, `topK` and `history` and returns a result or a stream of Core `AgentStreamEvent`s. This avoids writing history twice, keeps `StreamEventDto` in `RagAgent.Api`, and means guardrails run once per request.

### Project layout at the end

| Project | Change |
|---|---|
| `RagAgent.Core` | Adds `AgentAnswerRequest` (question, topK, history, sessionId) and `AgentStreamEvent` models, renames `ChatMessage` → `ConversationMessage`, splits `Subscribe` out of `IConversationStore` into `IConversationEventStream`. No AWS/MAF/SK references. |
| `RagAgent.Agents` | SK removed. MEAI + MAF: agents, `AIFunction` tools, workflow. Provider-neutral and unit-tested against `FakeChatClient`. |
| `RagAgent.Bedrock` *(new)* | Bedrock wiring: `IChatClient`/`IEmbeddingGenerator` registration, `BedrockGuardrailsService : IGuardrailsService`. |
| `RagAgent.AgentCore` *(new)* | `AgentCoreMemoryConversationStore : IConversationStore`, `AgentCoreRuntimeAnswerService : IAgentAnswerService` (runtime client). |
| `RagAgent.AgentHost` *(new)* | Minimal ASP.NET Core host for AgentCore Runtime (`/invocations`, `/ping`, port 8080, `linux/arm64`). |
| `RagAgent.Api` | Agent endpoints unchanged. Picks in-process or runtime `IAgentAnswerService` via `Agent__Host`. |
| `RagAgent.InMemory`, `RagAgent.Qdrant`, `RagAgent.Redis` | Kept for local dev / integration tests. |
| Tests | `RagAgent.UnitTests` covers every new provider project with faked SDK clients (`IAmazonBedrockAgentCore`, `IAmazonBedrockRuntime`), which keeps the ~70/30 unit/integration ratio. Integration tests add **one real-pipeline test** (real `IAgentAnswerService` + `FakeChatClient`) and one AgentHost `/invocations` contract test. |

---

## Guiding principles

1. **Strangler, not big-bang.** Each phase ships behind the existing Core interfaces, and the invariants above are the regression criteria.
2. **Measure with a frozen corpus** (Phase 0), not live HN data, and gate on confidence intervals, not a flat ±5%.
3. **Local first.** Every managed capability has a local implementation, so the Testcontainers suite stays credential-free.
4. **Runtime-switchable.** Switches are **environment variables set in the ECS task definition** (`deploy-ecs.sh` already injects env vars), not values baked into `appsettings.json`. Rolling back means registering a task definition again, not rebuilding the image: `Agent__Host`, `ConversationStore__Provider`, `Guardrails__Provider`, `Guardrails__Mode`.
5. **Every phase has a rollback line.** It says which flag to flip, which Terraform resources can safely stay, and what data is left behind.

---

## Phase 0: Baseline and safety net *(~2–3 days)*

**Goal:** a regression signal we can trust, built on Core interfaces so it survives the SK → MAF swap.

1. **Frozen evaluation corpus.**
   - Add `scripts/snapshot-hn.sh` to capture ~200 real HN posts once into `eval/corpus.json`.
   - Add a `SnapshotPostService : IPostService` (in `RagAgent.HackerNews`, selected by `DataSource:Provider = Snapshot`) that serves that file.
   - Point evaluation at a **dedicated eval index/collection** so `IngestionBackgroundService` never changes it.
   - Write `eval/questions.json` (40–60 questions, so one question is ≤2.5%) with `ExpectedPostIds` taken from the snapshot, so they never drift.
2. **Better metrics** (small code change):
   - Add `P50LatencyMs`/`P95LatencyMs` to `EvaluationReport`.
   - Add an **independent groundedness and relevance judge** using `Microsoft.Extensions.AI.Evaluation.Quality` (`GroundednessEvaluator`, `RelevanceEvaluator`) with response caching. This replaces relying on the writer's self-reported `Grounded` flag. It is the part of Phase 9 we need up front.
3. **Deterministic runs.** Run the eval **5 times** at temperature 0 against the current SK build. Commit `eval/baseline-sk.json` with the mean and 95% confidence interval per metric. **Gate for later phases:** a metric fails if its new mean falls below the baseline CI's lower bound. Latency fails if p95 goes above an agreed budget (default +25%).
4. **Characterisation tests against Core interfaces** (so they run unchanged against the MAF workflow in Phase 3):
   - `IAgentAnswerService` loop tests with stub `IResearcherAgent`/`IWriterAgent`/`ICriticAgent`:
     - approve first time
     - revise then approve
     - three rejections, where the 3rd draft skips the critic (I4)
     - `Iterations` is reported correctly
   - SSE **error path** test: a guardrail violation gives exactly one `error` frame, and nothing is appended to the store (I1, I2).
   - Keep the existing tests. They already cover SSE order, critic parse fallback, citation stripping, writer raw-output fallback (I5) and the history cap (I8).
5. **One real-pipeline integration test.** Add a second `WebApplicationFactory` variant that keeps the real `IAgentAnswerService` and replaces only the model layer. It uses a stub `IChatCompletionService` now and becomes `FakeChatClient` in Phase 2. Assert I1–I6 through HTTP.
6. **Remove dead code** (no behaviour change, because none of it runs today):
   - the unused `GroundedAnswer.yaml` resource
   - the Handlebars/Yaml SK packages
   - `IndexingPlugin`, with its test moved to `PostIndexingService`
7. **Check a production gap:** the ECS task role lacks `bedrock:InvokeModelWithResponseStream`. Confirm whether `/ask/stream` works in production today, and fix the IAM in this phase so the baseline covers streaming.

**Done when:** the baseline is committed and the new tests pass on the current SK code.
**Rollback:** nothing to roll back. These are tests, eval assets and dead-code removal.

---

## Phase 1: Platform refresh: .NET 10 LTS *(~2 days)*

**Goal:** move to the current LTS runtime without changing behaviour.

1. Add a `global.json` pinning the .NET 10 SDK. Set `net10.0` everywhere. Move the shared `TargetFramework`/`Nullable`/`ImplicitUsings`/`LangVersion` into the **existing** `Directory.Build.props`.
2. **Central Package Management** (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`):
   - Pin exact versions in place of `AWSSDK.* 4.0.*`.
   - Move the StyleCop reference in `Directory.Build.props` to a `<GlobalPackageReference>` (an inline `Version` would trigger NU1008 under CPM).
   - Add Dependabot or Renovate.
3. **Dockerfile restore layer:** copy `global.json`, `NuGet.config`, `Directory.Build.props`, `Directory.Packages.props`, `stylecop.json`, `rules.ruleset` and **every** `*.csproj` the API references before `dotnet restore`. Today only four are copied, and restore only works because `dotnet build` re-restores after `COPY . .`. Check with a clean `docker build --no-cache` in CI before merging.
4. Package bumps: `Microsoft.Extensions.*` 10.x, `Mvc.Testing` 10.x, OpenTelemetry current, test SDK.
5. Replace **Swashbuckle** with built-in `AddOpenApi`/`MapOpenApi` + Swagger UI or Scalar, keeping `Swagger:Enabled`. **Contract note:** the document moves from `/swagger/v1/swagger.json` to `/openapi/v1.json`. Either map the old path as an alias or update the Postman collection and README in the same commit.
6. Base images: `aspnet:10.0` / `sdk:10.0`. The `-noble-chiseled` variant is optional.
7. **ARM64 readiness, in its own commits and in this order:**
   1. Build the image **multi-arch without QEMU**: use `FROM --platform=$BUILDPLATFORM sdk:10.0` and `dotnet publish -a $TARGETARCH`, which cross-compiles managed code natively, then `docker buildx build --platform linux/amd64,linux/arm64`. Alternatively, use a native `ubuntu-24.04-arm` runner. Update `scripts/bootstrap-ecr-image.sh` to push a multi-arch bootstrap image too.
   2. Check `docker manifest inspect` shows both architectures for the deployed tag **and** the bootstrap tag.
   3. *Optional, separate commit:* switch ECS to Graviton (`runtime_platform { cpu_architecture = "ARM64" }`).
8. CI: `setup-dotnet` → `10.0.x`.

**Done when:** a clean Docker build passes, unit and integration tests are green, the ECS deploy is healthy, and the eval is within the baseline CI.
**Rollback:** revert the commit. The ECS task definition still points at the previous image. The Graviton switch is its own revertible commit.

---

## Phase 2: Model access on Microsoft.Extensions.AI *(~2–3 days)*

**Goal:** remove `IChatCompletionService`/`Kernel`/`AmazonClaudeExecutionSettings` from the agents, **keeping I1–I10**. SK Process still orchestrates.

1. **Rename Core `ChatMessage` → `ConversationMessage`** as its own commit first. It touches every Core interface and `ConversationEvent`, but it removes the naming clash with MEAI's `ChatMessage`.
2. Add `AWSSDK.Extensions.Bedrock.MEAI` + `Microsoft.Extensions.AI`, and register one pipeline:
   ```csharp
   services.AddAWSService<IAmazonBedrockRuntime>();
   services.AddChatClient(sp => sp.GetRequiredService<IAmazonBedrockRuntime>().AsIChatClient(options.ChatModelId))
       .UseLogging()
       .UseOpenTelemetry(sourceName: AgentActivitySource.Name, configure: c => c.EnableSensitiveData = false)
       .UseFunctionInvocation();
   ```
   **No guardrail middleware in this pipeline.** Guardrails stay at the request boundary (see Phase 4). The writer and critic send the sources JSON as user content, and the PII regex matches 8-digit HN post IDs and long floats, so a guardrail on every LLM call would reject every request (breaking I1 and I10).
   Optionally register a keyed `IChatClient` for the critic, e.g. Claude Haiku 4.5, using the inference-profile ID from the Bedrock console.
3. **WriterAgent / CriticAgent:** switch to `GetResponseAsync`/`GetStreamingResponseAsync` with `ChatOptions.MaxOutputTokens`, and **keep the prompt-plus-`AgentJsonHelpers.ExtractJson` approach unchanged** (I5).
   - **Don't set `ChatOptions.ResponseFormat` by default.** The Bedrock MEAI adapter implements it as a forced synthetic tool ([aws-sdk-net#4113](https://github.com/aws/aws-sdk-net/pull/4113)). It throws `ArgumentException` when combined with user tools and `NotSupportedException` on streaming, and either would be a 500.
   - *Optional spike:* try `ResponseFormat` **only** on the non-streaming, tool-free writer and critic calls, behind `Agent__UseSchemaOutput=true`. Add a unit test proving it's never set on the researcher (which has tools) or on any streaming call.
4. **Embeddings:** keep `CohereEmbeddingGenerator`, since it's already MEAI, and wrap it with `EmbeddingGeneratorBuilder.UseOpenTelemetry()`. *Optional:* spike Cohere Embed v4 at `output_dimension = 1024`. If adopted, deploy it **blue/green**: build a new index, re-embed everything, run the eval, then switch `VectorIndexName`. Never re-index in place, because that mixes v3 and v4 vectors.
5. **Delete the dead SK filters** (`InputGuardrailFilter`, `OutputGuardrailFilter`, `ToolInvocationFilter`). Move the static check helpers into `RegexGuardrails` (used by `GuardrailsService`). There's no behaviour change, because the filters never ran.
6. `SemanticSearchPlugin`: remove `[KernelFunction]` and keep `[Description]`.
7. Tests: add `FakeChatClient : IChatClient` (scripted responses) and switch the real-pipeline integration test from Phase 0 over to it.
8. IAM: add `bedrock:InvokeModelWithResponseStream` (Converse streaming) if Phase 0 didn't already.

**Done when:** no `Microsoft.SemanticKernel*` usings remain outside `Process/`, the invariant tests pass, and the eval is within the baseline CI.
**Rollback:** revert the commit. This phase has no infrastructure or data changes.

---

## Phase 3: Orchestration on Microsoft Agent Framework *(~3–5 days)*

**Goal:** replace SK Process with a MAF Workflow and remove SK completely, with the **same topology and the same streaming contract**.

1. Add `Microsoft.Agents.AI` + `Microsoft.Agents.AI.Workflows` (1.x), pinned via CPM.
2. **Agents:**
   - **Researcher** is a `ChatClientAgent` with the `search_posts` tool (`AIFunctionFactory.Create`). The model chooses the search query, which removes the SK#9750 workaround. Guard rails for the tool:
     - **I7:** the tool clamps its `topK` argument to the client's normalised `topK`, which is captured in the tool closure. The model can ask for fewer results, never more.
     - **Multiple searches:** merge results by `PostId`, keep the lowest distance, order by distance, and cap at `topK`.
     - **Fallback:** if the model makes no tool call, call search directly with the original question, so there's never an answer without retrieval.
     - **Never set `ResponseFormat`** on the researcher (see Phase 2).
   - **Writer** and **critic** are `ChatClientAgent`s with instructions only. The deterministic citation check stays in C# before the critic's LLM call.
   - Keep `IResearcherAgent`/`IWriterAgent`/`ICriticAgent` in Core. The MAF types are implementation details.
3. **Workflow with the same topology (I4):**
   ```
   Research ─▶ Write(1) ─▶ Critic ─approved─▶ Output
                  ▲           │
                  └─revise────┘   (Write(n) with n = 3 routes directly to Output, skipping the critic)
   ```
   `WorkflowBuilder` with executors per step. The conditional edge on `CriticResult.Approved` and the iteration counter live in executor state. Run it with `InProcessExecution` and delete `ProcessResultHolder`. The Phase 0 loop tests must pass **unchanged**.
4. **Streaming: the existing contract stays exactly the same (I3).** `/ask/stream` keeps the research → prose-stream path with no critic. `WriterAgent.StreamAsync` produces prose without citations, so the critic's citation check has nothing to check, and streaming structured drafts isn't supported by the adapter.
   - *Optional, opt-in only:* a new route `/api/agent/ask/stream/reviewed` (or `?mode=reviewed`) that runs the full workflow and streams **status events during drafting and critique, then only the approved final answer's tokens**. It adds new event types (`draft`, `critique`) that only exist on that route. The approved final answer is the one persisted, and `done.grounded` there means "critic approved and sources > 0". Existing clients never see new events.
5. **MAF event names (1.0):** consume `AgentResponseUpdateEvent` / executor events. Keep the MAF event → Core `AgentStreamEvent` mapping in one adapter class with unit tests.
6. **Conversation state:** keep passing `IConversationStore` history in as messages for each run. Don't adopt `AgentSession` persistence, because the API owns history (see Ownership rule).
7. Middleware: agent-run middleware for span tags and `rag.*` attributes only. **No guardrails in middleware.**
8. Remove all `Microsoft.SemanticKernel*` packages and `Process/`. Change the OTel sources to `"Microsoft.Extensions.AI"`, `"Microsoft.Agents.AI*"` and `AgentActivitySource.Name`.
9. Update `CLAUDE.md`, `RagAgent.Core/CLAUDE.md`, `README.md` and `Best next integrations.md` so they refer to MAF.

**Done when:** there are zero SK references, the Phase 0 loop tests and invariant tests pass unchanged, new tests cover the `topK` clamp, source merging and the no-tool-call fallback, and the eval is within the baseline CI.
**Rollback:** revert the commit. There's no infrastructure or data change. Keep the Phase 2 commit as a known-good point.

---

## Phase 4: Managed safety: Amazon Bedrock Guardrails *(~2–3 days)*

**Goal:** add a managed guardrail **at the request boundary only**, first in shadow mode, and keep I1, I2 and I10.

1. **Where it runs** (and nowhere else):
   - **Input:** `IGuardrailsService.ValidateQuestion(question)`. Only the user's question is checked, before anything is stored (I2).
   - **Output:** a new `IGuardrailsService.ValidateAnswerAsync(answer, sources)`, called in `AgentOrchestrationService` after `SanitiseAnswer`, on the **final answer text only**, with the sources passed as the grounding source for the contextual grounding check.
   - That's **2 `ApplyGuardrail` calls per `/ask`**, not one per LLM call. The critic's JSON and intermediate drafts are never checked.
   - For **streaming**, tokens have already gone to the client, so the output check can't redact or block them. On `/ask/stream`, the output check runs on the buffered final text for **logging and metrics only**, and the persisted assistant message is the anonymised version.
2. **Implementation:** `BedrockGuardrailsService : IGuardrailsService` in `RagAgent.Bedrock`. Violations map to the existing `GuardrailException`, so the 400 and SSE `error` frame behaviour stays the same (I1).
3. **Policy configuration** (Terraform `aws_bedrock_guardrail` + `aws_bedrock_guardrail_version`):
   - Prompt-attack filter.
   - Sensitive-info filters (EMAIL, PHONE, CREDIT_DEBIT_CARD_NUMBER): BLOCK on input, ANONYMIZE on output.
   - **Topic parity, not a broader ML topic filter:** start with the current exact phrases as word filters ("legal advice", "financial advice", "stock tips", …). Don't enable broad denied-topic definitions like "financial advice" at first, because they'd block legitimate HN questions about startups, funding, crypto or fintech. Any topic policy has to be proven on the eval question set with a measured false-positive rate first.
   - Contextual grounding check (output only).
4. **Shadow mode first:** with `Guardrails__Mode = Shadow`, the regex provider keeps enforcing and Bedrock runs alongside it, logging decisions and emitting `guardrail.shadow.{agree,disagree}` metrics. After at least one week of production traffic (or an agreed sample) with the false-positive rate below the agreed threshold, switch to `Guardrails__Mode = Enforce`.
5. **Failure behaviour:** if `ApplyGuardrail` fails (throttling, 5xx, timeout > 2 s):
   - **Input fails closed to the regex provider.** Regex checks still run, so protection never drops below today's.
   - **Output fails open** with a warning log and a metric, because the answer is already sanitised by `SanitiseAnswer`.
6. **Tests:** provider-agnostic tests assert that a `GuardrailException` is thrown and its **category** (`Injection`, `Pii`, `Topic`), not the message text. The regex-specific message assertions in `GuardrailTests` stay as regex-provider tests.
7. IAM: `bedrock:ApplyGuardrail` on the task role.

**Done when:** shadow metrics are reviewed, the invariant tests pass with both providers, and the grounding score is recorded in the eval report.
**Rollback:** `Guardrails__Provider=Regex` (re-register the task definition). The Guardrail resource can stay. No data is left behind.

---

## Phase 5: Durable conversations: AgentCore Memory *(~3 days)*

**Goal:** history is shared across tasks and survives restarts, **with the same retention, cap, ID and privacy semantics as today** (I8, I9).

1. **Agree the Core contract change first** (separate commit): move `Subscribe` out of `IConversationStore` into `IConversationEventStream`. Only `InMemoryConversationStore` implements it, and only tests use it.
2. Terraform: `aws_bedrockagentcore_memory` with the **service-minimum event expiry** (it's set in days; check the current minimum in the API reference) and **no long-term strategies**. Summary/semantic/user-preference strategies are **explicitly deferred to Phase 7**. With no real identity, every request would share one actor, and long-term memory would leak one user's extracted facts into another user's answers.
3. `AgentCoreMemoryConversationStore : IConversationStore` (in `RagAgent.AgentCore`):
   - **IDs (I9):** `sessionId = hex(SHA-256(conversationId))`. That's 64 characters matching `[a-zA-Z0-9][a-zA-Z0-9-_]*`, and it also meets AgentCore Runtime's ≥33-character `runtimeSessionId` rule for Phase 6. Store the original `conversationId` in event metadata so `ListConversationIdsAsync` can return it. Clients keep sending any free-text ID.
   - **Retention (I8):** apply today's **30-minute sliding window in the application layer**. `GetHistoryAsync` and `ListConversationIdsAsync` ignore sessions whose last event is older than 30 minutes. The service expiry (days) is only a storage backstop. Changing retention needs an explicit product/privacy sign-off and is out of scope here.
   - **Cap (I8):** page `ListEvents` explicitly (max 100 per page), sort by event timestamp, and return the last 40 messages. Don't rely on the API's default page size or ordering.
   - **Delete:** page `ListEvents` → `DeleteEvent` for each event. There's no delete-session API. With no long-term strategies, there are no memory records to purge.
   - **Actor:** fixed `actorId = "anonymous"` until Phase 7.
4. **Exposure:** `GET /api/agent/conversations` already lists every conversation without auth. That's unchanged in scope, but it now spans tasks. Either keep the 30-minute window (above) so exposure matches today, or gate the list endpoint behind `Conversations__ListEnabled` (default `false` in production) until Phase 7.
5. Map AgentCore `ValidationException`/`ResourceNotFoundException` to 400/404, never 500 (I10).
6. Config: `ConversationStore__Provider = InMemory | AgentCore`.

**Done when:**
- A conversation survives an ECS redeploy.
- The existing store tests (40-message cap, TTL, list, delete) pass against a faked `IAmazonBedrockAgentCore`.
- A client ID like `conv-xyz` and one with spaces and colons both work.

**Rollback:** `ConversationStore__Provider=InMemory`. Conversations stored in Memory are left behind and expire at the service expiry. The Memory resource can stay.

---

## Phase 6: Agent hosting: AgentCore Runtime *(~4–5 days)*

**Goal:** run the agent workflow on AgentCore Runtime as **stateless compute**. The API keeps the contract, guardrails and history (see Ownership rule).

1. **Core contract change** (own commit): `IAgentAnswerService.AnswerAsync(AgentAnswerRequest request)` and `StreamAsync(AgentAnswerRequest, ct) → IAsyncEnumerable<AgentStreamEvent>`, where `AgentAnswerRequest(Question, TopK, History, SessionId)`.
   - `SessionId` = the hashed conversation ID from Phase 5.
   - `EvaluationAgent` passes a new GUID-based session per question, padded or hashed to ≥33 characters.
   - `AgentStreamingService` moves from calling the researcher/writer directly to calling `StreamAsync`. The in-process implementation keeps today's research → prose stream (I3).
2. **`RagAgent.AgentHost`:** minimal ASP.NET Core app, port 8080, `linux/arm64`.
   - `GET /ping` → `{"status":"Healthy"}`.
   - `POST /invocations` → takes `{ question, topK, history }`. It returns an `AgentAnswerResult` as JSON (batch), or `AgentStreamEvent`s as SSE when `stream=true`.
   - **It does not touch conversation storage or guardrails.** It reuses `AddVectorSearch` and the vector store providers.
   - Add a contract integration test with `WebApplicationFactory<AgentHost>`.
3. **`AgentCoreRuntimeAnswerService : IAgentAnswerService`** (in `RagAgent.AgentCore`) calls `InvokeAgentRuntime` with `runtimeSessionId = request.SessionId` and propagates W3C `traceparent` so the traces join (see Phase 8). The API maps `AgentStreamEvent` → `StreamEventDto` exactly as it does today. Timeouts:
   - SDK client timeout of 120 s.
   - A timeout returns the existing error behaviour (500 on `/ask`, `error` frame on `/stream`), which matches what a Bedrock timeout does today.
4. **Rollout switch:** `Agent__Host = InProcess | AgentCore` is an **ECS task-definition env var** injected by `deploy-ecs.sh`. Keep the in-process path built, deployed and tested until the runtime path has been stable in production for 2+ weeks.
5. **Terraform, in order:**
   1. Add a second ECR repo for the agent host. Extend `bootstrap-ecr-image.sh` to push an **ARM64 bootstrap image** before the runtime is created, because the first apply fails without one.
   2. Create `aws_bedrockagentcore_agent_runtime` + `_endpoint` with `lifecycle { ignore_changes = [agent_runtime_artifact] }`, mirroring the ECS task-definition pattern. That way the CI `infrastructure` job (terraform apply on every push) never rolls the image back, and the `deploy` job owns the image through `UpdateAgentRuntime`.
   3. Runtime execution role: `bedrock:InvokeModel`, `bedrock:InvokeModelWithResponseStream`, `s3vectors:QueryVectors`/`GetVectors`, CloudWatch/X-Ray.
   4. ECS task role: add `bedrock-agentcore:InvokeAgentRuntime`. **Keep** its Bedrock and S3 Vectors permissions, because search and indexing still use them.
   5. `infra/deploy-role-policy.json`: add the `bedrock-agentcore:*` control-plane actions needed.
   6. ALB: set `idle_timeout = 180` on `aws_lb.api`. With the default 60 s, a batch `/ask` (a model-driven researcher call, a network hop, up to 3 writes and 2 critic calls) can end in a 504. Check p99 against the Phase 0 baseline.
6. **CI:** add a native ARM (or cross-compiled, see Phase 1) build job for `RagAgent.AgentHost`, push, then `UpdateAgentRuntime` after the API deploy.

**Done when:**
- Production `/ask` and `/ask/stream` go through the runtime with p95 inside the budget and no 504s in a load test at expected concurrency.
- The invariant tests pass against both `Agent__Host` values.
- Integration tests still run in-process.

**Rollback:** set `Agent__Host=InProcess` and re-register the task definition, which takes minutes and needs no rebuild. The runtime resource can stay. No data is left behind, because the runtime is stateless.

---

## Phase 7: Tools, identity and governance: AgentCore Gateway, Identity, Policy *(optional, ~3–5 days)*

Do this once there's a second tool consumer (the ingestion or evaluation agents on the roadmap). Until then, in-process `AIFunction` tools are simpler.

1. **Identity first:** add inbound JWT auth (Cognito or the corporate IdP) on the API. This is a **breaking contract change** for anonymous clients, so version or announce it. It gives each conversation a real `actorId`, which scopes `ListConversationIds` per user.
2. **Only then**, optionally enable Memory long-term strategies (summary, semantic), scoped per actor. `DeleteAsync` then also has to purge memory records (`BatchDeleteMemoryRecords`).
3. **Gateway:** expose `search_posts` as an MCP tool (`aws_bedrockagentcore_gateway` + target: a Lambda, or an OpenAPI target → ECS `/api/search`). The agent consumes it via the MCP C# SDK (`McpClientTool` is an `AIFunction`). Keep the I7 `topK` clamp in the tool.
4. **Policy:** rules on the Gateway, e.g. `topK ≤ 10`, and "the ingestion agent may call index tools, the Q&A agent may not".

**Rollback:** each piece has its own flag. Identity is a breaking change and should ship with a deprecation window.

---

## Phase 8: Observability: AgentCore Observability *(~1 day, alongside Phases 3–6)*

1. MEAI/MAF `UseOpenTelemetry` emits GenAI semantic-convention spans (`gen_ai.*`). Keep sensitive-data capture off in production.
2. **ECS:** the ADOT sidecar is added by `scripts/deploy-ecs.sh`. Update `infra/otel-collector-config.yaml` there to export to CloudWatch with Transaction Search enabled.
3. **AgentCore Runtime:** there's no sidecar. It uses the runtime's built-in observability (ADOT auto-instrumentation / OTLP to CloudWatch). **Trace continuity:** the API propagates `traceparent` on `InvokeAgentRuntime` (Phase 6), so one request is a single trace across ECS → runtime → Bedrock.
4. Metrics: tokens per request, critic revisions, guardrail blocks, shadow agreement, grounding score, runtime invoke latency.
5. Keep Jaeger in `docker-compose.yml` for local dev.

---

## Phase 9: Evaluation: M.E.AI.Evaluation in CI + AgentCore Evaluations online *(~2 days)*

1. **CI gate:** run the Phase 0 frozen-corpus eval (5 runs, cached judge responses) nightly and on PRs that touch `RagAgent.Agents/**`. It fails when a metric's mean falls below the baseline CI's lower bound, and it publishes the M.E.AI.Evaluation HTML report as an artefact.
2. **Online:** AgentCore Evaluations on sampled production traces (built-in evaluators such as correctness, faithfulness, tool-selection accuracy and harmfulness), with CloudWatch alarms on drift.
3. This finishes the "Evaluation agent" roadmap item in `CLAUDE.md`.

---

## Phase 10: Optional extras

| Item | Recommendation |
|---|---|
| **.NET Aspire** AppHost instead of `docker-compose.yml` for local dev | Recommended: cheap, and a large improvement to local dev. |
| **`Microsoft.Extensions.VectorData`** instead of our `IVectorStore` for Qdrant/Redis | Spike it. There's no S3 Vectors connector, so we'd write one. Only adopt it if the code gets smaller. |
| **Bedrock Knowledge Bases** (managed ingestion/retrieval, which can use S3 Vectors) | **Not recommended.** It removes the part of the repo we learn most from and hides retrieval tuning. |
| **A2A protocol** | Only if we need to talk to other teams' agents. |
| **Ingestion agent** on EventBridge Scheduler → Lambda/AgentCore | After Phase 7, so it can reuse the Gateway tools. |

---

## Sequencing and risk

```
P0 ─▶ P1 ─▶ P2 ─▶ P3 ─┬─▶ P4 (shadow ▶ enforce) ─┐
                      ├─▶ P5 ─────────────────────┼─▶ P6 ─▶ P7 (optional)
                      └─▶ P8 ─────────────────────┘        P9 (CI gate from P3 onwards)
```

| Risk | Mitigation |
|---|---|
| The eval signal is too noisy to catch regressions | Frozen corpus, 40–60 questions, 5 runs at temperature 0, a CI-based gate and an independent judge (Phase 0) |
| The Bedrock MEAI `ResponseFormat` throws with tools or streaming (aws-sdk-net#4113) | Off by default. The optional spike covers tool-free, non-streaming calls only, with a unit test guarding it (Phase 2). |
| Guardrails fire on sources or IDs, or block legitimate questions | Guardrails only run at the request boundary on the question and final answer, with shadow mode, topic parity and a measured false-positive rate (Phases 2 and 4) |
| Guardrail service outage | Input fails closed to regex, output fails open with a metric (Phase 4) |
| SSE contract breaks for existing clients | The existing route is unchanged. Reviewed streaming is opt-in on a new route (Phase 3). |
| Client conversation IDs rejected by Memory or Runtime | SHA-256 session ID mapping (Phase 5) |
| Cross-user leakage or longer retention via Memory | No long-term strategies before identity, a 30-minute app-level window, and the list endpoint gated (Phase 5) |
| History written twice / guardrails run twice | Ownership rule: the API owns history and guardrails, and the runtime is stateless (Phase 6) |
| Terraform rolls the runtime image back on every push | `ignore_changes` on the runtime artefact, and CI owns the image (Phase 6) |
| ALB 504 on longer runs | `idle_timeout = 180` and a load test (Phase 6) |
| Docker restore breaks under CPM | Update the Dockerfile restore layer and add a `--no-cache` CI check (Phase 1) |
| `exec format error` after the Graviton switch | Verify multi-arch manifests (including bootstrap) before the switch, in a separate commit (Phase 1) |
| MAF event types change between minor versions | Pin via CPM, and keep a single adapter class with tests |
| AgentCore cost (runtime sessions, Memory events, Evaluations) | Service-minimum expiry, sampled online evaluations, AWS Budgets alerts in Terraform |

## Per-phase checklist (from `CLAUDE.md`)

- [ ] `dotnet format RagAgent.sln --severity warn`
- [ ] `dotnet test RagAgent.UnitTests` and `dotnet test RagAgent.IntegrationTests` are green, including the invariant tests (I1–I10)
- [ ] For Terraform changes, `terraform fmt -recursive` and `terraform validate` via Docker
- [ ] The eval is within the baseline confidence interval, and p95 is within budget
- [ ] Any contract change (OpenAPI path, Core interface, auth) is called out in the PR description
- [ ] The rollback line has been tested (flag flip) where the phase has one
- [ ] Conventional commits, one phase or sub-step per commit, never pushed without being asked

## References

- [Microsoft Agent Framework 1.0](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/) · [Overview](https://learn.microsoft.com/en-us/agent-framework/overview/) · [Workflow events](https://learn.microsoft.com/en-us/agent-framework/workflows/events) · [Amazon Bedrock provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/amazon-bedrock)
- [AWSSDK.Extensions.Bedrock.MEAI](https://www.nuget.org/packages/AWSSDK.Extensions.Bedrock.MEAI/) · [aws-sdk-net#4113 (ResponseFormat via a forced tool)](https://github.com/aws/aws-sdk-net/pull/4113)
- [AgentCore Runtime Terraform resource](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/bedrockagentcore_agent_runtime) · [aws-ia/agentcore module](https://registry.terraform.io/modules/aws-ia/agentcore/aws/latest)
- [AgentCore Memory: create a memory](https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/memory-create-a-memory-store.html) · [CreateMemory API](https://docs.aws.amazon.com/bedrock-agentcore-control/latest/APIReference/API_CreateMemory.html) · [IAmazonBedrockAgentCore (.NET)](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/BedrockAgentCore/TIBedrockAgentCore.html)
- [AgentCore Evaluations and Policy](https://aws.amazon.com/blogs/aws/amazon-bedrock-agentcore-adds-quality-evaluations-and-policy-controls-for-deploying-trusted-ai-agents/)
- [Cohere Embed v3/v4 on Bedrock](https://docs.aws.amazon.com/bedrock/latest/userguide/model-parameters-embed.html)
