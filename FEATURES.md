# Final: feature list

RagSystem, the NDC API docs assistant (a rebuild of the old RagDocs). This file is the shared checklist: we both work from it. **The rules behind each feature are in `FEATURE-DETAILS.md`.** What we decided not to build is in `EXCLUDED.md`.

- Old code, for reference only: `D:\RAG\DotNet` (spec: `D:\RAG\DotNet\docs\PLAN.md`). We never edit it.
- Input files to ingest: `D:\RAG\DotNet\content\` and `D:\RAG\DotNet\resources\*\*.swagger.json`.

## Decisions so far

- **Project:** `RagSystem`, .NET `net9.0` (the installed SDK), one web project and one test project, folders by feature.
- **Chunking is not our own code.** Markdown and OpenAPI text is cut by Semantic Kernel's `TextChunker` (package `Microsoft.SemanticKernel.Core`) and stored with its vector exactly as cut. Size, line size and overlap are in the `Chunking` section of `appsettings.json` (500, 500, 100).
- **OpenAPI files** are read with `Microsoft.OpenApi`. Each operation and each component schema becomes its own text, which is cut by the same chunker.
- **Not built:** content hashes and "unchanged" checks, our own chunking, entity ranking, chunk types, sections, source and doc types. See `EXCLUDED.md`.
- **Small choices:** token estimate is `chars / 3.5`; the file header is read by our own small parser (no YAML package); packages are added as a feature needs them, and anything outside the old approved list (PLAN.md section 3) is asked first.

## Working rules

1. **One feature at a time.** Claude writes it, explains it in plain words, and stops. Nothing else starts until you say "approved".
2. **Order is by dependency.** A feature only uses features above it.
3. **Small.** One class = one job. One file = one idea. If a file grows past ~150 lines, we split it.
4. **No interface unless there are two real implementations.** No settings-as-regex.
5. **Every feature ships with its test** and a "how to see it working" step, so you can check it yourself.
6. **Status has three values:** `todo`, `in review` (written, waiting for you) and `done` (you approved). Changes you ask for are made while the feature is `in review`.
7. **Before coding a feature,** Claude reads its section in `FEATURE-DETAILS.md` and the old files listed there, then says in 2-3 lines what it will build.

---

## A. Foundation

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F01 | Solution scaffold | none | `RagSystem.sln`, one web project `RagSystem`, one test project `RagSystem.Tests`, pinned package versions, `dotnet build` and `dotnet test` pass | done |
| F02 | Token estimate | F01 | Token estimate (`chars / 3.5`, rounded up). Used by chunking, embedding and limits | done |
| F03 | Problem responses | F01 | One global error handler and helper that return `application/problem+json` with `status`, `title`, `detail`, `reason` (and `correlationId` once F37 exists) | todo |

## B. Reading documents (pure code, no database)

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F04 | Ingestion models | F01 | The shapes passed between steps: issue (severity, code, message, line), chunk (position + text exactly as cut), ingestion result (file name, version, title, issues, chunks) | done |
| F05 | Front matter reader | F04 | Reads the file header (`version`, `title`, `docType`, `updated`, optional `source`) with our own small parser. Errors: missing/unterminated header, missing field, bad docType, version differs from the upload's version | done |
| F06 | Markdown chunker | F02, F04, F05 | Reads the header (F05), then cuts the body with Semantic Kernel's `TextChunker` (`SplitMarkDownLines`, then `SplitMarkdownParagraphs`), with F02 as the token counter. Chunk size, line size and overlap come from `appsettings.json` (500, 500, 100); a wrong value stops the app at start. Chunks are kept exactly as cut. Adds package `Microsoft.SemanticKernel.Core` | done |
| F07 | OpenAPI chunker | F02, F04, F06 | Reads the swagger JSON with `Microsoft.OpenApi`. Each operation and each component schema is written back as its own JSON text with `$ref`s kept, then cut with the same `TextChunker` if over the limit, using the route or the schema name as the chunk header. One check: every path starts with `/api/V{n}/` for the upload's version | todo |
| F08 | Document processor | F06, F07 | Picks the chunker by extension (`.md`, `.json`), rejects other types (415) | todo |
| F09 | Dry-run endpoint | F08, F03 | `POST /api/documents?version=v3&dryRun=true` returns the report (errors, warnings) and the chunks, without storing. **Review checkpoint:** we read the real chunks of `content/` together before any database work | todo |

## C. Storing documents

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F10 | Database setup | F01 | Postgres + pgvector in a compose file, EF Core context, migration runs (`MigrateOnStartup`), `/health` checks the database | todo |
| F11 | Document tables | F10 | Tables `documents` (with title and status), `chunks` (text, position, vector column 1,024), `corpus_state` | todo |
| F12 | Ollama embedding client | F01 | Calls Ollama `/api/embed` over `HttpClient`, checks the vector size (1,024), classifies failures (fatal / rate-limited / transient) | todo |
| F13 | Batch embedder | F02, F12 | Splits texts into batches (32 items / 6,000 tokens), retries only the failed batch with back-off, normalises vectors | todo |
| F14 | Submit document | F08, F11 | Validates, refuses a file still Processing (409), creates the document row as Processing, enqueues. Same version + file name replaces | todo |
| F15 | Queue and worker | F13, F14 | In-memory queue and background worker. Embeds the chunks as they are, replaces old chunks in one transaction, bumps the corpus revision, marks Failed on error. Interrupted jobs are marked Failed at startup | todo |
| F16 | Document endpoints | F09, F14, F15 | Upload (200 / 202 / 409 / 413 over 10 MB / 415 / 422), list, get, chunks (paged), delete | todo |

## D. Retrieval

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F17 | Query embedder | F12 | Embeds a question with the Qwen3 instruct prefix (documents have none) | todo |
| F18 | Vector search | F11, F17 | Top 20 chunks of a version by cosine distance | todo |
| F19 | Result post-processing | F18 | Minimum score, near-duplicate removal, document order, 5,000-token budget, labels S1, S2... | todo |

## E. Chat core (endpoints are open until section F)

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F20 | Ollama chat client | F12 | Calls Ollama `/api/chat` (`think: false`, `num_ctx`, `num_predict`, temperature, keep_alive), returns text, token counts and whether it was cut off | todo |
| F21 | Generation gate | F20 | One generation at a time (semaphore) shared by chat and rewrite; waiting counts against the timeout | todo |
| F22 | Prompt builder | F19 | System rules + documentation context + question; documentation is marked as data; asks the model to end with `Sources: S1, S3` | todo |
| F23 | Answer parser | F22 | Splits off the `Sources:` line, builds citations from the sources named (document title + part number), handles `none`, missing line (cite first 3, never cache) and empty answer (502) | todo |
| F24 | Error mapping | F20, F03 | Ollama failures become problem+json: 503 `llm_unavailable` (down, loading, busy, timeout) with `Retry-After`, 502 `llm_error` (model missing) | todo |
| F25 | Basic chat endpoint | F19, F21, F22, F23, F24 | `POST /api/{version}/chat`: validate, retrieve, answer, citations. No relevant chunks = "not covered" without calling the model | todo |
| F26 | Conversations and history | F25, F11 | Tables `conversations`, `messages`; saves each turn; loads up to 6 turns / 2,000 tokens; 404 for a foreign conversation; `GET /api/{version}/conversations/{id}/messages` | todo |
| F27 | Query rewrite | F21, F26 | Follow-up question becomes a standalone question (last 3 turns, 10 s timeout, fallback: previous question + current) | todo |
| F28 | Retrieval tuning | F25 | Re-measure the minimum score and duplicate threshold on real Qwen3 scores; check answer quality on real questions; if chunks lack context, revisit chunk size, overlap and chunk header | todo |

## F. Security and limits

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F29 | API keys and clients | F11, F03 | Table `clients` (hashed keys, SHA-256), `POST/GET/DELETE /api/clients`, key shown once, admin seeded from `Admin__ApiKey`, admin key protects documents and clients, client key protects chat | todo |
| F30 | Request rate limit | F29 | Requests per minute per client, 429 with `Retry-After` | todo |
| F31 | Token budget | F02, F29, F25 | Per-client token bucket: reserve before the call, settle with actual usage, refund on failure, `X-Token-Remaining` header; also covers the rewrite call | todo |
| F32 | Project caps | F31 | Optional per-role caps (chat, rewrite, embedding), off by default | todo |
| F33 | Input guard | F25 | Blocks prompt injection (400), redacts PII (email, phone, Luhn-valid card, passport) before the model and before storage | todo |
| F34 | Output guard | F25 | Redacts PII in the answer, detects system-prompt leaks (canary + 8-word runs) | todo |
| F35 | Semantic cache | F17, F11, F27 | Table `semantic_cache`; hit when same version + corpus revision + similarity above threshold; written only for grounded answers without PII | todo |

## G. Delivery

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F36 | Streaming chat | F25, F34 | `POST /api/{version}/chat/stream` (SSE: `delta`, `done`, `error`), with the 80-character hold-back filter | todo |
| F37 | Logging and correlation id | F01 | `X-Correlation-Id` on every request, in log scope, spans and error bodies, stage prefixes, candidate table for retrieval, no raw user text | todo |
| F38 | Telemetry | F37 | OpenTelemetry traces, metrics and logs to the Aspire Dashboard, with the custom pipeline spans and `rag.*` metrics | todo |
| F39 | Langfuse exporter | F38 | LLM spans only to Langfuse (HTTP, filtered, session and user on every span); chat keeps working if Langfuse is down | todo |
| F40 | Docker | F16, F25 | Dockerfile, full compose (api, postgres, ollama, model pull, dashboard), GPU override, Langfuse compose, ingest script | todo |
| F41 | Web front end | F36, F29, F16 | Angular app: client chat, admin documents and clients. Large: split into parts when we reach it (see details) | todo |
| F42 | Own API docs | F16 | `/openapi/v1.json` and Swagger UI, only when enabled by a setting | todo |
| F43 | Project docs | F16 | README (run, test, ingest) and short upload rules (front matter fields, accepted file types) | todo |
