#!/usr/bin/env bash
set -euo pipefail

# Safe teardown helper for application infrastructure only.
# This intentionally does not remove the Terraform backend state bucket.

if [ "${DESTROY_CONFIRM:-}" != "YES" ]; then
  echo "Refusing to destroy. Set DESTROY_CONFIRM=YES to proceed." >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

AWS_REGION="${AWS_REGION:-us-east-1}"
ECR_REPOSITORY_NAME="${ECR_REPOSITORY_NAME:-dotnet-vector-search}"
APP_RUNNER_SERVICE_NAME="${APP_RUNNER_SERVICE_NAME:-dotnet-vector-search}"
VECTOR_BUCKET_NAME="${VECTOR_BUCKET_NAME:-posts-semantic-search}"
VECTOR_INDEX_NAME="${VECTOR_INDEX_NAME:-posts-content-index}"
GITHUB_REPO="${GITHUB_REPO:-}"
GITHUB_BRANCH="${GITHUB_BRANCH:-}"

if [ -z "$GITHUB_REPO" ]; then
  echo "Missing required GITHUB_REPO environment variable (for Terraform variable github_repo)." >&2
  exit 1
fi

export TF_VAR_aws_region="$AWS_REGION"
export TF_VAR_ecr_repository_name="$ECR_REPOSITORY_NAME"
export TF_VAR_apprunner_service_name="$APP_RUNNER_SERVICE_NAME"
export TF_VAR_vector_bucket_name="$VECTOR_BUCKET_NAME"
export TF_VAR_vector_index_name="$VECTOR_INDEX_NAME"
export TF_VAR_github_repo="$GITHUB_REPO"
export TF_VAR_github_branch="$GITHUB_BRANCH"

echo "Initializing Terraform..."
terraform init -reconfigure

BUCKET_ADDRESS="module.s3_vectors.aws_s3vectors_vector_bucket.vector_bucket"
INDEX_ADDRESS="module.s3_vectors.aws_s3vectors_index.vector_index"
ECR_ADDRESS="aws_ecr_repository.api"
APP_RUNNER_ADDRESS="aws_apprunner_service.api"

echo "Cleaning up stale Terraform state entries (best effort)..."

vector_bucket_exists="false"
bucket_found=$(aws s3vectors list-vector-buckets \
  --region "$AWS_REGION" \
  --query "vectorBuckets[?vectorBucketName=='$VECTOR_BUCKET_NAME'].vectorBucketName | [0]" \
  --output text 2>/dev/null || true)
if [ "$bucket_found" != "None" ] && [ -n "$bucket_found" ]; then
  vector_bucket_exists="true"
elif terraform state show "$BUCKET_ADDRESS" >/dev/null 2>&1; then
  terraform state rm "$BUCKET_ADDRESS" >/dev/null || true
fi

if [ "$vector_bucket_exists" = "true" ]; then
  index_found=$(aws s3vectors list-indexes \
    --region "$AWS_REGION" \
    --vector-bucket-name "$VECTOR_BUCKET_NAME" \
    --query "indexes[?indexName=='$VECTOR_INDEX_NAME'].indexName | [0]" \
    --output text 2>/dev/null || true)
  if [ "$index_found" = "None" ] || [ -z "$index_found" ]; then
    if terraform state show "$INDEX_ADDRESS" >/dev/null 2>&1; then
      terraform state rm "$INDEX_ADDRESS" >/dev/null || true
    fi
  fi
elif terraform state show "$INDEX_ADDRESS" >/dev/null 2>&1; then
  terraform state rm "$INDEX_ADDRESS" >/dev/null || true
fi

if ! aws ecr describe-repositories --region "$AWS_REGION" --repository-names "$ECR_REPOSITORY_NAME" >/dev/null 2>&1; then
  if terraform state show "$ECR_ADDRESS" >/dev/null 2>&1; then
    terraform state rm "$ECR_ADDRESS" >/dev/null || true
  fi
fi

app_runner_arn=$(aws apprunner list-services \
  --region "$AWS_REGION" \
  --query "ServiceSummaryList[?ServiceName=='$APP_RUNNER_SERVICE_NAME'].ServiceArn | [0]" \
  --output text 2>/dev/null || true)
if [ "$app_runner_arn" = "None" ] || [ -z "$app_runner_arn" ]; then
  if terraform state show "$APP_RUNNER_ADDRESS" >/dev/null 2>&1; then
    terraform state rm "$APP_RUNNER_ADDRESS" >/dev/null || true
  fi
fi

# Delete all images from ECR so terraform destroy can remove the repository
if aws ecr describe-repositories --region "$AWS_REGION" --repository-names "$ECR_REPOSITORY_NAME" >/dev/null 2>&1; then
  echo "Deleting all images from ECR repository $ECR_REPOSITORY_NAME..."
  IMAGE_IDS=$(aws ecr list-images \
    --region "$AWS_REGION" \
    --repository-name "$ECR_REPOSITORY_NAME" \
    --query 'imageIds[*]' \
    --output json 2>/dev/null || echo "[]")
  if [ "$IMAGE_IDS" != "[]" ] && [ -n "$IMAGE_IDS" ]; then
    aws ecr batch-delete-image \
      --region "$AWS_REGION" \
      --repository-name "$ECR_REPOSITORY_NAME" \
      --image-ids "$IMAGE_IDS" >/dev/null 2>&1 || true
  fi
fi

echo "Destroying Terraform-managed application resources..."
terraform destroy -auto-approve \
  -var="aws_region=$AWS_REGION" \
  -var="github_repo=$GITHUB_REPO" \
  -var="github_branch=$GITHUB_BRANCH" \
  -var="ecr_repository_name=$ECR_REPOSITORY_NAME" \
  -var="apprunner_service_name=$APP_RUNNER_SERVICE_NAME" \
  -var="vector_bucket_name=$VECTOR_BUCKET_NAME" \
  -var="vector_index_name=$VECTOR_INDEX_NAME"

echo "Teardown complete. Backend state bucket was intentionally preserved."
