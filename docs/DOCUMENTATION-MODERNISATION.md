# Documentation Modernisation Plan

## Goal

Make the repository documentation accurate, easy to navigate and verifiable. Treat documentation changes like code changes: define what a reader should be able to do, check that the current documentation fails that requirement, make the smallest useful change, then verify the new documentation against the repository.

This plan is deliberately separate from the application modernisation work in [`MODERNISATION-PLAN.md`](MODERNISATION-PLAN.md). It proposes five delivery phases; each phase can be reviewed and merged independently.

## Documentation TDD cycle

For each change:

1. **Specify the reader task.** State who needs the information and what they must be able to do (for example, run the API locally or configure a vector-store provider).
2. **RED — expose a gap.** Record a failing check, stale instruction, missing page, broken link, or unanswered reader task before editing. A red check should identify an actual documentation defect, not just the absence of a new page.
3. **Change the documentation.** Update the smallest authoritative page and its navigation links. Prefer one source of truth over duplicated instructions.
4. **GREEN — verify the claim.** Run the documented commands where practical, compare configuration and API claims with code, and run the relevant documentation checks.
5. **Review for safe use.** Check links, examples, defaults, secret handling and impact on existing reader workflows.

If an external dependency, credential or production environment prevents a verification step, record the limitation and the remaining manual check in the pull request. Do not present an unverified example as tested.

## Five-phase delivery

### Phase 1: Baseline and inventory

**Outcome:** an evidence-based view of what documentation exists, what is stale, and which page owns each topic.

- Inventory root-level guidance, `docs/`, README examples, configuration samples, CI workflows and deployment instructions.
- Compare stated SDK/runtime versions, project names, routes, provider names and command lines with the checked-in source.
- Identify duplicate or contradictory instructions and assign an authoritative page for each topic.
- Prioritise reader tasks: clone and build, run locally, configure a provider, call the API, run tests, and understand deployment.

**RED:** capture at least one concrete mismatch or blocked reader task for each high-priority area; use targeted searches and attempt existing quick-start commands without first correcting them.

**GREEN:** publish the inventory and source-of-truth map; reproduce each known issue or explicitly close it as not applicable. The repository and docs inventory must account for every existing document and significant README instruction.

**Exit criteria:** agreed scope and ownership map, with no undocumented assumption that a stale page is authoritative.

### Phase 2: Architecture alignment

**Outcome:** architecture descriptions and diagrams match the code that is actually present.

- Describe current components, project boundaries, request/data flows and provider responsibilities from source code.
- Distinguish current behaviour from target-state proposals and roadmap items.
- Keep diagrams close to the explanation they support; label planned components clearly.
- Verify the Core/provider boundary, controller/service responsibilities and the actual agent/model orchestration path.

**RED:** trace each documented flow to its implementation and list mismatches, missing ownership, or proposed features described as current.

**GREEN:** review every component and arrow against project references, registrations, routes and service calls; ensure planned components are visibly marked as planned. Render or syntax-check Mermaid diagrams when a supported checker is available.

**Exit criteria:** each architecture statement is source-backed, and current versus proposed design is unambiguous.

### Phase 3: API and configuration documentation

**Outcome:** API consumers and operators can use the routes and settings without guessing.

- Document routes, request and response shapes, status codes, streaming event order, limits and error behaviour.
- Document configuration keys, defaults, accepted values, environment-variable forms and precedence.
- Use generated OpenAPI or code-backed contract tests as the API reference where available; avoid maintaining a second hand-copied schema.
- Use placeholders for credentials and identifiers. Explain secret injection without publishing real values.
- Keep examples minimal and executable; include the expected result or response shape.

**RED:** compare each documented route and setting with controllers, validators, options, registrations and current OpenAPI output; record missing, inaccurate or contradictory details.

**GREEN:** validate examples against the running API or automated contract tests; check documented defaults against configuration binding and startup registrations. For any untestable external integration, identify the prerequisite and mark the example accordingly.

**Exit criteria:** all public routes and operator-facing settings in scope have a documented contract, including important unhappy paths and security-sensitive configuration.

### Phase 4: Contributor workflow

**Outcome:** a new contributor can set up, build, test and make a change using repository-supported instructions.

- Document prerequisites and pinned SDK selection, local dependencies, restore/build/test/format commands, and expected test dependencies.
- Explain the normal change, review and pull-request workflow, including the repository's formatting and validation expectations.
- Separate local development steps from deployment and production operations.
- Ensure shell examples match the intended platform; label alternatives rather than combining incompatible syntax.

**RED:** follow the onboarding instructions from a clean checkout or clean environment and record every missing prerequisite, failed command and ambiguous step.

**GREEN:** repeat the complete documented workflow from a clean state. Commands and expected outcomes must match the repository; platform-specific steps must be labelled and tested on the stated platform where practical.

**Exit criteria:** a contributor can reach a successful build and relevant test run without relying on undocumented local state.

### Phase 5: CI validation and maintenance

**Outcome:** documentation regressions are detected automatically without making routine contributions fragile.

- Add pinned, maintained checks for Markdown structure/style and local links.
- Validate Mermaid and generated API documentation where reliable tooling exists.
- Check documentation examples or scripts with safe, credential-free smoke tests when practical.
- Scope expensive or network-dependent checks appropriately; keep required pull-request checks deterministic.
- Define how suppressions are reviewed, how generated files are refreshed, and who maintains the checks.

**RED:** introduce representative temporary defects in an isolated test or fixture (for example, a malformed Markdown file or broken local link) and confirm each proposed CI check catches it. Do not commit the deliberate defect.

**GREEN:** run the validators against the current repository, remove the fixture, and confirm the normal pull-request workflow passes. Verify that intentional external-link or generated-content exceptions are narrow, documented and reviewed.

**Exit criteria:** CI catches representative documentation errors, remains reproducible without production secrets, and does not require network access for basic local-link validation.

## Documentation TDD checklist

- [ ] Reader, task and authoritative page are identified.
- [ ] RED evidence describes a reproducible gap or failed check.
- [ ] Current behaviour is distinguished from proposed or future behaviour.
- [ ] Commands, versions, routes, defaults and environment-variable names are checked against source.
- [ ] Examples are minimal, safe and tested where practical; expected output is shown when useful.
- [ ] No credentials, tokens, private endpoints or production data are included.
- [ ] Links resolve; relative links are preferred for repository content.
- [ ] Related index/navigation pages are updated, without copying the same instructions into multiple pages.
- [ ] GREEN validation is recorded, including any environment-dependent checks that could not be run.
- [ ] A reviewer can identify the impact on existing users and contributors.

## Risk safeguards

| Risk | Safeguard |
|---|---|
| Documentation claims drift from implementation | Verify claims against code, configuration and generated contracts; prefer executable examples and one authoritative page per topic. |
| Planned architecture is mistaken for shipped behaviour | Label target-state content, diagrams and roadmap items consistently; keep current-state guidance separate. |
| Examples expose credentials or encourage unsafe production changes | Use clearly fake placeholders, least-privilege examples and local/test resources; never include real secrets or production identifiers. |
| A broad restructure breaks existing links | Move pages incrementally, update inbound links in the same change, and retain redirects or compatibility pointers where external links may exist. |
| CI becomes flaky or blocks contributions on external services | Keep core checks deterministic and local; make network-dependent link checks optional or scheduled, with narrow documented exceptions. |
| Generated API docs conflict with hand-maintained prose | Treat generated schema as contract truth and keep explanatory prose focused on usage, limits and operational meaning. |
| Documentation commands damage data or cloud resources | Use disposable local resources and dry-run/sandbox modes; mark destructive actions and require explicit operator confirmation. |

## Suggested `docs/` structure

Adopt this structure gradually as documents are added or materially revised; do not move files solely to match the tree. Keep the root README as the short entry point and use `docs/README.md` as the documentation catalogue.

```text
docs/
├── README.md                         # Navigation and source-of-truth map
├── architecture/
│   ├── overview.md                   # Current components, boundaries and flows
│   └── decisions/                    # Accepted architecture decision records
├── api/
│   ├── overview.md                   # API usage and links to generated OpenAPI
│   └── configuration.md              # Settings, defaults and environment variables
├── development/
│   ├── getting-started.md            # Clean-checkout setup and local run
│   ├── testing.md                    # Unit, integration and formatting checks
│   └── contributing.md               # Change/review workflow
├── operations/
│   ├── deployment.md                 # Deployment and rollback
│   └── troubleshooting.md            # Common failures and diagnostics
├── plans/
│   ├── DOCUMENTATION-MODERNISATION.md
│   └── MODERNISATION-PLAN.md
└── img/                              # Documentation images and diagrams
```

Use stable lowercase filenames for new pages, descriptive headings, relative links, and dates only when the information is time-sensitive. Keep API schemas generated from code where possible; do not check in duplicate copies unless the generation and refresh process is documented.

## Suggested rollout

Deliver one phase per reviewable pull request. Start with the baseline/source-of-truth map, then align architecture and contracts, improve onboarding, and only then make CI enforcement required. Each pull request should state its RED evidence, GREEN checks and any remaining manual validation.
