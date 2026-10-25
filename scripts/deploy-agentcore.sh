#!/usr/bin/env bash
# Updates the AgentCore Runtime image and verifies the DEFAULT endpoint serves it.
#
# Usage: deploy-agentcore.sh <REGION> <RUNTIME_ID> <RUNTIME_ARN> <IMAGE_URI> <IMAGE_TAG>
set -euo pipefail

if [[ $# -ne 5 || -z "$2" || -z "$3" || -z "$4" || -z "$5" ]]; then
  echo "Usage: deploy-agentcore.sh <REGION> <RUNTIME_ID> <RUNTIME_ARN> <IMAGE_URI> <IMAGE_TAG>" >&2
  exit 2
fi

REGION="$1"
RUNTIME_ID="$2"
RUNTIME_ARN="$3"
IMAGE_URI="$4"
IMAGE_TAG="$5"
WORK_DIR="$(mktemp -d)"

CURRENT_RUNTIME="$WORK_DIR/current-runtime.json"
UPDATE_CONFIG="$WORK_DIR/update-runtime.json"
UPDATE_RESULT="$WORK_DIR/update-result.json"
HEALTH_PAYLOAD="$WORK_DIR/health.json"
RESPONSE="$WORK_DIR/health-response.json"
INVOKE_RESULT="$WORK_DIR/invoke-result.json"
trap 'rm -f "$CURRENT_RUNTIME" "$UPDATE_CONFIG" "$UPDATE_RESULT" "$HEALTH_PAYLOAD" "$RESPONSE" "$INVOKE_RESULT"; rmdir "$WORK_DIR"' EXIT
aws bedrock-agentcore-control get-agent-runtime \
  --agent-runtime-id "$RUNTIME_ID" \
  --region "$REGION" \
  --output json > "$CURRENT_RUNTIME"

# UpdateAgentRuntime replaces the full configuration, so retain every field
# Terraform manages and change only the image URI.
jq --arg IMAGE_URI "$IMAGE_URI" '
  {
    agentRuntimeArtifact: (.agentRuntimeArtifact |
      .containerConfiguration.containerUri = $IMAGE_URI),
    authorizerConfiguration,
    capacityProviderConfiguration,
    description,
    environmentVariables,
    filesystemConfigurations,
    lifecycleConfiguration,
    metadataConfiguration,
    networkConfiguration,
    platformVersion,
    protocolConfiguration,
    requestHeaderConfiguration,
    roleArn
  } | with_entries(select(.value != null))
' "$CURRENT_RUNTIME" > "$UPDATE_CONFIG"

aws bedrock-agentcore-control update-agent-runtime \
  --agent-runtime-id "$RUNTIME_ID" \
  --region "$REGION" \
  --cli-input-json "file://$UPDATE_CONFIG" \
  --output json > "$UPDATE_RESULT"

RUNTIME_VERSION="$(jq -er '.agentRuntimeVersion' "$UPDATE_RESULT")"
echo "Waiting for AgentCore Runtime version $RUNTIME_VERSION to become READY..."
for attempt in $(seq 1 90); do
  STATUS="$(aws bedrock-agentcore-control get-agent-runtime \
    --agent-runtime-id "$RUNTIME_ID" \
    --agent-runtime-version "$RUNTIME_VERSION" \
    --region "$REGION" \
    --query status \
    --output text)"
  if [ "$STATUS" = "READY" ]; then
    break
  fi
  if [ "$STATUS" = "UPDATE_FAILED" ] || [ "$STATUS" = "CREATE_FAILED" ]; then
    echo "AgentCore Runtime version $RUNTIME_VERSION entered status $STATUS." >&2
    exit 1
  fi
  if [ "$attempt" -eq 90 ]; then
    echo "Timed out waiting for AgentCore Runtime version $RUNTIME_VERSION; last status: $STATUS." >&2
    exit 1
  fi
  sleep 20
done

printf '{"healthCheck":true}\n' > "$HEALTH_PAYLOAD"
SESSION_ID="$(printf '%s' "$IMAGE_TAG" | sha256sum | cut -d ' ' -f 1)"
aws bedrock-agentcore invoke-agent-runtime \
  --agent-runtime-arn "$RUNTIME_ARN" \
  --runtime-session-id "$SESSION_ID" \
  --qualifier DEFAULT \
  --content-type application/json \
  --accept application/json \
  --payload "fileb://$HEALTH_PAYLOAD" \
  "$RESPONSE" \
  --region "$REGION" \
  --output json > "$INVOKE_RESULT"

if ! jq -e '.statusCode == 200' "$INVOKE_RESULT" >/dev/null; then
  echo "AgentCore Runtime health invocation did not return HTTP 200." >&2
  cat "$INVOKE_RESULT" >&2
  exit 1
fi

if ! jq -e --arg IMAGE_TAG "$IMAGE_TAG" '.appVersion == $IMAGE_TAG' "$RESPONSE" >/dev/null 2>&1; then
  echo "AgentCore Runtime health check did not report expected APP_VERSION '$IMAGE_TAG'." >&2
  cat "$RESPONSE" >&2
  exit 1
fi

echo "AgentCore Runtime DEFAULT endpoint is serving image $IMAGE_TAG."
