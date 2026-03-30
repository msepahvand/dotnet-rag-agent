
provider "aws" {
  region = local.effective_aws_region
}

terraform {
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 6.34.0, < 7.0.0"
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

