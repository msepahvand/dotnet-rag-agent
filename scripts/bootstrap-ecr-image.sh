#!/usr/bin/env bash
# Pushes a bootstrap image to ECR if the repository is empty.
# ECS requires at least one image to exist before a service can be created.
#
# Usage: bootstrap-ecr-image.sh <REGION> <ACCOUNT_ID> <ECR_REPO>
set -euo pipefail

REGION="$1"
ACCOUNT_ID="$2"
ECR_REPO="$3"
ECR_URI="$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com/$ECR_REPO"

IMAGE_COUNT=$(aws ecr list-images --repository-name "$ECR_REPO" --region "$REGION" \
  --query 'length(imageIds)' --output text 2>/dev/null || echo "0")

if [ "$IMAGE_COUNT" = "0" ] || [ "$IMAGE_COUNT" = "None" ]; then
  echo "ECR is empty — building and pushing bootstrap image..."
  aws ecr get-login-password --region "$REGION" \
    | docker login --username AWS --password-stdin "$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com"
  docker build -f VectorSearch.Api/Dockerfile -t "$ECR_URI:latest" .
  docker push "$ECR_URI:latest"
else
  echo "ECR already has $IMAGE_COUNT image(s) — skipping bootstrap."
fi
