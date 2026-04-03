#!/usr/bin/env bash
# Bootstrap script — run once with admin credentials to:
#   1. Create the Terraform remote-state S3 bucket (idempotent).
#   2. Update the GitHubActionsDeployRole inline policy.
#
# This is NOT executed by CI; it exists so all one-time setup is
# version-controlled and reproducible.
#
# Usage:
#   cd infra
#   ./bootstrap-deploy-role.sh

set -euo pipefail

ROLE_NAME="GitHubActionsDeployRole"
POLICY_NAME="GitHubActionsDeployPolicy"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
AWS_REGION="${AWS_REGION:-us-east-1}"
AWS_ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text)"
POLICY_TEMPLATE="${SCRIPT_DIR}/deploy-role-policy.json"
RENDERED_POLICY="$(mktemp)"
TF_STATE_BUCKET="${TF_STATE_BUCKET:-dotnet-rag-agent-tf-state}"

trap 'rm -f "$RENDERED_POLICY"' EXIT

# ── Terraform state bucket ────────────────────────────────────────────────────

echo "Ensuring Terraform state bucket '$TF_STATE_BUCKET' exists..."

if aws s3api head-bucket --bucket "$TF_STATE_BUCKET" 2>/dev/null; then
  echo "  Bucket already exists — skipping creation."
else
  if [ "$AWS_REGION" = "us-east-1" ]; then
    aws s3api create-bucket \
      --bucket "$TF_STATE_BUCKET" \
      --region "$AWS_REGION"
  else
    aws s3api create-bucket \
      --bucket "$TF_STATE_BUCKET" \
      --region "$AWS_REGION" \
      --create-bucket-configuration LocationConstraint="$AWS_REGION"
  fi
  echo "  Bucket created."
fi

aws s3api put-bucket-versioning \
  --bucket "$TF_STATE_BUCKET" \
  --versioning-configuration Status=Enabled

aws s3api put-bucket-encryption \
  --bucket "$TF_STATE_BUCKET" \
  --server-side-encryption-configuration '{
    "Rules": [{
      "ApplyServerSideEncryptionByDefault": {"SSEAlgorithm": "AES256"},
      "BucketKeyEnabled": true
    }]
  }'

aws s3api put-public-access-block \
  --bucket "$TF_STATE_BUCKET" \
  --public-access-block-configuration \
    "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"

echo "  Versioning, encryption, and public-access-block configured."

# ── IAM deploy-role policy ────────────────────────────────────────────────────

sed \
  -e "s/__AWS_ACCOUNT_ID__/${AWS_ACCOUNT_ID}/g" \
  -e "s/__AWS_REGION__/${AWS_REGION}/g" \
  "$POLICY_TEMPLATE" > "$RENDERED_POLICY"

aws iam put-role-policy \
  --role-name "$ROLE_NAME" \
  --policy-name "$POLICY_NAME" \
  --policy-document "file://${RENDERED_POLICY}"

echo "Updated inline policy '$POLICY_NAME' on role '$ROLE_NAME'."
