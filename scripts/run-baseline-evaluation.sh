#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASE_URL="${1:-http://localhost:5000}"
QUESTION_FILE="$ROOT_DIR/eval/questions.json"
OUTPUT_FILE="$ROOT_DIR/eval/baseline-sk.json"
API_URL="${BASE_URL%/}/api/agent/evaluate"

if ! command -v curl >/dev/null || ! command -v jq >/dev/null; then
  echo "This script requires curl and jq." >&2
  exit 1
fi

if [[ ! -s "$QUESTION_FILE" ]]; then
  echo "Missing $QUESTION_FILE. Run scripts/snapshot-hn.sh first." >&2
  exit 1
fi

QUESTION_COUNT="$(jq 'length' "$QUESTION_FILE")"
if (( QUESTION_COUNT < 40 || QUESTION_COUNT > 60 )); then
  echo "The evaluation set must contain 40–60 questions; found $QUESTION_COUNT." >&2
  exit 1
fi

mkdir -p "$ROOT_DIR/eval/runs"
TEMP_DIR="$(mktemp -d "$ROOT_DIR/eval/runs/baseline.XXXXXX")"
trap 'rm -rf "$TEMP_DIR"' EXIT
jq -n --slurpfile questions "$QUESTION_FILE" \
  '{questions: $questions[0], topK: 5}' > "$TEMP_DIR/request.json"

for run in 1 2 3 4 5; do
  echo "Running baseline pass $run of 5..."
  curl --fail --silent --show-error \
    --header "Content-Type: application/json" \
    --data-binary "@$TEMP_DIR/request.json" \
    "$API_URL" \
    | jq -c '{
        runAt: .runAt,
        totalQuestions: .totalQuestions,
        groundednessRate: .groundednessRate,
        citationValidityRate: .citationValidityRate,
        hitAtKRate: .hitAtKRate,
        averageIterations: .averageIterations,
        averageLatencyMs: .averageLatencyMs,
        p50LatencyMs: .p50LatencyMs,
        p95LatencyMs: .p95LatencyMs,
        averageJudgedGroundednessScore: .averageJudgedGroundednessScore,
        averageJudgedRelevanceScore: .averageJudgedRelevanceScore
      }' > "$TEMP_DIR/run-$run.json"
done

jq -s --arg commit "$(git -C "$ROOT_DIR" rev-parse HEAD)" \
  --arg model "${EVALUATION_MODEL_ID:-unspecified}" \
  --arg capturedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" '
  def summary($key):
    [ .[] | .[$key] | select(type == "number") ] as $values
    | ($values | length) as $count
    | if $count == 0 then null else
        ($values | add / $count) as $mean
        | (if $count > 1 then
             (($values | map((. - $mean) * (. - $mean)) | add) / ($count - 1) | sqrt)
           else 0 end) as $sampleDeviation
        | ($sampleDeviation * 2.776445 / sqrt($count)) as $margin
        | {mean: $mean, ci95: {lower: ($mean - $margin), upper: ($mean + $margin)}}
      end;
  {
    baseline: "semantic-kernel",
    capturedAtUtc: $capturedAt,
    sourceCommit: $commit,
    modelId: $model,
    runCount: length,
    questionCount: (.[0].totalQuestions),
    metrics: {
      groundednessRate: summary("groundednessRate"),
      citationValidityRate: summary("citationValidityRate"),
      hitAtKRate: summary("hitAtKRate"),
      averageIterations: summary("averageIterations"),
      averageLatencyMs: summary("averageLatencyMs"),
      p50LatencyMs: summary("p50LatencyMs"),
      p95LatencyMs: summary("p95LatencyMs"),
      averageJudgedGroundednessScore: summary("averageJudgedGroundednessScore"),
      averageJudgedRelevanceScore: summary("averageJudgedRelevanceScore")
    },
    runs: map({runAt: .runAt, totalQuestions: .totalQuestions})
  }' "$TEMP_DIR"/run-*.json > "$OUTPUT_FILE"

echo "Wrote aggregate baseline metrics to $OUTPUT_FILE."
