#!/usr/bin/env bash
# Pushes an ARM64 bootstrap image to ECR before Terraform creates the runtime.
#
# Usage: bootstrap-agentcore-ecr-image.sh <REGION> <ACCOUNT_ID> <ECR_REPO>
set -euo pipefail

REGION="$1"
ACCOUNT_ID="$2"
ECR_REPO="$3"
ECR_URI="$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com/$ECR_REPO"

IMAGE_COUNT=$(aws ecr list-images --repository-name "$ECR_REPO" --region "$REGION" \
  --query "length(imageIds[?imageTag=='latest'])" --output text 2>/dev/null || echo "0")

if [ "$IMAGE_COUNT" = "0" ] || [ "$IMAGE_COUNT" = "None" ]; then
  echo "AgentCore ECR is empty — building and pushing ARM64 bootstrap image..."
  aws ecr get-login-password --region "$REGION" \
    | docker login --username AWS --password-stdin "$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com"
  docker buildx build \
    --platform linux/arm64 \
    --build-arg APP_VERSION=bootstrap \
    --file src/RagAgent.AgentHost/Dockerfile \
    --tag "$ECR_URI:latest" \
    --push \
    .

  docker buildx imagetools inspect --raw "$ECR_URI:latest" \
    | jq -e '([.manifests[].platform.architecture] | index("arm64"))' \
    >/dev/null
else
  echo "AgentCore ECR already has $IMAGE_COUNT image(s) — skipping bootstrap."
fi
