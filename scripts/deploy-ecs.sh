#!/usr/bin/env bash
# Registers a new ECS task definition with the given image and triggers a rolling deployment.
# Also ensures the ADOT Collector sidecar is present for X-Ray tracing (idempotent).
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

# Build the ADOT Collector sidecar definition.
# Reads the config from infra/otel-collector-config.yaml relative to the repo root.
# The collector inherits the AWS region and credentials from the ECS task role.
COLLECTOR_CONFIG=$(cat infra/otel-collector-config.yaml)

OTEL_SIDECAR=$(jq -n \
  --arg CONFIG "$COLLECTOR_CONFIG" \
  --arg REGION "$REGION" \
  --arg LOG_GROUP "/ecs/$ECS_SERVICE/otel-collector" \
  '{
    name: "aws-otel-collector",
    image: "public.ecr.aws/aws-observability/aws-otel-collector:latest",
    essential: false,
    environment: [{name: "AOT_CONFIG_CONTENT", value: $CONFIG}],
    logConfiguration: {
      logDriver: "awslogs",
      options: {
        "awslogs-group": $LOG_GROUP,
        "awslogs-region": $REGION,
        "awslogs-stream-prefix": "ecs"
      }
    }
  }')

NEW_TASK_DEF=$(echo "$CURRENT_TASK_DEF" | jq \
  --arg IMAGE "$IMAGE_URI" \
  --argjson SIDECAR "$OTEL_SIDECAR" \
  '
  # Update the API container image.
  .containerDefinitions[0].image = $IMAGE |

  # Ensure OpenTelemetry__OtlpEndpoint points to the sidecar on localhost.
  # Removes any existing entry first so the value is always up to date.
  .containerDefinitions[0].environment = (
    [(.containerDefinitions[0].environment // [])[] | select(.name != "OpenTelemetry__OtlpEndpoint")] +
    [{name: "OpenTelemetry__OtlpEndpoint", value: "http://localhost:4317"}]
  ) |

  # Add the ADOT sidecar only if it is not already present (idempotent).
  if (.containerDefinitions | map(select(.name == "aws-otel-collector")) | length) == 0
  then .containerDefinitions += [$SIDECAR]
  else .
  end |

  # Remove ECS-managed fields before re-registering.
  del(.taskDefinitionArn, .revision, .status, .requiresAttributes,
      .placementConstraints, .compatibilities, .registeredAt, .registeredBy)
  ')

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
