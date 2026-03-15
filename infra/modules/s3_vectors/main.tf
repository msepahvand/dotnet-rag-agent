resource "terraform_data" "vector_bucket" {
  triggers_replace = [
    var.aws_region,
    var.vector_bucket_name
  ]

  provisioner "local-exec" {
    command = <<-EOT
      set -eu

      region="${var.aws_region}"
      if [ -z "$region" ]; then
        region="us-east-1"
      fi

      existing_bucket=$(aws s3vectors list-vector-buckets \
        --region "$region" \
        --query "vectorBuckets[?vectorBucketName=='${var.vector_bucket_name}'].vectorBucketName | [0]" \
        --output text)

      if [ "$existing_bucket" = "None" ] || [ -z "$existing_bucket" ]; then
        aws s3vectors create-vector-bucket \
          --region "$region" \
          --vector-bucket-name "${var.vector_bucket_name}" > /dev/null
      fi

      # Wait until bucket is visible to avoid eventual-consistency race in CI/CD.
      attempts=30
      delay_seconds=5
      found_bucket=""
      for i in $(seq 1 $attempts); do
        found_bucket=$(aws s3vectors list-vector-buckets \
          --region "$region" \
          --query "vectorBuckets[?vectorBucketName=='${var.vector_bucket_name}'].vectorBucketName | [0]" \
          --output text)

        if [ "$found_bucket" != "None" ] && [ -n "$found_bucket" ]; then
          break
        fi

        sleep $delay_seconds
      done

      if [ "$found_bucket" = "None" ] || [ -z "$found_bucket" ]; then
        echo "Timed out waiting for S3 Vectors bucket '${var.vector_bucket_name}' in region '$region'." >&2
        exit 1
      fi
    EOT
  }
}

resource "terraform_data" "vector_index" {
  depends_on = [terraform_data.vector_bucket]

  triggers_replace = [
    var.aws_region,
    var.vector_bucket_name,
    var.vector_index_name,
    tostring(var.vector_dimension),
    var.distance_metric,
    var.data_type
  ]

  provisioner "local-exec" {
    command = <<-EOT
      set -eu

      region="${var.aws_region}"
      if [ -z "$region" ]; then
        region="us-east-1"
      fi

      existing_index=$(aws s3vectors list-indexes \
        --region "$region" \
        --vector-bucket-name "${var.vector_bucket_name}" \
        --query "indexes[?indexName=='${var.vector_index_name}'].indexName | [0]" \
        --output text)

      if [ "$existing_index" = "None" ] || [ -z "$existing_index" ]; then
        aws s3vectors create-index \
          --region "$region" \
          --vector-bucket-name "${var.vector_bucket_name}" \
          --index-name "${var.vector_index_name}" \
          --data-type "${var.data_type}" \
          --dimension ${var.vector_dimension} \
          --distance-metric "${var.distance_metric}" > /dev/null
      fi

      # Wait until index is visible to avoid eventual-consistency race in CI/CD.
      attempts=30
      delay_seconds=5
      found_index=""
      for i in $(seq 1 $attempts); do
        found_index=$(aws s3vectors list-indexes \
          --region "$region" \
          --vector-bucket-name "${var.vector_bucket_name}" \
          --query "indexes[?indexName=='${var.vector_index_name}'].indexName | [0]" \
          --output text)

        if [ "$found_index" != "None" ] && [ -n "$found_index" ]; then
          break
        fi

        sleep $delay_seconds
      done

      if [ "$found_index" = "None" ] || [ -z "$found_index" ]; then
        echo "Timed out waiting for S3 Vectors index '${var.vector_index_name}' in bucket '${var.vector_bucket_name}'." >&2
        exit 1
      fi
    EOT
  }
}
