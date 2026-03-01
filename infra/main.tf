
provider "aws" {
  region = var.aws_region
}

terraform {
  backend "s3" {
    bucket = "dotnet-vector-search-tf-state"
    key    = "global/terraform.tfstate"
    region = "us-east-1"
    encrypt = true
  }
}

data "aws_caller_identity" "current" {}

locals {
  github_oidc_provider_arn = "arn:aws:iam::${data.aws_caller_identity.current.account_id}:oidc-provider/${replace(var.github_oidc_url, "https://", "")}" 
}

# Policy document for trust relationship
data "aws_iam_policy_document" "github_assume" {
  statement {
    effect = "Allow"

    principals {
      type        = "Federated"
      identifiers = [local.github_oidc_provider_arn]
    }

    actions = ["sts:AssumeRoleWithWebIdentity"]

    # The OIDC `sub` claim typically contains the repository and
    # may also include a ref suffix (e.g. "repo:owner/repo:ref:refs/heads/master").
    # Use StringLike with a wildcard so the exact format doesn't matter.
    condition {
      test     = "StringLike"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repo}*"]
    }

    dynamic "condition" {
      for_each = var.github_branch != "" ? [1] : []
      content {
        test     = "StringLike"
        variable = "token.actions.githubusercontent.com:ref"
        values   = [var.github_branch]
      }
    }
  }
}

resource "aws_iam_role" "github_actions" {
  name               = var.iam_role_name
  assume_role_policy = data.aws_iam_policy_document.github_assume.json
}

data "aws_iam_policy_document" "github_policy" {

  statement {
    effect    = "Allow"
    actions   = [
      "ecr:DescribeRepositories",
      "ecr:CreateRepository",
      "ecr:GetAuthorizationToken",
      "ecr:BatchCheckLayerAvailability",
      "ecr:PutImage",
      "ecr:InitiateLayerUpload",
      "ecr:UploadLayerPart",
      "ecr:CompleteLayerUpload",
      "ecr:ListImages"
    ]
    resources = ["*"]
  }

  statement {
    effect    = "Allow"
    actions   = ["s3:ListBucket"]
    resources = ["arn:aws:s3:::dotnet-vector-search-tf-state"]
  }

  statement {
    effect    = "Allow"
    actions   = ["s3:GetObject", "s3:PutObject"]
    resources = ["arn:aws:s3:::dotnet-vector-search-tf-state/*"]
  }

  statement {
    effect    = "Allow"
    actions   = ["apprunner:UpdateService"]
    resources = ["arn:aws:apprunner:${var.aws_region}:${data.aws_caller_identity.current.account_id}:service/*"]
  }
}

resource "aws_iam_role_policy" "github_actions_policy" {
  name   = var.iam_policy_name
  role   = aws_iam_role.github_actions.id
  policy = data.aws_iam_policy_document.github_policy.json
}
