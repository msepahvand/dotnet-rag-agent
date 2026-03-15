output "vector_bucket_name" {
  description = "S3 Vectors bucket name"
  value       = var.vector_bucket_name
}

output "vector_index_name" {
  description = "S3 Vectors index name"
  value       = var.vector_index_name
}

output "vector_dimension" {
  description = "S3 Vectors index dimension"
  value       = var.vector_dimension
}

output "distance_metric" {
  description = "S3 Vectors index distance metric"
  value       = var.distance_metric
}

output "data_type" {
  description = "S3 Vectors index data type"
  value       = var.data_type
}
