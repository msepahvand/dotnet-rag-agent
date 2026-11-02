#!/usr/bin/env bash
set -euo pipefail

TAG_KEY="Application"
TAG_VALUE="dotnet-rag-agent"
PENDING_WINDOW_DAYS=7

mapfile -t regions < <(
  aws ec2 describe-regions \
    --all-regions \
    --query "Regions[?OptInStatus != 'not-opted-in'].RegionName" \
    --output text | tr '\t' '\n'
)

declare -a replica_keys=()
declare -a other_keys=()
declare -a primary_keys=()

for region in "${regions[@]}"; do
  while IFS= read -r key_id; do
    [ -n "$key_id" ] || continue

    key_metadata=$(aws kms describe-key \
      --region "$region" \
      --key-id "$key_id" \
      --query 'KeyMetadata.[KeyManager,MultiRegionConfiguration.MultiRegionKeyType]' \
      --output text)
    read -r key_manager key_type <<< "$key_metadata"
    [ "$key_manager" = "CUSTOMER" ] || continue

    matching_tags=$(aws kms list-resource-tags \
      --region "$region" \
      --key-id "$key_id" \
      --query "Tags[?TagKey=='$TAG_KEY' && TagValue=='$TAG_VALUE'] | length(@)" \
      --output text)
    [ "$matching_tags" = "1" ] || continue

    case "$key_type" in
      REPLICA) replica_keys+=("$region:$key_id") ;;
      PRIMARY) primary_keys+=("$region:$key_id") ;;
      *) other_keys+=("$region:$key_id") ;;
    esac
  done < <(
    aws kms list-keys \
      --region "$region" \
      --query 'Keys[].KeyId' \
      --output text | tr '\t' '\n'
  )
done

schedule_key_deletion() {
  local region="$1"
  local key_id="$2"
  local key_state

  key_state=$(aws kms describe-key \
    --region "$region" \
    --key-id "$key_id" \
    --query 'KeyMetadata.KeyState' \
    --output text)

  case "$key_state" in
    PendingDeletion|PendingReplicaDeletion)
      echo "KMS key $key_id in $region is already pending deletion."
      ;;
    Enabled|Disabled)
      aws kms schedule-key-deletion \
        --region "$region" \
        --key-id "$key_id" \
        --pending-window-in-days "$PENDING_WINDOW_DAYS" >/dev/null
      echo "Scheduled KMS key $key_id in $region for deletion in $PENDING_WINDOW_DAYS days."
      ;;
    *)
      echo "Cannot schedule KMS key $key_id in $region: unexpected state '$key_state'." >&2
      return 1
      ;;
  esac
}

for key in "${replica_keys[@]}" "${other_keys[@]}"; do
  [ -n "$key" ] || continue
  region="${key%%:*}"
  key_id="${key#*:}"
  schedule_key_deletion "$region" "$key_id"
done

deferred_primary_count=0
for key in "${primary_keys[@]}"; do
  region="${key%%:*}"
  key_id="${key#*:}"
  replica_count=$(aws kms describe-key \
    --region "$region" \
    --key-id "$key_id" \
    --query 'length(KeyMetadata.MultiRegionConfiguration.ReplicaKeys || `[]`)' \
    --output text)

  if [ "$replica_count" != "0" ]; then
    echo "Deferring primary KMS key $key_id in $region; it still has $replica_count replica(s)." >&2
    deferred_primary_count=$((deferred_primary_count + 1))
    continue
  fi

  schedule_key_deletion "$region" "$key_id"
done

if [ "$deferred_primary_count" -gt 0 ]; then
  echo "Replica keys must finish their 7-day deletion window before their primary can be scheduled. Re-run the destroy workflow after that window." >&2
  exit 1
fi

if [ "${#replica_keys[@]}" -eq 0 ] && [ "${#other_keys[@]}" -eq 0 ] && [ "${#primary_keys[@]}" -eq 0 ]; then
  echo "No customer-managed KMS keys tagged $TAG_KEY=$TAG_VALUE were found."
fi
