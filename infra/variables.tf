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
  description = "Name of the inline IAM policy on the GitHub Actions role"
  type        = string
  default     = "GitHubActionsECSDeployPolicy"
}

variable "embedding_model_id" {
  description = "Bedrock embedding model ID used by the application"
  type        = string
  default     = "amazon.titan-embed-text-v2:0"
}

variable "vector_bucket_name" {
  description = "S3 Vectors bucket name used by the application"
  type        = string
  default     = "posts-semantic-search"
}

variable "vector_index_name" {
  description = "S3 Vectors index name used by the application"
  type        = string
  default     = "posts-content-index"
}

variable "vector_dimension" {
  description = "Vector dimension for the S3 Vectors index"
  type        = number
  default     = 1024
}

variable "vector_distance_metric" {
  description = "Distance metric for the S3 Vectors index"
  type        = string
  default     = "cosine"
}

variable "vector_data_type" {
  description = "Vector data type for the S3 Vectors index"
  type        = string
  default     = "float32"
}

variable "ecr_repository_name" {
  description = "ECR repository name used for the API container image"
  type        = string
  default     = "dotnet-vector-search"
}

variable "ecs_cluster_name" {
  description = "ECS cluster name"
  type        = string
  default     = "dotnet-vector-search"
}

variable "ecs_service_name" {
  description = "ECS service name for the API"
  type        = string
  default     = "dotnet-vector-search"
}

variable "ecs_container_name" {
  description = "Name of the container within the ECS task definition"
  type        = string
  default     = "api"
}

variable "ecs_task_cpu" {
  description = "CPU units for the Fargate task (256 = 0.25 vCPU)"
  type        = number
  default     = 512
}

variable "ecs_task_memory" {
  description = "Memory (MiB) for the Fargate task"
  type        = number
  default     = 1024
}

variable "ecs_bootstrap_image_tag" {
  description = "Bootstrap image tag used by Terraform when creating the ECS task definition before CI deploys the real image"
  type        = string
  default     = "latest"
}
