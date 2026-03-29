resource "aws_s3vectors_vector_bucket" "vector_bucket" {
  vector_bucket_name = var.vector_bucket_name
}

resource "aws_s3vectors_index" "vector_index" {
  vector_bucket_name = aws_s3vectors_vector_bucket.vector_bucket.vector_bucket_name
  index_name         = var.vector_index_name

  data_type       = var.data_type
  dimension       = var.vector_dimension
  distance_metric = var.distance_metric
}
