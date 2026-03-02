output "role_arn" {
  description = "ARN of the IAM role that GitHub Actions should assume"
  value       = "arn:aws:iam::${data.aws_caller_identity.current.account_id}:role/${var.iam_role_name}"
}

output "vector_bucket_name" {
  description = "Configured S3 Vectors bucket name"
  value       = module.s3_vectors.vector_bucket_name
}

output "vector_index_name" {
  description = "Configured S3 Vectors index name"
  value       = module.s3_vectors.vector_index_name
}
