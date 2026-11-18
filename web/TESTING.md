# Testing the web app

Two ways to run it. The mock needs no database, no Gemini key and no API.

| | Command | Talks to | Use it to |
|---|---|---|---|
| **Mock** | `npm run start:mock` | A copy of the API inside the browser | Check every screen and every error state, any time, for free |
| **Real API** | `npm start` | `http://localhost:5289` (the API in the Development profile) | Check the real answers, scores and speed |
| Unit and page tests | `npm test` | The mock, in a test browser (jsdom) | Catch regressions before a commit |

Open `http://localhost:4200`. In mock mode the header shows a **Mock data** badge and a **Reset** button (documents go back
to the starting ones, conversations are forgotten). Mock data is kept in this browser, so a reload does not lose it.

## What the mock is, and is not

- It follows the API's rules: the same validation order and `reason` codes, the retrieval steps of `ResultPostProcessor`
  (minimum score 0.60, near-duplicates, token budget, document order, labels S1, S2...), the `Sources:` line parser, the
  upload checks (front matter, `.md` and OpenAPI chunking, 10 MB, 409 while Processing, 422 reports with line numbers).
- It answers in the **shapes of the backend records** (`src/app/core/mock/backend-dtos.ts`, one type per C# record). The
  specs read every mock reply with the app's own readers, so a change in a record shows up as a failing test.
- It is **not a model and not a vector search.** Scores come from word overlap, and the answer is a short quote from the
  best sources. Use it to check the screens, **never to judge answer quality or to choose a minimum score.** That is F28 on
  the real API, with the Tuning page.
- Mock settings differ from `appsettings.json` on purpose: the context budget is 500 tokens (so "OverBudget" happens with
  small documents) and near-duplicates are found by shared words, not by cosine.
- Starting documents: V3 `ndc-portal-apis-v3.md` (11 chunks), V3 `whats-new-v3.md` (5), V3 `OffersV3.swagger.json` (Failed),
  V2 `ndc-portal-apis-v2.md` (6). Their text is short excerpts of the real documentation.

## Words that make the mock fail (put one in a chat message)

| Write | The mock |
|---|---|
| `mock:503` | answers 503 `llm_unavailable` with `Retry-After: 5` |
| `mock:502` | answers 502 `llm_error` (only when the model would have been called) |
| `mock:empty` | answers 502 `empty_answer` |
| `mock:network` | loses the connection (status 0) |
| `mock:slow` | takes 9 seconds (check "Stop waiting") |
| `mock:nosources` | the "model" forgets its Sources line: the first 3 sources are cited |
| `mock:none` | the "model" says it is not covered (`Sources: none`): no citations |
| `mock:cache` | usage all zeros with `cacheHit: true` (the "Answered from cache" badge) |

Other ways to reach states: upload a file whose **name contains `fail`** (ends Failed), upload the same name twice quickly
(409), upload a `.md` with no `---` header (422 with `frontmatter.missing`), open `/chat/v3/<any other guid>` (404).

## Checklist by feature

Run it in mock mode first, then once on the real API. Each line says what you should see.

**F16 Documents** (`/admin/documents`)
1. The V3 list shows 3 files; `OffersV3.swagger.json` is Failed with its reason. V2 shows 1.
2. Tick **Check only**, choose a `.md` with a valid header: "Checked only", report Valid, chunks listed, list unchanged.
3. Choose a `.md` without a header: Rejected, error `frontmatter.missing`, line 1.
4. Choose a `.pdf`: rejected before sending. A file over 10 MB: rejected before sending.
5. Upload a valid file: Accepted, row appears as Processing, becomes Ready on its own (the list refreshes every few seconds).
6. **Details** on a Ready file: chunks as "Part N" with their tokens. **Delete** asks first; Cancel keeps it.

**F25/F26 Chat** (`/chat/v3`)
1. Click an example question, Send: your question, then an answer with a **Sources** list ("Part N", document title), token counts.
2. The address becomes `/chat/v3/<id>` and the conversation appears in the left list. Reload: it reopens with both messages.
3. Ask something unrelated ("What is the capital of France?"): the "couldn't find this" answer, no sources, "0 tokens used".
4. The version switch is locked inside a conversation; **New conversation** unlocks it.
5. A question of 3,600 characters: red counter, Send disabled.
6. **Check retrieval** under an answer opens the Search check for that question.

**F27 Follow-up**: after an answer about seats, ask "What about children?". It still finds sources (it is searched together with
the earlier question). The same text in a new conversation finds nothing.

**Errors**: `mock:503` (countdown on Send, "Retry in", Edit message keeps the text), `mock:502` ("Send again"),
`mock:empty`, `mock:network` ("Cannot reach the server"), `mock:slow` then **Stop waiting**.

**F44 Search check** (`/search`): run a question, see kept sources with scores and the dropped table with the reason.
Share the address (it holds the version and the question).

**F28 Tuning** (`/tuning`) — the real work is on the real API with real questions:
1. **Load examples**, **Run**. Each question shows found / kept / best / weakest kept scores. The last example has
   `=> none`: it should keep nothing.
2. Change **Minimum score to try**: the "would be answered at" tile and the "At or above" column change without a new search.
3. Click a question: kept sources, what was left out and why, and a link to the Search check.
4. **Copy as CSV** / **Download JSON** keep the numbers for the tuning notes.
5. How to use it for F28: write 20 to 30 real questions with the source each should find (`question => part 4` or a
   document title), run them, look at the "Missed" rows (lost to `BelowMinScore`, `NearDuplicate` or `OverBudget`), and at the
   best scores of the questions that should be unanswered. Move the minimum between the two groups; then change
   `Retrieval:MinScore` and run again.

## Running the tests

```bash
npm test                 # all specs once
npx ng test --watch      # while editing
```

| Spec | Covers |
|---|---|
| `core/mock/mock-backend.spec.ts` | The mock itself: rules, errors, triggers, persistence, and that every reply is readable by the app |
| `core/api/*.spec.ts` | Error mapping (`reason` and status to plain language) and the readers for each DTO |
| `features/*/**.page.spec.ts` | Real pages, real services, real interceptors, mock behind them: chat, documents, search, tuning |
| `features/tuning/tuning-math.spec.ts` | Question set parsing, score statistics, expected-source check, CSV |
| `features/chat/conversation-state.spec.ts` | The chat state machine: send, fail, retry, cancel, cooldown, history |
| `shared/markdown/markdown.spec.ts` | The safe Markdown renderer |

What the tests do **not** cover: how the pages look (spacing, dark mode, phone width) and the real API. Check those by eye
with `npm run start:mock`, and run the checklist once on the real API.

## Switching to the real API

1. Start Postgres (`docker compose up -d postgres`) and the API (`dotnet run --project src/RagSystem`, Development
   profile, with `Gemini__ApiKey` or `appsettings.Local.json`). It listens on `http://localhost:5289`.
2. `npm start`. The dev server proxies `/api` to it (`proxy.conf.json`), so no CORS is needed.
3. Upload the five files of `content/` (or use the Documents page), then use Chat, Search check and Tuning.

Every chat answer and every search sends text to the model service (Gemini). Use test questions, not customer data.
