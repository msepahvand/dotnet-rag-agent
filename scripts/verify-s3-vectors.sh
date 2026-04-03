#!/usr/bin/env bash
# Verifies that the S3 Vectors bucket and index created by Terraform actually exist.
# Exits non-zero if either resource is missing.
#
# Usage: verify-s3-vectors.sh <REGION> <VECTOR_BUCKET_NAME> <VECTOR_INDEX_NAME>
set -euo pipefail

REGION="$1"
VECTOR_BUCKET_NAME="$2"
VECTOR_INDEX_NAME="$3"

BUCKET_FOUND=$(aws s3vectors list-vector-buckets \
  --region "$REGION" \
  --query "vectorBuckets[?vectorBucketName=='$VECTOR_BUCKET_NAME'].vectorBucketName | [0]" \
  --output text)
if [ "$BUCKET_FOUND" = "None" ] || [ -z "$BUCKET_FOUND" ]; then
  echo "S3 Vectors bucket '$VECTOR_BUCKET_NAME' not found in '$REGION'." >&2
  exit 1
fi

INDEX_FOUND=$(aws s3vectors list-indexes \
  --region "$REGION" \
  --vector-bucket-name "$VECTOR_BUCKET_NAME" \
  --query "indexes[?indexName=='$VECTOR_INDEX_NAME'].indexName | [0]" \
  --output text)
if [ "$INDEX_FOUND" = "None" ] || [ -z "$INDEX_FOUND" ]; then
  echo "S3 Vectors index '$VECTOR_INDEX_NAME' not found in '$VECTOR_BUCKET_NAME'." >&2
  exit 1
fi

echo "S3 Vectors verification succeeded."
