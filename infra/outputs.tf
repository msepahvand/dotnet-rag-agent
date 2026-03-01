output "role_arn" {
  description = "ARN of the IAM role that GitHub Actions should assume"
  value       = "arn:aws:iam::${data.aws_caller_identity.current.account_id}:role/${var.iam_role_name}"
}
