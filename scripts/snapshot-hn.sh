#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT_DIR="$ROOT_DIR/eval"
HN_API="https://hacker-news.firebaseio.com/v0"
STORY_LIMIT="${HN_SNAPSHOT_STORY_LIMIT:-200}"
QUESTION_LIMIT="${HN_EVALUATION_QUESTION_LIMIT:-50}"

if ! command -v curl >/dev/null || ! command -v jq >/dev/null; then
  echo "This script requires curl and jq." >&2
  exit 1
fi

if (( STORY_LIMIT < QUESTION_LIMIT || QUESTION_LIMIT < 40 || QUESTION_LIMIT > 60 )); then
  echo "Use 40–60 questions and a snapshot at least as large as the question set." >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"
TEMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TEMP_DIR"' EXIT
mkdir -p "$TEMP_DIR/stories"

curl --fail --silent --show-error "$HN_API/topstories.json" \
  | jq ".[0:$STORY_LIMIT]" > "$TEMP_DIR/story-ids.json"

jq -r '.[]' "$TEMP_DIR/story-ids.json" \
  | xargs -P 8 -I '{}' sh -c '
      set -e
      story_id="$1"
      output_dir="$2"
      curl --fail --silent --show-error "'"$HN_API"'/item/${story_id}.json" \
        | jq -c --argjson id "$story_id" \
            "select(.type == \"story\" and (.title | type == \"string\")) |
             {id: \$id, userId: 0, title: .title,
              body: ((.text // (if .url then \"Source URL: \" + .url
                                else \"No story text was provided for this item.\" end))
                     | gsub(\"<[^>]*>\"; \" \"))}" \
        > "$output_dir/$story_id.json"
    ' _ '{}' "$TEMP_DIR/stories"

jq -s 'map(select(. != null)) | sort_by(.id)' "$TEMP_DIR"/stories/*.json \
  > "$TEMP_DIR/corpus.json"

POST_COUNT="$(jq 'length' "$TEMP_DIR/corpus.json")"
if (( POST_COUNT < QUESTION_LIMIT )); then
  echo "Only $POST_COUNT stories were retrieved; at least $QUESTION_LIMIT are required." >&2
  exit 1
fi

jq --argjson count "$QUESTION_LIMIT" \
  '.[0:$count] | map({
    question: ("What is the main topic of the Hacker News story titled: \"" + .title + "\"?"),
    expectedPostIds: [.id]
  })' "$TEMP_DIR/corpus.json" > "$TEMP_DIR/questions.json"

mv "$TEMP_DIR/corpus.json" "$OUTPUT_DIR/corpus.json"
mv "$TEMP_DIR/questions.json" "$OUTPUT_DIR/questions.json"
echo "Created a local snapshot of $POST_COUNT stories and $QUESTION_LIMIT evaluation questions."
echo "Story content and questions are git-ignored and must not be committed."
