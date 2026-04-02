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
ECS_CLUSTER_NAME="${ECS_CLUSTER_NAME:-dotnet-vector-search}"
ECS_SERVICE_NAME="${ECS_SERVICE_NAME:-dotnet-vector-search}"
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
export TF_VAR_ecs_cluster_name="$ECS_CLUSTER_NAME"
export TF_VAR_ecs_service_name="$ECS_SERVICE_NAME"
export TF_VAR_vector_bucket_name="$VECTOR_BUCKET_NAME"
export TF_VAR_vector_index_name="$VECTOR_INDEX_NAME"
export TF_VAR_github_repo="$GITHUB_REPO"
export TF_VAR_github_branch="$GITHUB_BRANCH"

echo "Initialising Terraform..."
terraform init -reconfigure

BUCKET_ADDRESS="module.s3_vectors.aws_s3vectors_vector_bucket.vector_bucket"
INDEX_ADDRESS="module.s3_vectors.aws_s3vectors_index.vector_index"
ECR_ADDRESS="aws_ecr_repository.api"
ECS_CLUSTER_ADDRESS="aws_ecs_cluster.api"
ECS_SERVICE_ADDRESS="aws_ecs_service.api"

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

# Remove ECS resources from state if they no longer exist in AWS
if ! aws ecs describe-services \
    --region "$AWS_REGION" \
    --cluster "$ECS_CLUSTER_NAME" \
    --services "$ECS_SERVICE_NAME" \
    --query "services[?status=='ACTIVE'].serviceArn | [0]" \
    --output text 2>/dev/null | grep -q "arn:"; then
  terraform state rm "$ECS_SERVICE_ADDRESS" >/dev/null || true
fi

if ! aws ecs describe-clusters \
    --region "$AWS_REGION" \
    --clusters "$ECS_CLUSTER_NAME" \
    --query "clusters[?status=='ACTIVE'].clusterName | [0]" \
    --output text 2>/dev/null | grep -q "$ECS_CLUSTER_NAME"; then
  terraform state rm "$ECS_CLUSTER_ADDRESS" >/dev/null || true
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
  -var="ecs_cluster_name=$ECS_CLUSTER_NAME" \
  -var="ecs_service_name=$ECS_SERVICE_NAME" \
  -var="vector_bucket_name=$VECTOR_BUCKET_NAME" \
  -var="vector_index_name=$VECTOR_INDEX_NAME"

echo "Teardown complete. Backend state bucket was intentionally preserved."
