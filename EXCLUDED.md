# Excluded features

Feature numbers written as "old F05" are numbers from before the renumbering of 2026-11-16 (those features were removed and have no number now). Numbers without "old" are current; see FEATURES.md.

Things we decided not to build for now. Nothing here is deleted for good: before the project is finished we read this file and decide, one by one, whether each item comes back.

| Date | Item | What it was | Why excluded | Check later |
|---|---|---|---|---|
| 2026-11-16 | Content hash (SHA-256 of document or chunk text) | The `Sha256Hex` helper (was in F02) and a content-hash column (was in F11) | Not needed: we do not check whether stored text changed | Is a hash useful for anything else? (F29 hashes API keys; that is a separate use, decided in F29) |
| 2026-11-16 | "Unchanged file" skip (was in F14) | Skip an upload when the file has the same hash and is Ready | Follows from dropping the content hash. Every upload is processed | Re-upload cost: re-embedding everything each time is slower |
| 2026-11-16 | Reuse of unchanged chunk vectors (was in F15) | Keep the stored vector of a chunk whose text did not change, embed only the changed ones | Follows from dropping the content hash. Every chunk is embedded again | Same as above |
| 2026-11-16 | **old F05 Markdown section parser** | Section tree from the headings, with blocks and warnings for code, images and anchor links. Built (26 tests), then removed | The chunker package reads the Markdown itself | If we need heading context in chunks again |
| 2026-11-16 | **old F06 Section classifier** | Chunk types (endpoint, workflow, faq, data_model, enum, errors, glossary, guide) and route inheritance | Custom chunking. Chunks are now stored as the package cuts them, with no type | A type could help ranking later |
| 2026-11-16 | **old F07 Block splitter** | Splitting by blocks: tables by row with the header repeated, lists by item, workflows kept whole | The package splits | Tables are cut mid-row by the package (seen in the spike, see below) |
| 2026-11-16 | **old F08 Entity finder** and **old F15 Known entities provider** | Names (operations, fields, models) found in text and stored on chunks, so chunks naming what the user typed rank first | Dropped by decision | Exact-name questions (`FareCheck`, `cellCode`) may rank worse; check in F28 |
| 2026-11-16 | **old F44 Markdown structure checks** | One `#` heading, no skipped levels, table header and separator, oversize warnings | Built on the old F05 tree | Upload validation is now only the front matter checks (F05) |
| 2026-11-16 | Dropping code blocks, images and anchor links from uploads | Warnings and removal while parsing | Followed old F05. Text is stored as it is | Code samples are now indexed if a file has them (the real files have none) |
| 2026-11-16 | `source type` (`md`/`openapi`) and `doc type` (`guide`/`whats-new`) fields | Stored on the result, the report and the `documents` table | Nothing uses them with the new chunking: they told guide text from "API Spec" text and exempted whats-new files from a structure check | The admin document list could show the file kind; it can be read from the extension |
| 2026-11-16 | Parts of F11, F18, F19, F22 | `sections` table, `chunk_type`, `entities`, `extra`, `section_path` columns; entity boost in search; "at most 2 workflow chunks" and "expand to the whole section"; the prompt rule that field names come from "API Spec" chunks | They needed chunk types, entities or sections | Section expansion and the workflow cap were meant to keep complete answers together |

## What the spike showed (Semantic Kernel `TextChunker` on the real V3 files, 2026-11-16)

Run in a scratch project outside the repository, with the token counter set to our `chars / 3.5` estimate and 500 tokens per chunk.

- V3 guide: 127 chunks, average 296 tokens, largest 498. What's New: 8 chunks. V3 Swagger: 256 chunks.
- Chunk boundaries do not respect structure. In one real case the FareCheck "Request parameters" table was cut inside the `PassengersSelectedSeats` row: the row ends in one chunk and `See passengersSelectedSeats. |` starts the next, followed by the Response table.
- Chunks carry no heading or endpoint context. The FareCheck Response chunk does not contain the word "FareCheck" at all, and it ends with the first lines of the next endpoint (`#### BookAndPay`).
- OpenAPI, three ways tried on the V3 Swagger (same scratch project):
  - Raw JSON text through `TextChunker`: 256 chunks, cut anywhere, nothing names the operation.
  - `Microsoft.OpenApi` with every `$ref` inlined: unusable. 177,520 tokens for 18 operations (up to 19,000 each), 582 pieces. Shared schemas are repeated everywhere.
  - `Microsoft.OpenApi` with `$ref`s kept, each operation and each component schema written separately: 18 operations (11,749 tokens, 36 pieces after cutting) and 307 schemas (46,143 tokens, median 113, 132 under 100 tokens, 313 pieces). About 350 pieces. This is what F07 now uses.
  - The old project did the same read with `Microsoft.OpenApi` 2.13.0 but then used about 400 lines of its own code to flatten request fields to 4 levels with a cycle guard.
- Options that stay inside the package, to try in F28 if answers suffer: a chunk header (for example the document title), overlap between chunks, smaller or larger limits.
