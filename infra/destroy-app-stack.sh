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

export TF_VAR_aws_region="$AWS_REGION"
export TF_VAR_ecr_repository_name="$ECR_REPOSITORY_NAME"
export TF_VAR_apprunner_service_name="$APP_RUNNER_SERVICE_NAME"
export TF_VAR_vector_bucket_name="$VECTOR_BUCKET_NAME"
export TF_VAR_vector_index_name="$VECTOR_INDEX_NAME"

echo "Initializing Terraform..."
terraform init -reconfigure

echo "Destroying Terraform-managed application resources..."
terraform destroy -auto-approve \
  -var="aws_region=$AWS_REGION" \
  -var="ecr_repository_name=$ECR_REPOSITORY_NAME" \
  -var="apprunner_service_name=$APP_RUNNER_SERVICE_NAME" \
  -var="vector_bucket_name=$VECTOR_BUCKET_NAME" \
  -var="vector_index_name=$VECTOR_INDEX_NAME"

echo "Teardown complete. Backend state bucket was intentionally preserved."
