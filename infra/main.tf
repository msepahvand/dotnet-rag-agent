
provider "aws" {
  region = var.aws_region
}

# S3 bucket for Terraform state
resource "aws_s3_bucket" "tf_state" {
  bucket = var.tf_state_bucket_name
  force_destroy = true
}

terraform {
  backend "s3" {
    bucket = "${var.tf_state_bucket_name}"
    key    = "global/terraform.tfstate"
    region = var.aws_region
    encrypt = true
  }
}

data "aws_caller_identity" "current" {}

resource "aws_iam_openid_connect_provider" "github" {
  url             = var.github_oidc_url
  client_id_list  = [var.github_oidc_client_id]
  thumbprint_list = [var.github_oidc_thumbprint]
}

# Policy document for trust relationship
data "aws_iam_policy_document" "github_assume" {
  statement {
    effect = "Allow"

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github.arn]
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

# Inline permissions policy
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
        ,"iam:CreateOpenIDConnectProvider"
    ]
    resources = ["*"]
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
