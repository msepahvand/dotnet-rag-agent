
provider "aws" {
  region = var.aws_region
}

terraform {
  backend "s3" {
    bucket  = "dotnet-vector-search-tf-state"
    key     = "global/terraform.tfstate"
    region  = "us-east-1"
    encrypt = true
  }
}

data "aws_caller_identity" "current" {}

locals {
  github_oidc_provider_arn = "arn:aws:iam::${data.aws_caller_identity.current.account_id}:oidc-provider/${replace(var.github_oidc_url, "https://", "")}"
}

data "aws_iam_policy_document" "github_assume" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [local.github_oidc_provider_arn]
    }

    condition {
      test     = "StringLike"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repo}*"]
    }
  }
}

resource "aws_iam_role" "github_actions" {
  name               = var.iam_role_name
  assume_role_policy = data.aws_iam_policy_document.github_assume.json
}

data "aws_iam_policy_document" "github_policy" {
  statement {
    effect = "Allow"
    actions = [
      "ecr:BatchCheckLayerAvailability",
      "ecr:CompleteLayerUpload",
      "ecr:CreateRepository",
      "ecr:DescribeRepositories",
      "ecr:GetAuthorizationToken",
      "ecr:InitiateLayerUpload",
      "ecr:ListImages",
      "ecr:PutImage",
      "ecr:UploadLayerPart"
    ]
    resources = ["*"]
  }

  statement {
    effect = "Allow"
    actions = [
      "apprunner:CreateService",
      "apprunner:DescribeService",
      "apprunner:ListServices",
      "apprunner:UpdateService"
    ]
    resources = ["arn:aws:apprunner:${var.aws_region}:${data.aws_caller_identity.current.account_id}:service/*"]
  }

  statement {
    effect = "Allow"
    actions = [
      "iam:PassRole"
    ]
    resources = ["*"]

    condition {
      test     = "StringLike"
      variable = "iam:PassedToService"
      values = [
        "build.apprunner.amazonaws.com",
        "tasks.apprunner.amazonaws.com"
      ]
    }
  }
}

resource "aws_iam_role_policy" "github_actions_policy" {
  name   = var.iam_policy_name
  role   = aws_iam_role.github_actions.id
  policy = data.aws_iam_policy_document.github_policy.json
}
