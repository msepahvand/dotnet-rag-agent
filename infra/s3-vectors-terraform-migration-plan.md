# S3 Vectors Terraform Migration Plan

## Goal
Move from imperative `local-exec` AWS CLI creation to fully declarative Terraform-managed S3 Vectors resources.

## Why migrate
- Better drift detection and reconciliation
- Safer plans (explicit create/update/destroy)
- Cleaner CI/CD (fewer shell retries and parsing issues)
- Stronger long-term maintainability

## Current state (today)
- S3 Vectors bucket/index are created via `terraform_data` + `local-exec` in `infra/modules/s3_vectors/main.tf`.
- CI has verification/self-heal logic in `.github/workflows/ci-cd.yml` to mitigate drift and eventual consistency.

## Target state
- S3 Vectors bucket/index are defined as native Terraform resources.
- No create-if-missing logic in CI.
- CI only runs `terraform init/plan/apply` and optional read-only post-apply checks.

## Migration phases

### Phase 1 — Readiness and gating
1. Track Terraform AWS provider support for native S3 Vectors resources.
2. Introduce a feature flag variable:
   - `use_native_s3vectors_resources` (default `false`).
3. Keep current `local-exec` flow as fallback while validating provider behavior.

### Phase 2 — Add native resources
1. Add native Terraform resources for:
   - vector bucket
   - vector index
2. Keep names/shape aligned with existing variables:
   - `vector_bucket_name`
   - `vector_index_name`
   - `vector_dimension`
   - `vector_distance_metric`
   - `vector_data_type`
3. Add lifecycle protections where appropriate (e.g., prevent accidental destroy in prod).

### Phase 3 — State migration
1. In a non-production environment, switch `use_native_s3vectors_resources=true`.
2. Import existing resources into Terraform state (exact commands depend on final resource types):
   - `terraform import <bucket_resource_address> <bucket_id>`
   - `terraform import <index_resource_address> <index_id>`
3. Run `terraform plan` and confirm no destructive changes.
4. Repeat for production with approval gate.

### Phase 4 — Remove imperative fallback
1. Remove `terraform_data` + `local-exec` bucket/index creation.
2. Remove CI self-heal creation logic from `.github/workflows/ci-cd.yml`.
3. Keep a lightweight read-only verification step (optional).

### Phase 5 — Hardening
1. Add policy checks for Terraform plans in CI.
2. Add drift detection schedule (`terraform plan -detailed-exitcode`).
3. Document rollback steps.

## Rollback strategy
- If native resource behavior is unstable, set `use_native_s3vectors_resources=false` and re-apply.
- Preserve current outputs and naming so app config does not change.

## Suggested acceptance criteria
- `terraform plan` is deterministic and free from shell-based create logic.
- No CI steps perform imperative create-if-missing operations for S3 Vectors.
- New environment bootstrap works with Terraform alone.
- Existing environments can be imported with zero-downtime cutover.

## Notes
- Keep production gated by manual approval during migration.
- Perform first cutover in a staging environment before production.
