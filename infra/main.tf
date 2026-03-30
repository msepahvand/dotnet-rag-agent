
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
  effective_aws_region     = trimspace(var.aws_region) != "" ? var.aws_region : "us-east-1"
  github_oidc_provider_arn = "arn:aws:iam::${data.aws_caller_identity.current.account_id}:oidc-provider/${replace(var.github_oidc_url, "https://", "")}"
  ecr_repository_url        = "${data.aws_caller_identity.current.account_id}.dkr.ecr.${local.effective_aws_region}.amazonaws.com/${var.ecr_repository_name}"
}

data "aws_iam_role" "apprunner_ecr_access" {
  name = var.apprunner_ecr_access_role_name
}

data "aws_iam_role" "apprunner_instance" {
  name = var.apprunner_instance_role_name
}

data "aws_iam_policy_document" "apprunner_instance_runtime" {
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

resource "aws_iam_role_policy" "apprunner_instance_runtime" {
  name   = var.apprunner_instance_policy_name
  role   = var.apprunner_instance_role_name
  policy = data.aws_iam_policy_document.apprunner_instance_runtime.json
}

module "s3_vectors" {
  source = "./modules/s3_vectors"

  aws_region         = local.effective_aws_region
  vector_bucket_name = var.vector_bucket_name
  vector_index_name  = var.vector_index_name
  vector_dimension   = var.vector_dimension
  distance_metric    = var.vector_distance_metric
  data_type          = var.vector_data_type
}

resource "aws_ecr_repository" "api" {
  name                 = var.ecr_repository_name
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_apprunner_service" "api" {
  service_name = var.apprunner_service_name

  source_configuration {
    auto_deployments_enabled = false

    authentication_configuration {
      access_role_arn = data.aws_iam_role.apprunner_ecr_access.arn
    }

    image_repository {
      image_identifier      = "${local.ecr_repository_url}:${var.apprunner_bootstrap_image_tag}"
      image_repository_type = "ECR"

      image_configuration {
        port = "8080"
      }
    }
  }

  instance_configuration {
    instance_role_arn = data.aws_iam_role.apprunner_instance.arn
  }

  lifecycle {
    # Deploy workflow updates image tags out-of-band; keep Terraform authoritative for service existence/config.
    ignore_changes = [
      source_configuration[0].image_repository[0].image_identifier
    ]
  }
}

