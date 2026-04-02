#!/usr/bin/env bash
# Bootstrap script — run once with admin credentials to set up the
# GitHubActionsDeployRole inline policy.  This is NOT executed by CI;
# it exists so the required permissions are version-controlled.
#
# Usage:
#   cd infra
#   ./bootstrap-deploy-role.sh

set -euo pipefail

ROLE_NAME="GitHubActionsDeployRole"
POLICY_NAME="GitHubActionsDeployPolicy"
LEGACY_POLICY_NAME="GitHubActionsECRAppRunner"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
AWS_REGION="${AWS_REGION:-us-east-1}"
AWS_ACCOUNT_ID="$(aws sts get-caller-identity --query Account --output text)"
POLICY_TEMPLATE="${SCRIPT_DIR}/deploy-role-policy.json"
RENDERED_POLICY="$(mktemp)"

trap 'rm -f "$RENDERED_POLICY"' EXIT

sed \
  -e "s/__AWS_ACCOUNT_ID__/${AWS_ACCOUNT_ID}/g" \
  -e "s/__AWS_REGION__/${AWS_REGION}/g" \
  "$POLICY_TEMPLATE" > "$RENDERED_POLICY"

# Remove legacy policy name if it still exists
aws iam delete-role-policy \
  --role-name "$ROLE_NAME" \
  --policy-name "$LEGACY_POLICY_NAME" 2>/dev/null || true

aws iam put-role-policy \
  --role-name "$ROLE_NAME" \
  --policy-name "$POLICY_NAME" \
  --policy-document "file://${RENDERED_POLICY}"

echo "Updated inline policy '$POLICY_NAME' on role '$ROLE_NAME'."
