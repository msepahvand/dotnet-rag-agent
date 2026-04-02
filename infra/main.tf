
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
    bucket  = "dotnet-vector-search-tf-state"
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

data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
}

resource "aws_security_group" "alb" {
  name        = "${var.ecs_service_name}-alb"
  description = "Allow HTTP inbound to the load balancer"
  vpc_id      = data.aws_vpc.default.id

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
  vpc_id      = data.aws_vpc.default.id

  ingress {
    from_port       = 8080
    to_port         = 8080
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
  subnets            = data.aws_subnets.default.ids
}

resource "aws_lb_target_group" "api" {
  name        = var.ecs_service_name
  port        = 8080
  protocol    = "HTTP"
  vpc_id      = data.aws_vpc.default.id
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

data "aws_iam_policy_document" "ecs_task_runtime" {
  statement {
    sid    = "AllowBedrockInvokeModel"
    effect = "Allow"
    actions = [
      "bedrock:InvokeModel"
    ]
    resources = [
      "arn:aws:bedrock:${local.effective_aws_region}::foundation-model/${var.embedding_model_id}"
    ]
  }

  statement {
    sid    = "AllowS3Vectors"
    effect = "Allow"
    actions = [
      "s3vectors:*"
    ]
    resources = ["*"]
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
      containerPort = 8080
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
    subnets          = data.aws_subnets.default.ids
    security_groups  = [aws_security_group.ecs_tasks.id]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = var.ecs_container_name
    container_port   = 8080
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
