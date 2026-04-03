#!/usr/bin/env bash
# Registers a new ECS task definition with the given image and triggers a rolling deployment.
#
# Usage: deploy-ecs.sh <REGION> <ECS_CLUSTER> <ECS_SERVICE> <IMAGE_URI>
set -euo pipefail

REGION="$1"
ECS_CLUSTER="$2"
ECS_SERVICE="$3"
IMAGE_URI="$4"

CURRENT_TASK_DEF=$(aws ecs describe-task-definition \
  --task-definition "$ECS_SERVICE" \
  --region "$REGION" \
  --query 'taskDefinition' \
  --output json)

NEW_TASK_DEF=$(echo "$CURRENT_TASK_DEF" | jq \
  --arg IMAGE "$IMAGE_URI" \
  '.containerDefinitions[0].image = $IMAGE |
   del(.taskDefinitionArn, .revision, .status, .requiresAttributes,
       .placementConstraints, .compatibilities, .registeredAt, .registeredBy)')

NEW_TASK_DEF_ARN=$(aws ecs register-task-definition \
  --region "$REGION" \
  --cli-input-json "$NEW_TASK_DEF" \
  --query 'taskDefinition.taskDefinitionArn' \
  --output text)

echo "New task definition: $NEW_TASK_DEF_ARN"

# Rolling deployment: ECS starts the new task, waits for it to be healthy,
# then stops the old task. No downtime.
aws ecs update-service \
  --cluster "$ECS_CLUSTER" \
  --service "$ECS_SERVICE" \
  --task-definition "$NEW_TASK_DEF_ARN" \
  --region "$REGION"

echo "Waiting for rolling deployment to stabilise..."
aws ecs wait services-stable \
  --cluster "$ECS_CLUSTER" \
  --services "$ECS_SERVICE" \
  --region "$REGION"

echo "Deployment complete."
