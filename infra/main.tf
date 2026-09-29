
provider "aws" {
  region = local.effective_aws_region
}

terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 6.35.0, < 7.0.0"
    }
  }

  backend "s3" {
    bucket  = "dotnet-rag-agent-tf-state"
    key     = "global/terraform.tfstate"
    region  = "us-east-1"
    encrypt = true
  }
}

data "aws_caller_identity" "current" {}

locals {
  effective_aws_region = trimspace(var.aws_region) != "" ? var.aws_region : "us-east-1"
  ecr_repository_url   = "${data.aws_caller_identity.current.account_id}.dkr.ecr.${local.effective_aws_region}.amazonaws.com/${var.ecr_repository_name}"
}

# ── Networking ────────────────────────────────────────────────────────────────

data "aws_availability_zones" "available" {
  state = "available"
}

resource "aws_vpc" "main" {
  cidr_block           = "10.0.0.0/16"
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = {
    Name = var.ecs_service_name
  }
}

resource "aws_subnet" "public" {
  count                   = 2
  vpc_id                  = aws_vpc.main.id
  cidr_block              = cidrsubnet("10.0.0.0/16", 8, count.index)
  availability_zone       = data.aws_availability_zones.available.names[count.index]
  map_public_ip_on_launch = true

  tags = {
    Name = "${var.ecs_service_name}-public-${count.index}"
  }
}

resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id

  tags = {
    Name = var.ecs_service_name
  }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.main.id
  }

  tags = {
    Name = "${var.ecs_service_name}-public"
  }
}

resource "aws_route_table_association" "public" {
  count          = 2
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

resource "aws_security_group" "alb" {
  name        = "${var.ecs_service_name}-alb"
  description = "Allow HTTP inbound to the load balancer"
  vpc_id      = aws_vpc.main.id

  ingress {
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_security_group" "ecs_tasks" {
  name        = "${var.ecs_service_name}-tasks"
  description = "Allow inbound from ALB on the container port"
  vpc_id      = aws_vpc.main.id

  ingress {
    from_port       = var.container_port
    to_port         = var.container_port
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

# ── Load balancer ─────────────────────────────────────────────────────────────

resource "aws_lb" "api" {
  name               = var.ecs_service_name
  internal           = false
  load_balancer_type = "application"
  security_groups    = [aws_security_group.alb.id]
  subnets            = aws_subnet.public[*].id
}

resource "aws_lb_target_group" "api" {
  name        = var.ecs_service_name
  port        = var.container_port
  protocol    = "HTTP"
  vpc_id      = aws_vpc.main.id
  target_type = "ip"

  health_check {
    path                = "/api/posts"
    interval            = 30
    timeout             = 10
    healthy_threshold   = 2
    unhealthy_threshold = 3
    matcher             = "200"
  }
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.api.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }
}

# ── IAM — ECS task execution role (ECR pull + CloudWatch logs) ─────────────────

data "aws_iam_policy_document" "ecs_task_assume" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "ecs_task_execution" {
  name               = "EcsTaskExecutionRole"
  assume_role_policy = data.aws_iam_policy_document.ecs_task_assume.json
}

resource "aws_iam_role_policy_attachment" "ecs_task_execution" {
  role       = aws_iam_role.ecs_task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# ── IAM — ECS task role (application runtime permissions) ─────────────────────

resource "aws_iam_role" "ecs_task" {
  name               = "EcsTaskRole"
  assume_role_policy = data.aws_iam_policy_document.ecs_task_assume.json
}

resource "aws_bedrock_guardrail" "api_safety" {
  name                      = "${substr(var.ecs_service_name, 0, 30)}-managed-safety"
  description               = "Request-boundary safety policies for the RAG API"
  blocked_input_messaging   = "The question was blocked by safety policies."
  blocked_outputs_messaging = "The answer did not pass the safety policies."

  content_policy_config {
    filters_config {
      type            = "PROMPT_ATTACK"
      input_strength  = "HIGH"
      output_strength = "NONE"
      input_enabled   = true
      output_enabled  = false
      input_action    = "BLOCK"
      output_action   = "NONE"
    }
  }

  sensitive_information_policy_config {
    dynamic "pii_entities_config" {
      for_each = ["EMAIL", "PHONE", "CREDIT_DEBIT_CARD_NUMBER"]

      content {
        type           = pii_entities_config.value
        action         = "BLOCK"
        input_action   = "BLOCK"
        output_action  = "ANONYMIZE"
        input_enabled  = true
        output_enabled = true
      }
    }
  }

  word_policy_config {
    dynamic "words_config" {
      for_each = [
        "legal advice",
        "medical diagnosis",
        "financial advice",
        "stock tips",
        "investment advice",
        "tax advice"
      ]

      content {
        text           = words_config.value
        input_action   = "BLOCK"
        output_action  = "NONE"
        input_enabled  = true
        output_enabled = false
      }
    }
  }

  contextual_grounding_policy_config {
    filters_config {
      type      = "GROUNDING"
      threshold = 0.7
    }

    filters_config {
      type      = "RELEVANCE"
      threshold = 0.7
    }
  }
}

resource "aws_bedrock_guardrail_version" "api_safety" {
  guardrail_arn = aws_bedrock_guardrail.api_safety.guardrail_arn
  description   = "Version deployed with the RAG API"
  skip_destroy  = true
}

data "aws_iam_policy_document" "ecs_task_runtime" {
  statement {
    sid    = "AllowBedrockInvokeModel"
    effect = "Allow"
    actions = [
      "bedrock:InvokeModel",
      "bedrock:InvokeModelWithResponseStream"
    ]
    resources = [
      "arn:aws:bedrock:*::foundation-model/*",
      "arn:aws:bedrock:${local.effective_aws_region}:${data.aws_caller_identity.current.account_id}:inference-profile/*"
    ]
  }

  statement {
    sid       = "AllowBedrockApplyGuardrail"
    effect    = "Allow"
    actions   = ["bedrock:ApplyGuardrail"]
    resources = [aws_bedrock_guardrail.api_safety.guardrail_arn]
  }

  statement {
    sid    = "AllowS3Vectors"
    effect = "Allow"
    actions = [
      "s3vectors:*"
    ]
    resources = ["*"]
  }

  statement {
    sid    = "AllowMarketplaceForBedrock"
    effect = "Allow"
    actions = [
      "aws-marketplace:ViewSubscriptions",
      "aws-marketplace:Subscribe"
    ]
    resources = ["*"]
  }

  statement {
    sid    = "AllowXRayTracing"
    effect = "Allow"
    actions = [
      "xray:PutTraceSegments",
      "xray:PutTelemetryRecords",
      "xray:GetSamplingRules",
      "xray:GetSamplingTargets",
    ]
    resources = ["*"]
  }

  statement {
    sid    = "AllowGuardrailMetricExport"
    effect = "Allow"
    actions = [
      "logs:CreateLogGroup",
      "logs:CreateLogStream",
      "logs:PutLogEvents"
    ]
    resources = [
      "arn:aws:logs:${local.effective_aws_region}:${data.aws_caller_identity.current.account_id}:log-group:/aws/metrics/rag-agent:*"
    ]
  }
}

resource "aws_iam_role_policy" "ecs_task_runtime" {
  name   = "EcsVectorSearchRuntime"
  role   = aws_iam_role.ecs_task.name
  policy = data.aws_iam_policy_document.ecs_task_runtime.json
}

# ── Observability ─────────────────────────────────────────────────────────────

resource "aws_cloudwatch_log_group" "api" {
  name              = "/ecs/${var.ecs_service_name}"
  retention_in_days = 7
}

# Separate log group for the ADOT Collector sidecar so collector logs don't
# mix with application logs.
resource "aws_cloudwatch_log_group" "otel_collector" {
  name              = "/ecs/${var.ecs_service_name}/otel-collector"
  retention_in_days = 7
}

# ── S3 Vectors ────────────────────────────────────────────────────────────────

module "s3_vectors" {
  source = "./modules/s3_vectors"

  aws_region         = local.effective_aws_region
  vector_bucket_name = var.vector_bucket_name
  vector_index_name  = var.vector_index_name
  vector_dimension   = var.vector_dimension
  distance_metric    = var.vector_distance_metric
  data_type          = var.vector_data_type
}

# ── ECR ───────────────────────────────────────────────────────────────────────

resource "aws_ecr_repository" "api" {
  name                 = var.ecr_repository_name
  image_tag_mutability = "IMMUTABLE"
  force_delete         = true

  image_scanning_configuration {
    scan_on_push = true
  }
}

# ── ECS ───────────────────────────────────────────────────────────────────────

resource "aws_ecs_cluster" "api" {
  name = var.ecs_cluster_name
}

resource "aws_ecs_task_definition" "api" {
  family                   = var.ecs_service_name
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.ecs_task_cpu
  memory                   = var.ecs_task_memory
  execution_role_arn       = aws_iam_role.ecs_task_execution.arn
  task_role_arn            = aws_iam_role.ecs_task.arn

  container_definitions = jsonencode([{
    name      = var.ecs_container_name
    image     = "${local.ecr_repository_url}:${var.ecs_bootstrap_image_tag}"
    essential = true

    portMappings = [{
      containerPort = var.container_port
      protocol      = "tcp"
    }]

    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.api.name
        "awslogs-region"        = local.effective_aws_region
        "awslogs-stream-prefix" = "ecs"
      }
    }

    environment = [
      {
        name  = "Guardrails__Provider"
        value = "Bedrock"
      },
      {
        name  = "Guardrails__Mode"
        value = "Shadow"
      },
      {
        name  = "Guardrails__Bedrock__GuardrailIdentifier"
        value = aws_bedrock_guardrail.api_safety.guardrail_id
      },
      {
        name  = "Guardrails__Bedrock__GuardrailVersion"
        value = aws_bedrock_guardrail_version.api_safety.version
      }
    ]
  }])

  lifecycle {
    # Deploy workflow registers new task definition revisions with updated image tags.
    ignore_changes = [container_definitions]
  }
}

resource "aws_ecs_service" "api" {
  name            = var.ecs_service_name
  cluster         = aws_ecs_cluster.api.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = aws_subnet.public[*].id
    security_groups  = [aws_security_group.ecs_tasks.id]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = var.ecs_container_name
    container_port   = var.container_port
  }

  # Rolling deployment: ECS keeps the old task running until the new one is healthy.
  deployment_minimum_healthy_percent = 100
  deployment_maximum_percent         = 200

  depends_on = [aws_lb_listener.http]

  lifecycle {
    # Deploy workflow updates the task definition out-of-band via register-task-definition.
    ignore_changes = [task_definition]
  }
}
