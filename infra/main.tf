
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
