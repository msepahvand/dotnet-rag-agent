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
POLICY_NAME="GitHubActionsECRAppRunner"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

aws iam put-role-policy \
  --role-name "$ROLE_NAME" \
  --policy-name "$POLICY_NAME" \
  --policy-document "file://${SCRIPT_DIR}/deploy-role-policy.json"

echo "Updated inline policy '$POLICY_NAME' on role '$ROLE_NAME'."
