# Final: feature list

RagSystem, the NDC API docs assistant (a rebuild of the old RagDocs). This file is the shared checklist: we both work from it.

- Old code, for reference only: `D:\RAG\DotNet` (spec: `D:\RAG\DotNet\docs\PLAN.md`). We never edit it.
- Input files to ingest: `D:\RAG\DotNet\content\` and `D:\RAG\DotNet\resources\*\*.swagger.json`.

## Working rules

1. **One feature at a time.** Claude writes it, explains it in plain words, and stops. Nothing else starts until you say "approved".
2. **Order is by dependency.** A feature only uses features above it. If a feature needs something not built yet, we move it down instead.
3. **Small.** One class = one job. One file = one idea. If a file grows past ~150 lines, we split it.
4. **Few projects.** One web project plus one test project. Folders are by feature, not by technical layer.
5. **No interface unless there are two real implementations.** No settings-as-regex. No new NuGet package without asking (approved list: PLAN.md section 3).
6. **Every feature ships with its test** and a "how to see it working" step, so you can check it yourself.
7. **Status:** `todo` -> `in review` (written, waiting for you) -> `done` (you approved) or `redo` (you want changes). `excluded` = we decided not to build it for now; it is listed in `EXCLUDED.md` and we re-check it later.

---

## A. Foundation

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F01 | Solution scaffold | none | `RagSystem.sln`, one web project `RagSystem`, one test project `RagSystem.Tests`, pinned package versions, `dotnet build` and `dotnet test` pass | done |
| F02 | Token estimate | F01 | Token estimate (`chars / 3.5`, rounded up). Used by chunking, embedding and limits | in review |

## B. Reading documents (pure code, no database)

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F03 | Ingestion models | F01 | The shapes passed between steps: issue (warning/error), section draft, chunk draft, chunk types, ingestion result | todo |
| F04 | Front matter reader | F03 | Reads the YAML header (`version`, `title`, `docType`, `updated`). Errors: missing header, missing field, version differs from the upload's version | todo |
| F05 | Markdown section parser | F03 | Splits the body by headings into a section tree. Each section holds blocks: paragraph, table, quote, list. Code blocks are dropped with a warning | todo |
| F06 | Section classifier | F05 | Gives each section a chunk type. First match wins: route line = endpoint, workflow table = workflow, `Q1.` heading = faq, chapter title = data_model / enum / errors / glossary, else guide. Child sections inherit the route | todo |
| F07 | Block splitter | F02, F05 | Packs blocks into pieces under the token limit (500; workflow 1200). Tables split by row | todo |
| F08 | Entity finder | F03 | Collects names (operations, models, fields, enum values) from a file and finds them inside chunk text | todo |
| F09 | Markdown chunker | F04, F06, F07, F08 | Puts F04 to F08 together: sections become chunks with breadcrumb, route/flow lead line, entities and extra data | todo |
| F10 | OpenAPI chunker | F02, F03, F08 | Reads a swagger JSON file: an overview chunk, plus one spec chunk per operation (request fields, depth limit 4) | todo |
| F11 | Document processor | F09, F10 | Picks the chunker by extension (`.md`, `.json`), rejects other types (415) | todo |
| F12 | Dry-run endpoint | F11 | `POST /api/documents?version=v3&dryRun=true` returns the chunks and warnings without storing. **Review checkpoint:** we read the real chunks of `content/` together before any database work | todo |

## C. Storing documents

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F13 | Database setup | F01 | Postgres + pgvector in a compose file, EF Core context, migration runs, `/health` checks the database | todo |
| F14 | Document tables | F13 | Tables `documents`, `sections`, `chunks` (vector column 1,024), `corpus_state`, with status (no content hash) | todo |
| F15 | Known entities provider | F14, F08 | Reads the entity names already stored for a version, so a new file can find names defined in other files | todo |
| F16 | Ollama embedding client | F01 | Calls Ollama `/api/embed` over `HttpClient`, checks the vector size | todo |
| F17 | Batch embedder | F02, F16 | Splits texts into batches (32 items / 6,000 tokens), retries rate-limit errors, normalises vectors | todo |
| F18 | Submit document | F11, F14, F15 | Validates, refuses a file still Processing, creates the document row as Processing. (No "unchanged file" skip: see EXCLUDED.md) | todo |
| F19 | Queue and worker | F17, F18 | In-memory queue and background worker. Replaces old chunks in one transaction, bumps the corpus revision, marks Failed on error. Interrupted jobs are marked Failed at startup. (No reuse of unchanged chunks: see EXCLUDED.md) | todo |
| F20 | Document endpoints | F12, F18, F19 | Upload (200 / 202 / 409 / 413 / 415 / 422), list, get, chunks, delete | todo |

## D. Retrieval

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F21 | Query embedder | F16 | Embeds a question with the Qwen3 instruct prefix | todo |
| F22 | Vector search | F14, F21 | Top 20 chunks of a version by cosine distance, entity matches ranked first | todo |
| F23 | Result post-processing | F22 | Minimum score, near-duplicate removal, at most 2 workflow chunks, expand to the whole section when 2+ chunks hit, document order, 5,000-token budget | todo |

## E. Chat core (endpoints are open until section F)

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F24 | Ollama chat client | F01 | Calls Ollama `/api/chat` (`think: false`, `num_ctx`, `num_predict`), returns text and token counts | todo |
| F25 | Generation gate | F24 | One generation at a time (semaphore); waiting counts against the timeout | todo |
| F26 | Prompt builder | F23 | System rules + documentation context + question; documentation is marked as data | todo |
| F27 | Error mapping | F24 | Ollama failures become problem+json: 503 (down, loading, busy, timeout) with `Retry-After`, 502 (model missing) | todo |
| F28 | Basic chat endpoint | F23, F25, F26, F27 | `POST /api/{version}/chat`: retrieve, answer, citations. No relevant chunks = "not covered" without calling the model | todo |
| F29 | Conversations and history | F28, F14 | Tables `conversations`, `messages`; saves each turn; loads up to 6 turns / 2,000 tokens; 404 for a foreign conversation | todo |
| F30 | Query rewrite | F25, F29 | Follow-up question becomes a standalone question (10 s timeout, fallback: previous question + current) | todo |

## F. Security and limits

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F31 | API keys and clients | F14 | Table `clients` (hashed keys), `POST/GET/DELETE /api/clients`, key shown once, admin key protects admin endpoints and client key protects chat | todo |
| F32 | Request rate limit | F31 | Requests per minute per client, 429 with `Retry-After` | todo |
| F33 | Token budget | F02, F31, F28 | Per-client token bucket: reserve before the call, settle with actual usage, refund on failure, `X-Token-Remaining` header | todo |
| F34 | Project caps | F33 | Optional per-role caps (chat, rewrite, embedding), off by default | todo |
| F35 | Input guard | F28 | Blocks prompt injection (400), redacts PII (email, phone, card, passport) before the model and before storage | todo |
| F36 | Output guard | F28 | Redacts PII in the answer, detects system-prompt leaks | todo |
| F37 | Semantic cache | F21, F14, F30 | Table `semantic_cache`; hit when same version + corpus revision + similarity above threshold; written only for grounded answers without PII | todo |

## G. Delivery

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F38 | Streaming chat | F28, F36 | `POST /api/{version}/chat/stream` (SSE: `delta`, `done`, `error`), with the hold-back filter | todo |
| F39 | Logging and correlation id | F01 | `X-Correlation-Id` on every request, in log scope and spans, stage prefixes, no raw user text | todo |
| F40 | Telemetry | F39 | OpenTelemetry traces, metrics, logs to the Aspire Dashboard; LLM spans to Langfuse | todo |
| F41 | Docker | F20, F28 | Dockerfile, full compose (api, postgres, ollama, model pull, dashboard), GPU override, Langfuse compose, ingest script | todo |
| F42 | Web front end | F38, F31, F20 | Angular app: client chat, admin documents and clients | todo |

---

## Rules for changing this file

- Claude updates the **Status** column as we go. Nothing is `done` until you say so.
- To add, split, merge or reorder features, we edit this file first, then build.
- Notes and decisions from reviews go in the section below, newest first.

## Review log

| Date | Feature | Decision / change you asked for |
|---|---|---|
| 2026-11-16 | F02, F14, F18, F19 | No content hash and no "unchanged text" check: removed from F02 (now only the token estimate), F14, F18 and F19. F18 and F19 themselves stay. Token estimate `chars / 3.5` kept. See `EXCLUDED.md`. |
| 2026-11-16 | F01 | New project name is `RagSystem` (not `RagDocs`). Target `net9.0` (installed SDK 9.0.308) instead of .NET 10. Packages may be added as each feature needs them. |
