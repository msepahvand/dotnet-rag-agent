#!/usr/bin/env bash
# Registers a new ECS task definition with the given image and triggers a rolling deployment.
# Also ensures the ADOT Collector sidecar is present for X-Ray tracing (idempotent).
#
# Usage: deploy-ecs.sh <REGION> <ECS_CLUSTER> <ECS_SERVICE> <IMAGE_URI> <AGENTCORE_MEMORY_ID> <CONVERSATION_LOCK_TABLE>
set -euo pipefail

if [[ $# -ne 6 || -z "$5" || -z "$6" ]]; then
  echo "Usage: deploy-ecs.sh <REGION> <ECS_CLUSTER> <ECS_SERVICE> <IMAGE_URI> <AGENTCORE_MEMORY_ID> <CONVERSATION_LOCK_TABLE>" >&2
  exit 2
fi

REGION="$1"
ECS_CLUSTER="$2"
ECS_SERVICE="$3"
IMAGE_URI="$4"
AGENTCORE_MEMORY_ID="$5"
CONVERSATION_LOCK_TABLE="$6"

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
  --arg MEMORY_ID "$AGENTCORE_MEMORY_ID" \
  --arg LOCK_TABLE "$CONVERSATION_LOCK_TABLE" \
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

  # Keep existing rollout settings; use the safe shadow defaults for first deploys.
  .containerDefinitions[0].environment = (
    .containerDefinitions[0].environment +
    (if ([.containerDefinitions[0].environment[] | select(.name == "Guardrails__Provider")] | length) == 0
     then [{name: "Guardrails__Provider", value: "Bedrock"}]
     else []
     end) +
    (if ([.containerDefinitions[0].environment[] | select(.name == "Guardrails__Mode")] | length) == 0
     then [{name: "Guardrails__Mode", value: "Shadow"}]
     else []
     end)
  ) |

  # Terraform ignores container definition changes so the deployment script owns
  # the rollout environment, including the durable conversation-store provider.
  .containerDefinitions[0].environment = (
    [(.containerDefinitions[0].environment // [])[] |
      select(.name != "ConversationStore__Provider" and
             .name != "ConversationStore__AgentCore__MemoryId" and
             .name != "ConversationStore__AgentCore__LockTableName" and
             .name != "Conversations__ListEnabled")] +
    [
      {name: "ConversationStore__Provider", value: "AgentCore"},
      {name: "ConversationStore__AgentCore__MemoryId", value: $MEMORY_ID},
      {name: "ConversationStore__AgentCore__LockTableName", value: $LOCK_TABLE},
      {name: "Conversations__ListEnabled", value: "false"}
    ]
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
