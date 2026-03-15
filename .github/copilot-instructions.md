- For commit messages, use Use chore:, feat:, fix:, docs commit prefixes

- chore: for changes that don't modify src or test files, such as updating build scripts, package.json, etc.

- feat: for new features or enhancements to existing features.

- fix: for bug fixes.

- docs: for documentation changes, such as updating README.md or adding comments to code.

- Next integrations roadmap:

- Add a single-tool-calling agent first: user question → agent decides to run semantic search → returns grounded answer with sources.

- Wire Bedrock text generation into the same endpoint so the final answer is LLM-generated while still grounded on retrieved sources.

- Add a second ingestion agent: watches new docs, chunks them, embeds them, and updates the vector store automatically.

- Add an evaluation agent: runs a test question set and scores retrieval + answer quality (hit@k, groundedness, hallucination rate).