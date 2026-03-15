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

output "vector_dimension" {
  description = "Configured S3 Vectors index dimension"
  value       = module.s3_vectors.vector_dimension
}

output "vector_distance_metric" {
  description = "Configured S3 Vectors index distance metric"
  value       = module.s3_vectors.distance_metric
}

output "vector_data_type" {
  description = "Configured S3 Vectors index data type"
  value       = module.s3_vectors.data_type
}
