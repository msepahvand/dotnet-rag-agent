variable "aws_region" {
  description = "AWS region where S3 Vectors resources are created"
  type        = string
}

variable "vector_bucket_name" {
  description = "S3 Vectors bucket name"
  type        = string
}

variable "vector_index_name" {
  description = "S3 Vectors index name"
  type        = string
}

variable "vector_dimension" {
  description = "Vector index dimension"
  type        = number
}

variable "distance_metric" {
  description = "Distance metric for S3 Vectors index"
  type        = string
}

variable "data_type" {
  description = "Vector data type"
  type        = string
}
