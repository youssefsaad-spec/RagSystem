# NDC Docs Assistant: web front end

Angular 22 (standalone components, signals, zoneless) front end for the RagSystem API. Ported from the old RagDocs
`web/` app and reshaped to the API that exists today. No UI, Markdown or sanitizer libraries: the only runtime
packages are Angular and RxJS, pinned exactly in `package.json`. Ask before adding one.

| Page | Address | What it does |
|---|---|---|
| Chat | `/chat/:version/:id?` | Ask about the V2 or V3 documentation. Answers show their sources ("title, part N"). Past conversations reopen from the server. |
| Documents | `/admin/documents?version=` | Upload `.md` / OpenAPI `.json` (or "check only" dry runs), validation reports with line numbers, status polling, chunk browser, delete. |
| Search check | `/search?version=&q=` | Shows what retrieval finds for a question without calling a chat model: kept sources with scores, and dropped candidates with the reason. |
| Tuning (F28) | `/tuning?version=` | Runs a set of real questions through the search: scores next to the minimum score, "what if the minimum were X", and whether each question finds the source it was written for (`question => text`). Export as CSV or JSON. |

## Run

```bash
cd web
npm ci
npm run start:mock   # http://localhost:4200 with a simulated API in the browser: no database, no Gemini key
npm start            # http://localhost:4200, /api proxied to the real API on localhost:5289 (proxy.conf.json)
npm test             # unit and page tests (Vitest), they use the mock
npm run build        # production build in dist/ (contains no mock code)
```

`npm run start:mock` and the tests are described in [TESTING.md](TESTING.md): what the mock does, the words that make it
fail (`mock:503`, ...), and a checklist per feature (F16, F25 to F28, F44).

## One origin, no CORS

The API sends no CORS headers. The app must be served from the same origin as `/api`: the dev server's proxy does that
now; a reverse proxy (nginx, F40) must do it in production, with a long read timeout (chat can take about a minute).

## What the API does not have yet (so the app does not either)

| Missing | Comes with | Effect here |
|---|---|---|
| API keys, sign-in, admin role (F29) | F29 | No login page; Documents is open. The router has a comment where the admin guard goes. |
| Streaming chat (F36) | F36 | The answer arrives whole after a wait; "Stop waiting" cancels the request in the browser only. |
| Clients admin (F29) | F29 | No Clients page. |
| List-conversations endpoint | none planned | The conversation list is kept in this browser (`localStorage`); messages always come from the server. Removing an entry only forgets it locally. |
| Token and rate limits (F30-F32) | F30-F32 | No token meter. A `503 llm_unavailable` still locks the composer for its `Retry-After`. |

## Error handling

Every failure becomes an `ApiError` (`core/api/api-error.ts`) with a plain-language message, whether it can be retried,
and `Retry-After` and a reference id when the server sent them. The `reason` values it knows are the ones the API
sends: `empty_question`, `question_too_long`, `unknown_version`, `conversation_not_found`, `document_not_found`,
`document_processing`, `not_multipart`, `no_file`, `llm_unavailable`, `llm_error`, `empty_answer`, plus the upload
statuses 409 / 413 / 415 / 422 (the 422 body is a line-numbered report shown per file). Bodies that are not the API's
(wrong server, missing proxy) become `bad_response`, never "undefined".

## Safety of answers

Answers are parsed by `shared/markdown/markdown.ts` into a typed tree and rendered with ordinary Angular text bindings.
There is no `innerHTML`: raw HTML stays text, and only `http`, `https` and `mailto` links are linked.

## Layout

```
src/app/core      api (error model, interceptor, services, models), mock (the simulated API), prefs, ui, util
src/app/features  chat, admin/documents, search, tuning, not-found
src/environments  environment.ts (real) and environment.mock.ts (mock), swapped by `--configuration mock`
src/testing       helper that starts the app with the mock for page specs
src/app/shared    markdown, modal (native <dialog>), error-notice
```
