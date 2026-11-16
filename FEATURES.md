# Final: feature list

RagSystem, the NDC API docs assistant (a rebuild of the old RagDocs). This file is the shared checklist: we both work from it. **The rules behind each feature are in `FEATURE-DETAILS.md`.**

- Old code, for reference only: `D:\RAG\DotNet` (spec: `D:\RAG\DotNet\docs\PLAN.md`). We never edit it.
- Input files to ingest: `D:\RAG\DotNet\content\` and `D:\RAG\DotNet\resources\*\*.swagger.json`.
- Not rebuilt on purpose: `tools/refine-docs` (the script that made `content/`). We use `content/` as it is.
- **Chunking is not our own code.** Text is cut by Semantic Kernel's `TextChunker` (package `Microsoft.SemanticKernel.Core`) and stored with its vector exactly as cut. The old custom chunking features are excluded (see `EXCLUDED.md`).

## Working rules

1. **One feature at a time.** Claude writes it, explains it in plain words, and stops. Nothing else starts until you say "approved".
2. **Order is by dependency.** A feature only uses features above it. If a feature needs something not built yet, we move it down instead.
3. **Small.** One class = one job. One file = one idea. If a file grows past ~150 lines, we split it.
4. **Few projects.** One web project plus one test project. Folders are by feature, not by technical layer.
5. **No interface unless there are two real implementations.** No settings-as-regex. Packages are added as a feature needs them (the old approved list is in PLAN.md section 3); anything not on that list, ask first.
6. **Every feature ships with its test** and a "how to see it working" step, so you can check it yourself.
7. **Status:** `todo` -> `in review` (written, waiting for you) -> `done` (you approved) or `redo` (you want changes). `excluded` = we decided not to build it for now; it is listed in `EXCLUDED.md` and we re-check it later.
8. **Before coding a feature,** Claude reads its section in `FEATURE-DETAILS.md` and the old files listed there, then says in 2-3 lines what it will build.
9. **IDs F43 and up** were added in the review of 2026-11-16. They sit where their dependencies put them, so the numbers are not in order.

---

## A. Foundation

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F01 | Solution scaffold | none | `RagSystem.sln`, one web project `RagSystem`, one test project `RagSystem.Tests`, pinned package versions, `dotnet build` and `dotnet test` pass | done |
| F02 | Token estimate | F01 | Token estimate (`chars / 3.5`, rounded up). Used by chunking, embedding and limits | done |
| F43 | Problem responses | F01 | One global error handler and helper that return `application/problem+json` with `status`, `title`, `detail`, `reason` (and `correlationId` once F39 exists) | todo |

## B. Reading documents (pure code, no database)

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F03 | Ingestion models | F01 | The shapes passed between steps: issue (warning/error with code and line), chunk (position + text exactly as cut), ingestion result (file name, version, title, issues, chunks). Simplified on 2026-11-16 | done |
| F04 | Front matter reader | F03 | Reads the header (`version`, `title`, `docType` guide or whats-new, `updated`, optional `source`) with our own small parser. Errors: missing/unterminated header, missing field, bad docType, version differs from the upload's version | done |
| F05 | Markdown section parser | F03 | (Built, then removed: the chunker package reads the Markdown itself.) | excluded (see EXCLUDED.md) |
| F44 | Markdown structure checks | F05 | (Built on the F05 tree.) | excluded (see EXCLUDED.md) |
| F06 | Section classifier | F05 | (Custom chunk typing: endpoint, workflow, faq, ...) | excluded (see EXCLUDED.md) |
| F07 | Block splitter | F02, F05 | (Custom splitting by blocks, tables by row.) | excluded (see EXCLUDED.md) |
| F08 | Entity finder | F03 | (Entity names used to rank chunks first.) | excluded (see EXCLUDED.md) |
| F09 | Markdown chunker | F02, F03, F04 | Reads the front matter (F04), then cuts the body with Semantic Kernel's `TextChunker` (`SplitMarkDownLines`, then `SplitMarkdownParagraphs`), at most 500 tokens per chunk with a 50-token overlap (both in the `Chunking` section of appsettings.json), with F02 as the token counter. Chunks are kept exactly as cut. Adds package `Microsoft.SemanticKernel.Core` | in review |
| F10 | OpenAPI chunker | F02, F03, F09 | Reads the swagger JSON with `Microsoft.OpenApi` (the package the old project used; it resolves and validates the file). Each operation and each component schema is written back as its own JSON text with `$ref`s kept, then cut with the same `TextChunker` if over 500 tokens, using the route or the schema name as the chunk header. One check: every path starts with `/api/V{n}/` for the upload's version | todo |
| F11 | Document processor | F09, F10 | Picks the chunker by extension (`.md`, `.json`), rejects other types (415) | todo |
| F12 | Dry-run endpoint | F11, F43 | `POST /api/documents?version=v3&dryRun=true` returns the report (errors, warnings) and the chunks, without storing. **Review checkpoint:** we read the real chunks of `content/` together before any database work | todo |

## C. Storing documents

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F13 | Database setup | F01 | Postgres + pgvector in a compose file, EF Core context, migration runs (`MigrateOnStartup`), `/health` checks the database | todo |
| F14 | Document tables | F13 | Tables `documents` (with title and status), `chunks` (text, position, vector column 1,024), `corpus_state`. No sections table, chunk types, entities, extra data or content hash | todo |
| F15 | Known entities provider | F14, F08 | (Names stored for a version, for the entity finder.) | excluded (see EXCLUDED.md) |
| F16 | Ollama embedding client | F01 | Calls Ollama `/api/embed` over `HttpClient`, checks the vector size (1,024), classifies failures (fatal / rate-limited / transient) | todo |
| F17 | Batch embedder | F02, F16 | Splits texts into batches (32 items / 6,000 tokens), retries only the failed batch with back-off, normalises vectors | todo |
| F18 | Submit document | F11, F14 | Validates, refuses a file still Processing (409), creates the document row as Processing, enqueues. Same version + file name replaces. (No "unchanged file" skip: see EXCLUDED.md) | todo |
| F19 | Queue and worker | F17, F18 | In-memory queue and background worker. Embeds the chunks as they are, replaces old chunks in one transaction, bumps the corpus revision, marks Failed on error. Interrupted jobs are marked Failed at startup. (No reuse of unchanged chunks: see EXCLUDED.md) | todo |
| F20 | Document endpoints | F12, F18, F19 | Upload (200 / 202 / 409 / 413 over 10 MB / 415 / 422), list, get, chunks (paged), delete | todo |

## D. Retrieval

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F21 | Query embedder | F16 | Embeds a question with the Qwen3 instruct prefix (documents have none) | todo |
| F22 | Vector search | F14, F21 | Top 20 chunks of a version by cosine distance (no entity boost) | todo |
| F23 | Result post-processing | F22 | Minimum score, near-duplicate removal, document order, 5,000-token budget, labels S1, S2... (no workflow cap, no section expansion: those needed chunk types and sections) | todo |

## E. Chat core (endpoints are open until section F)

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F24 | Ollama chat client | F16 | Calls Ollama `/api/chat` (`think: false`, `num_ctx`, `num_predict`, temperature, keep_alive), returns text, token counts and whether it was cut off | todo |
| F25 | Generation gate | F24 | One generation at a time (semaphore) shared by chat and rewrite; waiting counts against the timeout | todo |
| F26 | Prompt builder | F23 | System rules + documentation context + question; documentation is marked as data; asks the model to end with `Sources: S1, S3` | todo |
| F45 | Answer parser | F26 | Splits off the `Sources:` line, builds citations from the sources named (document title + part number), handles `none`, missing line (cite first 3, never cache) and empty answer (502) | todo |
| F27 | Error mapping | F24, F43 | Ollama failures become problem+json: 503 `llm_unavailable` (down, loading, busy, timeout) with `Retry-After`, 502 `llm_error` (model missing) | todo |
| F28 | Basic chat endpoint | F23, F25, F26, F45, F27 | `POST /api/{version}/chat`: validate, retrieve, answer, citations. No relevant chunks = "not covered" without calling the model | todo |
| F29 | Conversations and history | F28, F14 | Tables `conversations`, `messages`; saves each turn; loads up to 6 turns / 2,000 tokens; 404 for a foreign conversation; `GET /api/{version}/conversations/{id}/messages` | todo |
| F30 | Query rewrite | F25, F29 | Follow-up question becomes a standalone question (last 3 turns, 10 s timeout, fallback: previous question + current) | todo |
| F46 | Retrieval tuning | F28 | Re-measure the minimum score and duplicate threshold on real Qwen3 scores; check answer quality on real questions; if chunks lack context, revisit chunk size, overlap and chunk header | todo |

## F. Security and limits

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F31 | API keys and clients | F14, F43 | Table `clients` (hashed keys, SHA-256), `POST/GET/DELETE /api/clients`, key shown once, admin seeded from `Admin__ApiKey`, admin key protects documents and clients, client key protects chat | todo |
| F32 | Request rate limit | F31 | Requests per minute per client, 429 with `Retry-After` | todo |
| F33 | Token budget | F02, F31, F28 | Per-client token bucket: reserve before the call, settle with actual usage, refund on failure, `X-Token-Remaining` header; also covers the rewrite call | todo |
| F34 | Project caps | F33 | Optional per-role caps (chat, rewrite, embedding), off by default | todo |
| F35 | Input guard | F28 | Blocks prompt injection (400), redacts PII (email, phone, Luhn-valid card, passport) before the model and before storage | todo |
| F36 | Output guard | F28 | Redacts PII in the answer, detects system-prompt leaks (canary + 8-word runs) | todo |
| F37 | Semantic cache | F21, F14, F30 | Table `semantic_cache`; hit when same version + corpus revision + similarity above threshold; written only for grounded answers without PII | todo |

## G. Delivery

| ID | Feature | Needs | What it does / done when | Status |
|---|---|---|---|---|
| F38 | Streaming chat | F28, F36 | `POST /api/{version}/chat/stream` (SSE: `delta`, `done`, `error`), with the 80-character hold-back filter | todo |
| F39 | Logging and correlation id | F01 | `X-Correlation-Id` on every request, in log scope, spans and error bodies, stage prefixes, candidate table for retrieval, no raw user text | todo |
| F40 | Telemetry | F39 | OpenTelemetry traces, metrics and logs to the Aspire Dashboard, with the custom pipeline spans and `rag.*` metrics | todo |
| F47 | Langfuse exporter | F40 | LLM spans only to Langfuse (HTTP, filtered, session and user on every span); chat keeps working if Langfuse is down | todo |
| F41 | Docker | F20, F28 | Dockerfile, full compose (api, postgres, ollama, model pull, dashboard), GPU override, Langfuse compose, ingest script | todo |
| F42 | Web front end | F38, F31, F20 | Angular app: client chat, admin documents and clients. Large: split into parts when we reach it (see details) | todo |
| F48 | Own API docs | F20 | `/openapi/v1.json` and Swagger UI, only when enabled by a setting | todo |
| F49 | Project docs | F20 | README (run, test, ingest) and short upload rules (front matter fields, accepted file types) | todo |

---

## Rules for changing this file

- Claude updates the **Status** column as we go. Nothing is `done` until you say so.
- To add, split, merge or reorder features, we edit this file first, then build.
- Notes and decisions from reviews go in the section below, newest first.

## Review log

| Date | Feature | Decision / change you asked for |
|---|---|---|
| 2026-11-16 | F03, F05 to F11, F14, F15, F22, F23, F26, F44, F45, F46, F49 | **Chunking switches to Semantic Kernel's `TextChunker`** (package `Microsoft.SemanticKernel.Core`) and chunks are stored with their vectors exactly as cut. Excluded: F05, F06, F07, F08, F15, F44. F09 and F10 are redefined as calls to the package (no OpenAPI-specific chunking package exists, so the JSON text is cut by the same chunker). F03 simplified, F14 loses the sections table and the type/entity/extra columns, F22 loses the entity boost, F23 loses the workflow cap and section expansion. Written code for F05, F03's section/type shapes and the Markdig reference was removed. |
| 2026-11-16 | F09 | Chunk overlap is used (50 tokens, 10% of a chunk), and the chunk size, line size and overlap are read from the `Chunking` section of appsettings.json. A wrong value stops the app at start. |
| 2026-11-16 | F03, F12, F14 | Dropped `source type` and `doc type` from the ingestion result, the dry-run report and the `documents` table: with the Semantic Kernel chunking nothing uses them. F04 still reads and checks `docType` in the header. |
| 2026-11-16 | F04 | Read the front matter with our own small parser, no `YamlDotNet` package. |
| 2026-11-16 | F02 | Approved. |
| 2026-11-16 | F48, F49 | Both are wanted (Swagger UI and README are in, no longer optional). |
| 2026-11-16 | all | Reviewed against the old RagDocs (PLAN.md and code). Added `FEATURE-DETAILS.md` with the rules per feature. New features: F43 problem responses, F44 Markdown structure checks, F45 answer parser, F46 retrieval tuning, F47 Langfuse exporter (split from F40), F48 own API docs, F49 project docs. F24 now needs F16 (shared failure kinds), F28 needs F45. |
| 2026-11-16 | F02, F14, F18, F19 | No content hash and no "unchanged text" check: removed from F02 (now only the token estimate), F14, F18 and F19. F18 and F19 themselves stay. Token estimate `chars / 3.5` kept. See `EXCLUDED.md`. |
| 2026-11-16 | F01 | New project name is `RagSystem` (not `RagDocs`). Target `net9.0` (installed SDK 9.0.308) instead of .NET 10. Packages may be added as each feature needs them. |
