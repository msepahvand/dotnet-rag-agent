# Evaluation baseline

The baseline uses a deterministic snapshot of Hacker News stories and 50 title-grounded questions. The corpus and question file are local-only: they contain third-party story content and are excluded from Git. The committed baseline report contains aggregate metrics and timestamps, not story text or generated answers.

## Create the local frozen corpus

Run from the repository root in Bash with `curl` and `jq` installed:

```bash
scripts/snapshot-hn.sh
```

This downloads up to 200 current top stories to `eval/corpus.json` and creates 50 questions in `eval/questions.json`. Keep these files unchanged for the five baseline passes. Capture a new snapshot only when intentionally establishing a new baseline.

## Run an isolated baseline

Use a dedicated Qdrant collection named `rag-agent-eval`, not a production vector index. Start Qdrant locally, then run the API with a snapshot data source, startup indexing enabled, periodic ingestion disabled, and quality judges enabled:

```bash
export DataSource__Provider=Snapshot
export DataSource__SnapshotPath="$(pwd)/eval/corpus.json"
export Ingestion__IndexOnStartup=true
export Ingestion__Enabled=false
export VectorStore__Provider=Qdrant
export VectorStore__Qdrant__Url=http://localhost:6333
export VectorStore__Qdrant__CollectionName=rag-agent-eval
export VectorStore__Qdrant__VectorSize=1024
export Evaluation__EnableQualityJudges=true
export Agent__Temperature=0
dotnet run --project RagAgent.Api
```

The API startup indexing seeds the local evaluation collection from the frozen snapshot. Do not point this run at a production vector bucket, index or collection. Bedrock chat and embedding calls still use AWS credentials and incur AWS charges.

In another terminal, run:

```bash
EVALUATION_MODEL_ID=us.anthropic.claude-sonnet-4-6 scripts/run-baseline-evaluation.sh
```

The runner calls the evaluation endpoint five times in the same API process, so exact judge inputs are cached across passes. It writes aggregate means and 95% t-confidence intervals to `eval/baseline-sk.json`; no question text or answer content is saved in that report. Review the report before adding it to version control. The confidence interval summarizes five whole-question-set runs; it does not correct for a biased or unrepresentative question set.

Quality evaluators are tuned for GPT-4o-class models. Treat Claude-as-judge results as relative comparisons using the same model and settings, not as absolute quality scores.
