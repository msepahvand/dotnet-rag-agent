# Modernisation Plan: .NET 10, Microsoft.Extensions.AI, Agent Framework and Bedrock AgentCore

**Status:** Phases 0–3 complete; Phase 4 implementation is present and awaiting live shadow review; Phase 5 implementation is in progress; Phases 6, 8 and 9 remain planned; Phase 7 and Phase 10 are optional · **Updated:** 2026-10-03

### Authoritative phase status

| Phase | Status | Repository implementation | Remaining work |
|---|---|---|---|
| 0 — Baseline and safety net | **Complete** | Merged in PR #7; snapshot tooling, evaluation metrics, characterisation tests and dead-code removal are in place. | Run the five-pass live Bedrock baseline and verify production IAM when AWS access is available. |
| 1 — Platform refresh: .NET 10 LTS | **Complete** | Merged in PR #7; .NET 10, central package management, OpenAPI/Scalar and multi-architecture Docker/CI changes are in place. | Verify the deployed ECS service, API/OpenAPI endpoints and remote ECR manifests in the target environment. |
| 2 — Model access on Microsoft.Extensions.AI | **Complete** | MEAI chat and embedding access, with embedding telemetry, merged and validated in PR #24. | None. |
| 3 — Orchestration on Microsoft Agent Framework | **Complete** | MAF workflow and `ChatClientAgent`s merged in PR #24; CI Build & Test passed. | `/ask/stream` intentionally remains a separate research-to-prose path without the critic. |
| 4 — Managed Bedrock Guardrails | **Implemented; live verification pending** | Regex + Bedrock dual-run, shadow metrics, Terraform guardrail and IAM are present. | Review production shadow agreement/disagreement and output grounding results; decide whether to enable enforcement. No AWS evidence is recorded yet. |
| 5 — AgentCore Memory | **In progress** | Provider implementation, per-conversation operation locking, 15-minute expired-session cleanup, Terraform resource/IAM wiring, list-endpoint gate and unit coverage are present. | Re-run integration tests and Terraform fmt/validate when Docker is available, deploy the memory resource, then verify persistence, expiry, deletion and listing behaviour in AWS. |
| 6 — AgentCore Runtime | **Planned** | Not implemented. | Build the stateless host, runtime client, deployment pipeline and production load/rollback checks. |
| 7 — Gateway, identity and policy | **Optional / deferred** | Not implemented. | Only start when a second tool consumer justifies it; identity is a separately announced breaking API change. |
| 8 — AgentCore Observability | **Planned; partly overlaps current OTel** | Base OTel and ECS ADOT are present; AgentCore trace continuity is not. | Add runtime trace propagation and CloudWatch metrics/alarms alongside Phase 6. |
| 9 — Evaluation automation | **Planned; evaluation building blocks exist** | Snapshot evaluation and optional quality judges are present. | Add the CI regression gate and sampled online AgentCore evaluations. |
| 10 — Optional extras | **Deferred** | Not implemented. | Aspire/vector-data/A2A/ingestion-agent work is not required to finish the core migration. |

**Status convention:** “Implemented” describes repository code and tests; production rollout and AWS-only acceptance are tracked separately and are not claimed complete without evidence. The table is the quick status summary; the detailed phase sections below define acceptance and rollback.

### What remains to finish the core modernisation

1. Complete Phase 5 integration/Terraform validation (currently blocked because Docker is unavailable), deploy it, then verify durable conversation history with the 30-minute application expiry and the 40-message cap.
2. Review Phase 4's live shadow metrics and grounding evaluation; only then decide whether managed output checks should be enforced.
3. Implement and roll out Phase 6 AgentCore Runtime, with Phase 8 trace continuity and Phase 9 evaluation gates completed alongside it.
4. Leave Phase 7 and Phase 10 deferred unless a product requirement makes them necessary.

## Decisions and intended direction

MEAI model access and MAF orchestration are implemented. AgentCore Memory is being added as an optional conversation-store provider; AgentCore Runtime remains a later phase.

| Question | Answer | Why it's worth it here |
|---|---|---|
| Replace Semantic Kernel with **Microsoft.Extensions.AI (MEAI)**? | **Implemented.** Bedrock chat and Cohere embeddings use MEAI abstractions. | MEAI provides provider-oriented `IChatClient` and `IEmbeddingGenerator` APIs; see Phase 2 for migration rationale. |
| Replace SK Agents/Process with **Microsoft Agent Framework (MAF)**? | **Implemented.** Researcher, writer and critic use `ChatClientAgent`; batch orchestration uses a typed MAF Workflow. | Research uses MEAI `AIFunction` function invocation; the workflow preserves the bounded critic/revision loop. |
| Use **Amazon Bedrock AgentCore**? | **Memory implementation in progress; Runtime planned.** | Memory makes API conversation state durable while preserving the current API contract. Runtime hosting, Gateway/identity, observability and online evaluation remain separate phases. |

**Longer-term target:** a thin ECS API that owns the HTTP contract, guardrails and conversation history, calling a stateless MAF Workflow hosted on AgentCore Runtime. Models use MEAI on Bedrock. AgentCore Memory is the planned durable store; Runtime, Observability and Evaluations remain follow-on work.

The rules in `CLAUDE.md` still apply: **Core stays provider-agnostic**, controllers stay thin, and each phase is committed on its own with all tests passing.

---

## Current implementation after Phases 0–3

| Concern | Current implementation | File(s) |
|---|---|---|
| Runtime | .NET 10 with SDK pinned in `global.json`, central package management and multi-architecture .NET 10 container builds. | `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `RagAgent.Api/Dockerfile` |
| Chat model | Bedrock Converse exposed through MEAI `IChatClient`; researcher, writer and critic are MAF `ChatClientAgent`s. | `RagAgent.Agents/ServiceCollectionExtensions.cs`, `ResearcherAgent.cs`, `WriterAgent.cs`, `CriticAgent.cs` |
| Embeddings | MEAI `IEmbeddingGenerator` calls Cohere Embed v3 through `InvokeModel` (1024 dimensions), with OpenTelemetry instrumentation. | `CohereEmbeddingGenerator.cs`, `EmbeddingService.cs`, `ServiceCollectionExtensions.cs` |
| Tools | `search_posts` is an MEAI `AIFunction` offered to the researcher; results are bounded, merged by post ID, and direct search is the no-tool fallback. | `ResearcherAgent.cs`, `SemanticSearchPlugin.cs` |
| Orchestration | Typed MAF Workflow: Research → Write → Critic → revise; the third draft routes directly to output. | `Workflow/AgentAnswerWorkflowService.cs`, `Workflow/*Executor.cs` |
| Streaming | Separate research → prose-stream path without critic or structured citations. Guardrail violations yield one SSE `error` event; `done.grounded` reflects whether sources were retrieved. | `RagAgent.Api/Services/AgentStreamingService.cs` |
| Guardrails | `GuardrailsService` validates questions at the request boundary. Batch answers are sanitised for invalid citations and length; streaming answers are length-limited. No SK filter pipeline remains. | `RagAgent.Agents/GuardrailsService.cs`, `RagAgent.Api/Services/AgentOrchestrationService.cs`, `AgentStreamingService.cs` |
| Conversation state | `InMemoryConversationStore` remains the default/local provider. The opt-in AgentCore Memory provider is being implemented to preserve 30-minute expiry, 40-message history and free-text client IDs. | `RagAgent.InMemory/InMemoryConversationStore.cs`, `RagAgent.AgentCore/AgentCoreMemoryConversationStore.cs` |
| Observability | OpenTelemetry subscribes to ASP.NET Core, HTTP client, MEAI, MAF and application agent sources. Agent/workflow/model instrumentation disables sensitive payload capture; ECS ADOT forwards traces to X-Ray. | `RagAgent.Api/Program.cs`, `RagAgent.Agents/Workflow/AgentAnswerWorkflowService.cs`, `infra/otel-collector-config.yaml` |
| Evaluation | Hit@k, citation validity, writer-reported groundedness, average/P50/P95 latency, and optional independent groundedness/relevance judges. Snapshot-backed evaluation is available. | `RagAgent.Agents/EvaluationAgent.cs`, `RagAgent.Core/Models/EvaluationReport.cs`, `RagAgent.HackerNews/SnapshotPostService.cs` |
| Hosting | ECS Fargate behind an ALB, provisioned with Terraform. PRs run build, format, unit and integration checks; pushes to `main`/`master` deploy only for non-Markdown changes. | `infra/main.tf`, `.github/workflows/ci-cd.yml` |
| IAM | ECS task role grants Bedrock model/guardrail access; AgentCore Memory access is added in Phase 5. | `infra/main.tf` |
| Tests | Unit tests cover workflow routing and researcher tool behaviour. Integration tests include the real `IAgentAnswerService` with a scripted chat client and Testcontainers vector providers. | `RagAgent.UnitTests/*`, `RagAgent.IntegrationTests/VectorSearchWebApplicationFactory.cs` |

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

## Long-term target architecture (future phases)

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
| `RagAgent.Core` | Adds `AgentAnswerRequest` and `AgentStreamEvent` in Phase 6, keeps `ConversationMessage`, and splits `Subscribe` out of `IConversationStore` into `IConversationEventStream` in Phase 5. No AWS/MAF/SK references. |
| `RagAgent.Agents` | SK removed. MEAI + MAF: agents, `AIFunction` tools, workflow. Provider-neutral and unit-tested against `FakeChatClient`. |
| `RagAgent.Bedrock` *(new)* | Bedrock wiring: `IChatClient`/`IEmbeddingGenerator` registration, `BedrockGuardrailsService : IGuardrailsService`. |
| `RagAgent.AgentCore` | `AgentCoreMemoryConversationStore : IConversationStore` in Phase 5; runtime client remains Phase 6 work. |
| `RagAgent.AgentHost` *(new)* | Minimal ASP.NET Core host for AgentCore Runtime (`/invocations`, `/ping`, port 8080, `linux/arm64`). |
| `RagAgent.Api` | Agent endpoints unchanged. Picks in-process or runtime `IAgentAnswerService` via `Agent__Host`. |
| `RagAgent.InMemory`, `RagAgent.Qdrant`, `RagAgent.Redis` | Kept for local dev / integration tests. |
| Tests | `RagAgent.UnitTests` tests provider behaviour through narrow client adapters, with no live AWS credentials. Integration tests add **one real-pipeline test** (real `IAgentAnswerService` + `FakeChatClient`) and one AgentHost `/invocations` contract test in Phase 6. |

---

## Guiding principles

1. **Strangler, not big-bang.** Each phase ships behind the existing Core interfaces, and the invariants above are the regression criteria.
2. **Measure with a frozen corpus** (Phase 0), not live HN data, and gate on confidence intervals, not a flat ±5%.
3. **Local first.** Every managed capability has a local implementation, so the Testcontainers suite stays credential-free.
4. **Runtime-switchable.** Switches are **environment variables set in the ECS task definition** (`deploy-ecs.sh` already injects env vars), not values baked into `appsettings.json`. Rolling back means registering a task definition again, not rebuilding the image: `Agent__Host`, `ConversationStore__Provider`, `Guardrails__Provider`, `Guardrails__Mode`.
5. **Every phase has a rollback line.** It says which flag to flip, which Terraform resources can safely stay, and what data is left behind.

---

## Phase 0: Baseline and safety net — **COMPLETE**

**Goal:** a regression signal we can trust, built on Core interfaces so it survives the SK → MAF swap.

1. [x] **Frozen evaluation corpus.**
   - [x] `scripts/snapshot-hn.sh` captures the story snapshot and creates 50 title-grounded questions.
   - [x] `SnapshotPostService : IPostService` and the `DataSource:Provider=HackerNews|Snapshot` registration are implemented.
   - [x] Evaluation supports an isolated snapshot-backed app, a dedicated vector collection, and disabled periodic ingestion. The corpus and question files are git-ignored to avoid committing third-party story content.
2. [x] **Better metrics:**
   - [x] Add `P50LatencyMs`/`P95LatencyMs` to `EvaluationReport`.
   - [x] Add independent groundedness and relevance judges using `Microsoft.Extensions.AI.Evaluation.Quality`, with a 30-day in-memory response cache. Judges are opt-in to avoid extra Bedrock calls in normal API evaluation.
   - [x] Add per-question and aggregate judge scores. Treat Claude-as-judge scores as relative comparisons, not absolute quality ratings.
3. [x] **Deterministic runs.** `Agent__Temperature` is supported for writer and critic calls and remains unset by default. The evaluation setup sets it to `0`.
4. [x] **Characterisation tests against Core interfaces** (so they run unchanged against the MAF workflow in Phase 3):
   - `IAgentAnswerService` loop tests with stub `IResearcherAgent`/`IWriterAgent`/`ICriticAgent`:
     - approve first time
     - revise then approve
     - three rejections, where the 3rd draft skips the critic (I4)
     - `Iterations` is reported correctly
   - SSE **error path** test: a guardrail violation gives exactly one `error` frame, and nothing is appended to the store (I1, I2).
   - Keep the existing tests. They already cover SSE order, critic parse fallback, citation stripping, writer raw-output fallback (I5) and the history cap (I8).
5. [x] **One real-pipeline integration test.** The integration-test host keeps the real `IAgentAnswerService` and replaces only `IChatClient`; it exercises the HTTP ask path with deterministic model responses.
6. [x] **Remove dead code** (no behaviour change, because none of it runs today):
   - [x] `GroundedAnswer.yaml` is absent.
   - [x] Remove `IndexingPlugin` and its tests; indexing is covered at the service boundary.
   - [x] No direct Handlebars or SK YAML package references remain. Review any remaining transitive YAML dependency against its current parent package before removing it.
7. [x] **Check the streaming IAM action in Terraform:** `bedrock:InvokeModelWithResponseStream` is present in the task-role policy.

**Completion evidence:** Phase 0 implementation and local validation are complete and merged in PR #7. The local snapshot contains 199 stories and 50 questions, and remains ignored. Unit and integration tests passed.
**Operational follow-up:** the five-pass live Bedrock baseline and production IAM verification have not been run because AWS credentials and the AWS CLI were unavailable in the implementation environment. Run these when AWS access is available; this does not reopen the completed implementation phase.
**Rollback:** nothing to roll back. These are tests, eval assets and dead-code removal.

---

## Phase 1: Platform refresh: .NET 10 LTS — **COMPLETE**

**Goal:** move to the current LTS runtime without changing behaviour.

1. [x] Add a `global.json` pinning the .NET 10 SDK. Set `net10.0` everywhere. Move the shared `TargetFramework`/`Nullable`/`ImplicitUsings`/`LangVersion` into the **existing** `Directory.Build.props`, and update CI to install .NET 10.
2. [x] **Central Package Management** (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`):
   - Pin exact versions in place of `AWSSDK.* 4.0.*`.
   - Remove the StyleCop `PackageReference` from `Directory.Build.props` and declare it as a `<GlobalPackageReference>` **in `Directory.Packages.props`**, next to the `PackageVersion` entries. An inline `Version` would trigger NU1008 under CPM.
   - [x] Add Dependabot for NuGet, GitHub Actions and Terraform.
3. [x] **Dockerfile restore layer:** copy `global.json`, `NuGet.config`, shared build settings, and every referenced project file before `dotnet restore`. Exclude host `bin`/`obj` outputs from the Docker context. A clean multi-architecture build has passed locally.
4. [x] Package bumps: `Microsoft.Extensions.*` 10.x, `Mvc.Testing` 10.x, OpenTelemetry current, test SDK.
5. [x] Replace **Swashbuckle** with built-in OpenAPI and Scalar, retaining `Swagger:Enabled`; update the Postman collection to `/openapi/v1.json`.
6. [x] Use `aspnet:10.0` / `sdk:10.0` base images. The `-noble-chiseled` variant remains optional.
7. [x] **ARM64 readiness, in this order:**
   1. [x] Build the image **multi-arch without QEMU**, following [Microsoft's multi-platform container guidance](https://devblogs.microsoft.com/dotnet/improving-multiplatform-container-support/):
      - The build stage uses `FROM --platform=$BUILDPLATFORM sdk:10.0`.
      - The restore layer runs `dotnet restore -a $TARGETARCH`, and the publish step runs `dotnet publish -a $TARGETARCH --no-restore`. Without the RID-specific restore, the build fails with NETSDK1047.
      - The final stage uses the **target-platform** `aspnet:10.0` image, without `--platform=$BUILDPLATFORM`.
      - Then build with `docker buildx build --platform linux/amd64,linux/arm64`. Alternatively, use a native `ubuntu-24.04-arm` runner. Update `scripts/bootstrap-ecr-image.sh` to push a multi-arch bootstrap image too.
   2. [x] Add CI manifest checks for both architectures on deployment and bootstrap images. Remote ECR tags remain unverified until deployment.
   3. *Optional, separate commit:* switch ECS to Graviton (`runtime_platform { cpu_architecture = "ARM64" }`).
8. [x] CI: `setup-dotnet` → `10.0.x`.

**Completion evidence:** Phase 1 implementation and local validation are complete and merged in PR #7. The clean local `linux/amd64` + `linux/arm64` OCI build passed, OpenAPI/Scalar and API integration tests passed, and the full unit/integration suites are green.
**Operational follow-up:** verify remote ECR manifests, the deployed ECS service, and deployed API/OpenAPI endpoints in the target environment. The Phase 0 live baseline is also tracked as operational follow-up. These environment-dependent checks do not reopen the completed implementation phase.
**Rollback:** revert the commit. The ECS task definition still points at the previous image. The Graviton switch is its own revertible commit.

---

## Phase 2: Model access on Microsoft.Extensions.AI *(~2–3 days)*

**Goal:** remove `IChatCompletionService`/`Kernel`/`AmazonClaudeExecutionSettings` from the agents, **keeping I1–I10**. Orchestration is subsequently migrated in Phase 3.

1. [x] **Rename Core `ChatMessage` → `ConversationMessage`** to remove the naming clash with MEAI's `ChatMessage`.
2. [x] Add `AWSSDK.Extensions.Bedrock.MEAI` + `Microsoft.Extensions.AI`, and register one pipeline:
   ```csharp
   services.AddAWSService<IAmazonBedrockRuntime>();
   services.AddChatClient(sp => sp.GetRequiredService<IAmazonBedrockRuntime>().AsIChatClient(options.ChatModelId))
       .UseLogging()
       .UseOpenTelemetry(sourceName: AgentActivitySource.Name, configure: c => c.EnableSensitiveData = false)
       .UseFunctionInvocation();
   ```
   **No guardrail middleware in this pipeline.** Guardrails stay at the request boundary (see Phase 4). The writer and critic send the sources JSON as user content, and the PII regex matches 8-digit HN post IDs and long floats, so a guardrail on every LLM call would reject every request (breaking I1 and I10).
   Optionally register a keyed `IChatClient` for the critic, e.g. Claude Haiku 4.5, using the inference-profile ID from the Bedrock console.
3. [x] **WriterAgent / CriticAgent:** use MEAI `ChatOptions.MaxOutputTokens` and **keep the prompt-plus-`AgentJsonHelpers.ExtractJson` approach unchanged** (I5). These clients were subsequently wrapped in MAF `ChatClientAgent`s in Phase 3.
   - **Don't set `ChatOptions.ResponseFormat` by default.** The Bedrock MEAI adapter implements it as a forced synthetic tool ([aws-sdk-net#4113](https://github.com/aws/aws-sdk-net/pull/4113)). It throws `ArgumentException` when combined with user tools and `NotSupportedException` on streaming, and either would be a 500.
   - *Optional spike:* try `ResponseFormat` **only** on the non-streaming, tool-free writer and critic calls, behind `Agent__UseSchemaOutput=true`. Add a unit test proving it's never set on the researcher (which has tools) or on any streaming call.
4. [x] **Embeddings:** keep `CohereEmbeddingGenerator` and wrap it with `EmbeddingGeneratorBuilder.UseOpenTelemetry()`. Cohere Embed v4 remains an optional, separate blue/green migration.
5. [x] **Delete the dead SK filters** (`InputGuardrailFilter`, `OutputGuardrailFilter`, `ToolInvocationFilter`); static check helpers remain in `RegexGuardrails`/`GuardrailsService`.
6. [x] `SemanticSearchPlugin`: remove `[KernelFunction]` and keep `[Description]`.
7. [x] Tests use scripted `IChatClient` fakes with deterministic responses in unit and integration tests.
8. [x] IAM includes `bedrock:InvokeModelWithResponseStream`.

**Completion evidence:** Phase 2 is complete and merged with Phase 3 in PR #24. CI's Build & Test job passed, including formatting verification, unit tests and integration tests.
**Rollback:** revert the commit. This phase has no infrastructure or data changes.

---

## Phase 3: Orchestration on Microsoft Agent Framework *(~3–5 days)*

**Goal:** replace SK Process with a MAF Workflow and remove SK packages completely, with the **same topology and the same streaming contract**.

1. [x] Add `Microsoft.Agents.AI` + `Microsoft.Agents.AI.Workflows` (1.x), pinned via CPM.
2. **Agents:**
   - [x] **Researcher** is a `ChatClientAgent` with the `search_posts` tool (`AIFunctionFactory.Create`). The model chooses the search query, which removes the SK#9750 workaround. Guard rails for the tool:
     - **I7:** the tool clamps its `topK` argument to the client's normalised `topK`, which is captured in the tool closure. The model can ask for fewer results, never more.
     - **Multiple searches:** merge results by `PostId`, keep the lowest distance, order by distance, and cap at `topK`.
     - **Fallback:** if the model makes no tool call, call search directly with the original question, so there's never an answer without retrieval.
     - **Never set `ResponseFormat`** on the researcher (see Phase 2).
   - [x] **Writer** and **critic** are `ChatClientAgent`s. The deterministic citation check stays in C# before the critic's LLM call.
   - [x] Keep `IResearcherAgent`/`IWriterAgent`/`ICriticAgent` in Core. The MAF types are implementation details.
3. **Workflow with the same topology (I4):**
   ```
   Research ─▶ Write(1) ─▶ Critic ─approved─▶ Output
                  ▲           │
                  └─revise────┘   (Write(n) with n = 3 routes directly to Output, skipping the critic)
   ```
   [x] `WorkflowBuilder` with executors per step. The conditional edge on `CriticResult.Approved` and the iteration counter live in immutable workflow state. Run it with `InProcessExecution` and remove `ProcessResultHolder`. The loop behaviour is covered by workflow unit tests.
4. [x] **Streaming: the existing contract stays exactly the same (I3).** `/ask/stream` keeps the research → prose-stream path with no critic. `WriterAgent.StreamAsync` produces prose without citations, so the critic's citation check has nothing to check, and streaming structured drafts isn't supported by the adapter.
   - *Optional, opt-in only:* a new route `/api/agent/ask/stream/reviewed` (or `?mode=reviewed`) that runs the full workflow and streams **status events during drafting and critique, then only the approved final answer's tokens**. It adds new event types (`draft`, `critique`) that only exist on that route. The approved final answer is the one persisted, and `done.grounded` there means "critic approved and sources > 0". Existing clients never see new events.
5. **MAF events:** deferred while the existing stream route remains independent of the batch workflow. No MAF event-to-Core stream adapter is needed unless the optional reviewed-stream route is implemented; the current SSE contract remains unchanged.
6. [x] **Conversation state:** keep passing `IConversationStore` history in as messages for each run. Don't adopt `AgentSession` persistence, because the API owns history (see Ownership rule).
7. [x] **Telemetry:** subscribe to MEAI, MAF and application agent sources; enable workflow telemetry with sensitive payload capture disabled. **No guardrails in middleware.**
8. [x] Remove all `Microsoft.SemanticKernel*` packages and `Process/`. Change the OTel sources to `"Microsoft.Extensions.AI"`, `"Microsoft.Agents.AI*"` and `AgentActivitySource.Name`.
9. [x] Update `CLAUDE.md`, `RagAgent.Core/CLAUDE.md`, `README.md` and `Best next integrations.md` so they refer to MAF.

**Completion evidence:** Phase 3 is complete and merged in PR #24. No Semantic Kernel packages or runtime references remain. Workflow tests cover approval, revision, critic-skip on the third draft and `topK` normalisation; researcher tests cover bounded multi-search merging and no-tool-call fallback. CI's Build & Test job passed, including formatting verification, unit tests and the real-pipeline integration test with both vector-store providers.
**Rollback:** revert the commit. There's no infrastructure or data change. Keep the Phase 2 commit as a known-good point.

---

## Phase 4: Managed safety: Amazon Bedrock Guardrails *(~2–3 days)*

**Goal:** add a managed guardrail **at the request boundary only**, first in shadow mode, and keep I1, I2 and I10.

1. **Where it runs** (and nowhere else):
   - **Input:** `IGuardrailsService.ValidateQuestion(question)`. Only the user's question is checked, before anything is stored (I2).
   - **Output:** a new `IGuardrailsService.ValidateAnswerAsync(answer, sources)`, called in `AgentOrchestrationService` after `SanitiseAnswer`, on the **final answer text only**, with the sources passed as the grounding source for the contextual grounding check.
   - That's **2 `ApplyGuardrail` calls per `/ask`**, not one per LLM call. The critic's JSON and intermediate drafts are never checked.
   - For **streaming**, tokens have already gone to the client, so the output check can't redact or block them. On `/ask/stream`, the output check runs on the buffered final text for **logging and metrics only**, and the persisted assistant message is the anonymised version.
2. **Implementation:** `BedrockGuardrailsService : IGuardrailsService` alongside the existing Bedrock runtime integration. `Guardrails__Provider` selects `Regex` or `Bedrock`; `Guardrails__Mode` selects `Shadow` or `Enforce`. The task definition starts on `Bedrock` + `Shadow`, and local runs default to `Regex`.
   - **Input** violations map to the existing `GuardrailException`, so the 400 and SSE `error` frame behaviour stays the same (I1).
   - **Output** violations (anonymise, grounding/relevance below threshold) **don't** throw. The request was a 200 before and stays a 200, and the user message is already stored. In Enforce mode `/ask` returns 200 with a safe fallback (`Grounded = false`, a fixed "I couldn't produce a well-grounded answer from the sources" message plus the sources). That fallback is what gets stored as the assistant message. Output blocking stays in Shadow mode until its false-positive rate has also been measured.
3. **Policy configuration** (Terraform `aws_bedrock_guardrail` + `aws_bedrock_guardrail_version`):
   - Prompt-attack filter.
   - Sensitive-info filters (EMAIL, PHONE, CREDIT_DEBIT_CARD_NUMBER): BLOCK on input, ANONYMIZE on output.
   - **Topic parity, not a broader ML topic filter:** start with the current exact phrases as word filters ("legal advice", "financial advice", "stock tips", …). Don't enable broad denied-topic definitions like "financial advice" at first, because they'd block legitimate HN questions about startups, funding, crypto or fintech. Any topic policy has to be proven on the eval question set with a measured false-positive rate first.
   - Contextual grounding check (output only).
4. **Shadow mode first:** with `Guardrails__Mode = Shadow`, the regex provider keeps enforcing and Bedrock runs alongside it, logging decisions and emitting `guardrail.shadow.{agree,disagree}` metrics. Contextual grounding and relevance thresholds start at 0.7. After at least one week of production traffic (or an agreed sample) with the false-positive rate below the agreed threshold, switch to `Guardrails__Mode = Enforce`.
5. **Failure behaviour:** if `ApplyGuardrail` fails (throttling, 5xx, timeout > 2 s):
   - **Input fails closed to the regex provider.** Regex checks still run, so protection never drops below today's.
   - **Output fails open** with a warning log and a metric, because the answer is already sanitised by `SanitiseAnswer`.
6. **Tests:** provider-agnostic tests assert that a `GuardrailException` is thrown and its **category** (`Injection`, `Pii`, `Topic`), not the message text. The regex-specific message assertions in `GuardrailTests` stay as regex-provider tests.
7. IAM: `bedrock:ApplyGuardrail` on the task role.

**Done when:** shadow metrics are reviewed, the invariant tests pass with both providers, and the grounding score is recorded in the eval report.
**Rollback:** `Guardrails__Provider=Regex` (re-register the task definition). The Guardrail resource can stay. No data is left behind.

---

## Phase 5: Durable conversations: AgentCore Memory *(~3 days; implementation in progress)*

**Goal:** history is shared across tasks and survives restarts, **with the same retention, cap, ID and privacy semantics as today** (I8, I9).

1. **Agree the Core contract change first** (separate commit): move `Subscribe` out of `IConversationStore` into `IConversationEventStream`. Only `InMemoryConversationStore` implements it, and only tests use it.
2. Terraform: `aws_bedrockagentcore_memory` with `event_expiry_duration = 7` days (the current AWS Terraform provider minimum) and **no long-term strategies**. The app enforces the existing 30-minute expiry and deletes expired events; the longer service TTL is only a storage backstop. Summary/semantic/user-preference strategies are **explicitly deferred to Phase 7**. With no real identity, every request would share one actor, and long-term memory would leak one user's extracted facts into another user's answers.
3. `AgentCoreMemoryConversationStore : IConversationStore` (in `RagAgent.AgentCore`):
   - **IDs (I9):** `sessionId = hex(SHA-256(conversationId))`. That's 64 characters matching `[a-zA-Z0-9][a-zA-Z0-9-_]*`, and it also meets AgentCore Runtime's ≥33-character `runtimeSessionId` rule for Phase 6.
     - Store the original `conversationId` **in the event payload** next to the conversational message, e.g. a JSON payload item containing `{"conversationId": "..."}`. **Don't put it in event metadata.** Metadata values are limited to 256 characters from `[a-zA-Z0-9\s._:/=+@-]`, so IDs containing `#`, `,` or non-ASCII characters, or longer IDs, would get a 400 where they get a 200 today.
     - Clients keep sending any free-text ID. The tests must include IDs with `#`, `,`, emoji, and one over 256 characters, as well as `conv-xyz` and IDs with spaces and colons.
   - **Retention (I8): same logical semantics as today's sliding TTL.** Today a conversation idle for more than 30 minutes is **gone completely**, and the next message starts from empty history. `GetHistoryAsync` writes a JSON-only activity event when history exists so reads renew the sliding expiry without adding a chat message; it removes the prior activity event and appends remove activity markers. `GetHistoryAsync` and `AppendAsync` first read the session's latest event timestamp. If it's older than 30 minutes, they **delete all the session's events** (paged `DeleteEvent`, as for `DeleteAsync`) before returning empty history or appending. That way expired messages can never come back into a prompt or `GET /conversations/{id}` after a new append. A background sweep lists sessions every 15 minutes and deletes expired events, including when the production list endpoint is disabled; inactive data is therefore physically purged on the next sweep, while expired conversations are immediately hidden from history and listing.
     - The service expiry is only a storage backstop. It's set in days; Terraform currently validates **7–365** days. Verify the deployed provider/API limit before changing this setting.
     - Changing retention needs an explicit product/privacy sign-off and is out of scope here.
   - **Cap (I8):** page `ListEvents` explicitly (max 100 per page), sort by event timestamp, return the last 40 messages, and delete older message events on append so active conversations do not grow without bound. Activity events are excluded from the message cap. Don't rely on the API's default page size or ordering.
   - **Delete:** page `ListEvents` → `DeleteEvent` for each event. There's no delete-session API. With no long-term strategies, there are no memory records to purge.
   - **Actor:** fixed `actorId = "anonymous"` until Phase 7.
   - **Listing cost:** `ListSessions` returns only `sessionId`/`createdAt`. `ListConversationIdsAsync` therefore needs one `ListSessions` pass plus one `ListEvents` per session to read the original ID and the last-event time. That's acceptable behind `Conversations__ListEnabled=false` (below), but don't call it on a hot path.
4. **Exposure:** `GET /api/agent/conversations` already lists every conversation without auth. With AgentCore Memory it spans tasks, so gate the list endpoint behind `Conversations__ListEnabled` (`false` in the production task definition; defaults to current behaviour otherwise) until Phase 7 identity exists.
5. Map AgentCore `ValidationException`/`ResourceNotFoundException` to 400/404, never 500 (I10).
6. Config: `ConversationStore__Provider = InMemory | AgentCore`.

**Done when:**
- Repository validation passes: format, unit and integration tests, including fake-client coverage for the 40-message cap, expiry, listing, deletion, paging and arbitrary IDs.
- After deployment, a conversation survives an ECS redeploy; stale sessions are hidden after the 30-minute app TTL and physically purged on the next 15-minute cleanup sweep; listing remains disabled in production until identity exists.

**Implementation note:** AWS access is isolated behind `IAgentCoreMemoryClient` so unit tests can exercise request semantics without live credentials. The current SDK client is registered only when `ConversationStore:Provider=AgentCore`.

**Rollback:** `ConversationStore__Provider=InMemory`. Conversations stored in Memory are left behind for up to 7 days (the service expiry backstop). They're unreachable through the API after rollback. The Memory resource can stay.

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
   2. Create `aws_bedrockagentcore_agent_runtime` with `lifecycle { ignore_changes = [agent_runtime_artifact] }`, mirroring the ECS task-definition pattern. That way the CI `infrastructure` job (terraform apply on every push) never rolls the image back, and the `deploy` job owns the image.
      - **Invoke the `DEFAULT` endpoint and don't create a custom `aws_bedrockagentcore_agent_runtime_endpoint`.** Only `DEFAULT` follows the latest runtime version. A custom endpoint stays pinned to its version after `UpdateAgentRuntime`, so CI would report success while production keeps serving the old image. If a named endpoint is needed later (blue/green), CI must also call `UpdateAgentRuntimeEndpoint` to the new version, and Terraform needs `ignore_changes` on the endpoint's version.
   3. Runtime execution role: `bedrock:InvokeModel`, `bedrock:InvokeModelWithResponseStream`, `s3vectors:QueryVectors`/`GetVectors`, CloudWatch/X-Ray.
   4. ECS task role: add `bedrock-agentcore:InvokeAgentRuntime`. **Keep** its Bedrock and S3 Vectors permissions, because search and indexing still use them.
   5. `infra/deploy-role-policy.json`: add the `bedrock-agentcore:*` control-plane actions needed.
   6. ALB: set `idle_timeout = 180` on `aws_lb.api`. With the default 60 s, a batch `/ask` (a model-driven researcher call, a network hop, up to 3 writes and 2 critic calls) can end in a 504. Check p99 against the Phase 0 baseline.
6. **CI:** add a native ARM (or cross-compiled, see Phase 1) build job for `RagAgent.AgentHost` and push the image. Then run `scripts/deploy-agentcore.sh`:
   1. `GetAgentRuntime` to read the current full configuration: role ARN, network, protocol, environment variables, authoriser.
   2. `UpdateAgentRuntime` with **that same configuration plus only the new container URI**. `UpdateAgentRuntime` replaces the whole configuration, so leaving out a Terraform-managed field would silently reset it.
   3. Wait for the new version to be `READY`.
   4. **Post-deploy check:** invoke the `DEFAULT` endpoint with a health payload. The host echoes its `APP_VERSION` (image tag, injected as an env var), and the deploy fails unless it matches the tag just pushed.
7. **The host's dependencies:** `SemanticSearchPlugin` calls `IPostService.GetPostByIdAsync` for each source to build snippets. So the host also registers `AddHackerNewsDataSource` and needs outbound internet. That's fine with `network_mode = PUBLIC`, but it needs a NAT gateway if the runtime later moves into the VPC. Check the runtime role against the S3 Vectors calls the store actually makes (`QueryVectors`, `GetVectors`, and `ListVectors` if `IsIndexEmptyAsync` is reached).
8. **Cold starts:** one runtime session per conversation means a microVM cold start on each conversation's first turn. Include it in the p95 budget and the load test (first-turn and follow-up latencies reported separately).

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
| Client conversation IDs rejected by Memory or Runtime | SHA-256 session ID mapping, and the original ID stored in the payload, not metadata (Phase 5) |
| Cross-user leakage or longer retention via Memory | No long-term strategies before identity, stale sessions purged on the next read or append (30-minute parity), and the list endpoint gated (Phase 5) |
| History written twice / guardrails run twice | Ownership rule: the API owns history and guardrails, and the runtime is stateless (Phase 6) |
| Terraform rolls the runtime image back on every push | `ignore_changes` on the runtime artefact, and CI owns the image (Phase 6) |
| CI deploy "succeeds" but production serves the old agent image | Invoke the `DEFAULT` endpoint, `UpdateAgentRuntime` resends the full config, and a post-deploy `APP_VERSION` check (Phase 6) |
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
