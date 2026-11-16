# Excluded features

Things we decided not to build for now. Nothing here is deleted for good: before the project is finished we read this file and decide, one by one, whether each item comes back.

| Date | Item | What it was | Why excluded | Check later |
|---|---|---|---|---|
| 2026-11-16 | Content hash (SHA-256 of document or chunk text) | The `Sha256Hex` helper (was in F02) and a content-hash column (was in F14) | Not needed: we do not check whether stored text changed | Is a hash useful for anything else? (F31 hashes API keys; that is a separate use, decided in F31) |
| 2026-11-16 | "Unchanged file" skip (was in F18) | Skip an upload when the file has the same hash and is Ready | Follows from dropping the content hash. Every upload is processed | Re-upload cost: re-embedding everything each time is slower |
| 2026-11-16 | Reuse of unchanged chunk vectors (was in F19) | Keep the stored vector of a chunk whose text did not change, embed only the changed ones | Follows from dropping the content hash. Every chunk is embedded again | Same as above |

F18 (submit document) and F19 (queue and worker) are **not** excluded. They stay, without the three items above.
