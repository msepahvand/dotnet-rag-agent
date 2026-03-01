variable "tf_state_bucket_name" {
  description = "Name of the S3 bucket for Terraform state"
  type        = string
  default     = "dotnet-vector-search-tf-state"
}

variable "github_repo" {
  description = "GitHub repository in the form owner/name"
  type        = string
}

variable "github_branch" {
  description = "Optional branch pattern to restrict OIDC tokens (e.g. refs/heads/master)"
  type        = string
  default     = ""
}

variable "aws_region" {
  description = "AWS region to deploy into"
  type        = string
  default     = "us-east-1"
}

variable "github_oidc_url" {
  description = "GitHub OIDC provider URL"
  type        = string
  default     = "https://token.actions.githubusercontent.com"
}

variable "github_oidc_client_id" {
  description = "GitHub OIDC client ID (typically sts.amazonaws.com)"
  type        = string
  default     = "sts.amazonaws.com"
}

variable "github_oidc_thumbprint" {
  description = "GitHub OIDC provider thumbprint"
  type        = string
  default     = "6938fd4d98bab03faadb97b34396831e3780aea1"
}

variable "iam_role_name" {
  description = "Name of the IAM role for GitHub Actions"
  type        = string
  default     = "GitHubActionsDeployRole"
}

variable "iam_policy_name" {
  description = "Name of the inline IAM policy"
  type        = string
  default     = "GitHubActionsECRAppRunner"
}
