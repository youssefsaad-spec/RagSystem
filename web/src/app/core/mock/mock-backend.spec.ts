import { ApiError } from '../api/api-error';
import {
  readChatResponse,
  readChunks,
  readDocument,
  readDocuments,
  readHistory,
  readReport,
  readSearch,
} from '../api/models';
import { ChatResponseDto, DocumentViewDto, ProblemDto } from './backend-dtos';
import { MockBackend, MockReply, MockState, MockStorage } from './mock-backend';

function setup(options: { storage?: MockStorage } = {}) {
  let now = Date.parse('2026-11-17T10:00:00Z');
  let n = 0;
  const backend = new MockBackend({
    latency: 0,
    processingMs: 1000,
    now: () => now,
    newId: () => `00000000-0000-4000-8000-${String(++n).padStart(12, '0')}`,
    storage: options.storage,
  });
  return { backend, advance: (ms: number) => (now += ms) };
}

type Mock = ReturnType<typeof setup>['backend'];

const get = (b: Mock, path: string, query: Record<string, string> = {}) => b.handle({ method: 'GET', path, query });
const chat = (b: Mock, version: string, body: unknown) => b.handle({ method: 'POST', path: `/api/${version}/chat`, query: {}, body });
const upload = (b: Mock, name: string, text: string, query: Record<string, string> = { version: 'v3' }, size = text.length) =>
  b.handle({ method: 'POST', path: '/api/documents', query, multipart: true, file: { name, size, text } });

const problemOf = (r: MockReply) => r.body as ProblemDto;
const answerOf = (r: MockReply) => r.body as ChatResponseDto;

const MD = (extra = '', version = 'v3') =>
  ['---', `version: ${version}`, 'title: "Test guide"', 'docType: guide', 'updated: 2026-11-17', '---', '', '# Test guide', '', 'First paragraph about seats.', '', extra].join('\n');

describe('MockBackend documents', () => {
  it('lists the documents of a version by file name, in the shape the app reads', () => {
    const { backend } = setup();
    const reply = get(backend, '/api/documents', { version: 'v3' });
    expect(reply.status).toBe(200);
    const docs = readDocuments(reply.body);
    expect(docs.map((d) => d.fileName)).toEqual(['ndc-portal-apis-v3.md', 'OffersV3.swagger.json', 'whats-new-v3.md'].sort((a, b) => a.localeCompare(b, 'en', { sensitivity: 'base' })));
    expect(docs.find((d) => d.status === 'Failed')?.error).toMatch(/Interrupted by a restart/);
    expect(readDocuments(get(backend, '/api/documents', { version: 'V2' }).body)).toHaveLength(1);
  });

  it('answers 400 unknown_version for a version it does not hold, with the API wording', () => {
    const { backend } = setup();
    const reply = get(backend, '/api/documents', { version: 'v9' });
    expect(reply.status).toBe(400);
    expect(problemOf(reply)).toMatchObject({ reason: 'unknown_version', title: 'Unknown version' });
    expect(problemOf(reply).detail).toContain('v2, v3');
    expect(get(backend, '/api/documents', {}).status).toBe(400);
  });

  it('reads one document, and 404s for another version', () => {
    const { backend } = setup();
    const [first] = readDocuments(get(backend, '/api/documents', { version: 'v3' }).body);
    expect(readDocument(get(backend, `/api/documents/${first.id}`, { version: 'v3' }).body).id).toBe(first.id);
    const other = get(backend, `/api/documents/${first.id}`, { version: 'v2' });
    expect(other.status).toBe(404);
    expect(problemOf(other).reason).toBe('document_not_found');
  });

  it('pages chunks in order and clamps skip and take like the API', () => {
    const { backend } = setup();
    const doc = readDocuments(get(backend, '/api/documents', { version: 'v3' }).body).find((d) => d.fileName === 'ndc-portal-apis-v3.md')!;
    const all = readChunks(get(backend, `/api/documents/${doc.id}/chunks`, { version: 'v3' }).body);
    expect(all).toHaveLength(doc.chunkCount);
    expect(all.map((c) => c.ordinal)).toEqual(all.map((_, i) => i));
    expect(readChunks(get(backend, `/api/documents/${doc.id}/chunks`, { version: 'v3', skip: '2', take: '3' }).body).map((c) => c.ordinal)).toEqual([2, 3, 4]);
    expect(readChunks(get(backend, `/api/documents/${doc.id}/chunks`, { version: 'v3', skip: '-5', take: '0' }).body)).toHaveLength(1);
    expect(get(backend, `/api/documents/99999999-0000-4000-8000-000000000000/chunks`, { version: 'v3' }).status).toBe(404);
  });

  it('deletes a document with its chunks, then 404s', () => {
    const { backend } = setup();
    const doc = readDocuments(get(backend, '/api/documents', { version: 'v2' }).body)[0];
    const del = backend.handle({ method: 'DELETE', path: `/api/documents/${doc.id}`, query: { version: 'v2' } });
    expect(del.status).toBe(204);
    expect(readDocuments(get(backend, '/api/documents', { version: 'v2' }).body)).toHaveLength(0);
    expect(backend.handle({ method: 'DELETE', path: `/api/documents/${doc.id}`, query: { version: 'v2' } }).status).toBe(404);
    // With no documents, nothing can be found.
    expect(readSearch(get(backend, '/api/v2/search', { q: 'authenticate' }).body).sources).toHaveLength(0);
  });
});

describe('MockBackend upload', () => {
  it('dry run: 200 with the report and its chunks, nothing stored', () => {
    const { backend } = setup();
    const reply = upload(backend, 'new.md', MD(), { version: 'v3', dryRun: 'true' });
    expect(reply.status).toBe(200);
    const report = readReport(reply.body);
    expect(report).toMatchObject({ valid: true, title: 'Test guide', version: 'v3', fileName: 'new.md' });
    expect(report.chunks.length).toBeGreaterThan(0);
    expect(readDocuments(get(backend, '/api/documents', { version: 'v3' }).body).some((d) => d.fileName === 'new.md')).toBe(false);
  });

  it('dry run of an invalid file is still 200 and says what is wrong, with line numbers', () => {
    const { backend } = setup();
    const missing = readReport(upload(backend, 'a.md', '# no header', { version: 'v3', dryRun: 'true' }).body);
    expect(missing.valid).toBe(false);
    expect(missing.errors[0]).toMatchObject({ code: 'frontmatter.missing', line: 1 });
    expect(missing.chunks).toEqual([]);

    const wrong = readReport(upload(backend, 'a.md', MD('', 'v2'), { version: 'v3', dryRun: 'true' }).body);
    expect(wrong.errors.map((e) => e.code)).toContain('version.mismatch');

    const badType = readReport(upload(backend, 'a.md', MD().replace('docType: guide', 'docType: poem'), { version: 'v3', dryRun: 'true' }).body);
    expect(badType.errors.map((e) => e.code)).toEqual(['frontmatter.docType']);
  });

  it('a real upload of an invalid file is 422 with the report and no chunks', () => {
    const { backend } = setup();
    const reply = upload(backend, 'a.md', '# no header');
    expect(reply.status).toBe(422);
    const report = readReport(reply.body);
    expect(report.valid).toBe(false);
    expect(report.chunks).toEqual([]);
  });

  it('refuses a missing, empty, huge or unsupported file with the API codes', () => {
    const { backend } = setup();
    const noFile = backend.handle({ method: 'POST', path: '/api/documents', query: { version: 'v3' }, multipart: true, file: null });
    expect([noFile.status, problemOf(noFile).reason]).toEqual([400, 'no_file']);
    expect(problemOf(upload(backend, 'a.md', '')).reason).toBe('no_file');
    const huge = upload(backend, 'a.md', 'x', { version: 'v3' }, 10 * 1024 * 1024 + 1);
    expect([huge.status, problemOf(huge).reason]).toEqual([413, 'file_too_large']);
    const wrongType = upload(backend, 'a.pdf', 'x');
    expect([wrongType.status, problemOf(wrongType).reason]).toEqual([415, 'unsupported_file_type']);
    const notForm = backend.handle({ method: 'POST', path: '/api/documents', query: { version: 'v3' } });
    expect(problemOf(notForm).reason).toBe('not_multipart');
    expect(problemOf(upload(backend, 'a.md', MD(), { version: 'v7' })).reason).toBe('unknown_version');
  });

  it('accepts a valid file as 202 Processing, refuses it again with 409, and makes it Ready later', () => {
    const { backend, advance } = setup();
    const accepted = upload(backend, 'new.md', MD('Second paragraph about aisles.'));
    expect(accepted.status).toBe(202);
    const { id, status } = accepted.body as { id: string; status: string };
    expect(status).toBe('Processing');
    expect(accepted.headers?.['Location']).toBe(`/api/documents/${id}?version=v3`);

    const processing = readDocument(get(backend, `/api/documents/${id}`, { version: 'v3' }).body);
    expect(processing).toMatchObject({ status: 'Processing', readyAt: null, error: null });

    const busy = upload(backend, 'new.md', MD());
    expect([busy.status, problemOf(busy).reason]).toEqual([409, 'document_processing']);
    expect(problemOf(busy).detail).toContain(id);

    advance(1500);
    const ready = readDocument(get(backend, `/api/documents/${id}`, { version: 'v3' }).body);
    expect(ready.status).toBe('Ready');
    expect(ready.chunkCount).toBeGreaterThan(0);
    expect(ready.readyAt).not.toBeNull();
    expect(readChunks(get(backend, `/api/documents/${id}/chunks`, { version: 'v3' }).body)).toHaveLength(ready.chunkCount);
  });

  it('uploading the same file name again replaces the document and keeps its id', () => {
    const { backend, advance } = setup();
    const first = (upload(backend, 'new.md', MD()).body as { id: string }).id;
    advance(2000);
    const second = (upload(backend, 'new.md', MD('More text about meals.')).body as { id: string }).id;
    expect(second).toBe(first);
    advance(2000);
    const docs = readDocuments(get(backend, '/api/documents', { version: 'v3' }).body).filter((d) => d.fileName === 'new.md');
    expect(docs).toHaveLength(1);
  });

  it('a file whose name contains "fail" ends Failed with a reason', () => {
    const { backend, advance } = setup();
    const id = (upload(backend, 'will-fail.md', MD()).body as { id: string }).id;
    advance(2000);
    const doc = readDocument(get(backend, `/api/documents/${id}`, { version: 'v3' }).body);
    expect(doc.status).toBe('Failed');
    expect(doc.error).toMatch(/refused the request/);
  });

  it('reads an OpenAPI file: one chunk per operation, and the path prefix must match the version', () => {
    const { backend } = setup();
    const spec = (path: string) =>
      JSON.stringify({ info: { title: 'Offers API' }, paths: { [path]: { post: { summary: 'Seat map' }, get: { summary: 'Read' } } } });
    const good = readReport(upload(backend, 'offers.json', spec('/api/V3/Offers/SeatMap'), { version: 'v3', dryRun: 'true' }).body);
    expect(good).toMatchObject({ valid: true, title: 'Offers API' });
    expect(good.chunks).toHaveLength(2);

    const bad = readReport(upload(backend, 'offers.json', spec('/api/V2/Offers/SeatMap'), { version: 'v3', dryRun: 'true' }).body);
    expect(bad.errors[0].code).toBe('openapi.path_version');
    expect(readReport(upload(backend, 'offers.json', '{ not json', { version: 'v3', dryRun: 'true' }).body).errors[0].code).toBe('openapi.invalid_json');
  });
});

describe('MockBackend search', () => {
  it('finds the sources of a question and reports the rest as dropped, in the shape the app reads', () => {
    const { backend } = setup();
    const reply = get(backend, '/api/v3/search', { q: 'How do I select seats in V3?' });
    expect(reply.status).toBe(200);
    const result = readSearch(reply.body);
    expect(result.version).toBe('v3');
    expect(result.sources.length).toBeGreaterThan(0);
    expect(result.sources.map((s) => s.label)).toEqual(result.sources.map((_, i) => `S${i + 1}`));
    expect(result.sources.every((s) => s.score >= 0.6)).toBe(true);
    expect(result.sources.every((s) => /, part \d+$/.test(s.citation))).toBe(true);
    expect(result.dropped.every((d) => ['BelowMinScore', 'NearDuplicate', 'OverBudget'].includes(d.reason))).toBe(true);
    expect(result.candidates).toBe(result.sources.length + result.dropped.length);
  });

  it('keeps the sources of the best document first and in document order inside it', () => {
    const { backend } = setup();
    const { sources } = readSearch(get(backend, '/api/v3/search', { q: 'offerId SeatMap FareCheck cellCode' }).body);
    const parts = sources.filter((s) => s.citation.startsWith('NDC Portal APIs V3, part')).map((s) => Number(s.citation.split('part ')[1]));
    expect(parts).toEqual([...parts].sort((a, b) => a - b));
  });

  it('finds nothing for a question the documentation does not cover', () => {
    const { backend } = setup();
    const result = readSearch(get(backend, '/api/v3/search', { q: 'What is the capital of France?' }).body);
    expect(result.sources).toHaveLength(0);
    expect(result.dropped.length).toBeGreaterThan(0);
    expect(result.dropped.every((d) => d.reason === 'BelowMinScore')).toBe(true);
  });

  it('drops a near-duplicate and what no longer fits the budget, each with its reason', () => {
    const { backend } = setup();
    const result = readSearch(get(backend, '/api/v3/search', { q: 'GetAvailableOffers FareConfirmOffer AddPassengersDetails FareCheck Hold BookAndPay ndcBookingReference offerId workflow' }).body);
    const reasons = result.dropped.map((d) => d.reason);
    expect(reasons).toContain('NearDuplicate');
    expect(result.sources.length).toBeGreaterThan(0);
    const budget = readSearch(get(backend, '/api/v3/search', { q: 'offerId FareCheck Hold BookAndPay SeatMap seats rate limit authentication key flow' }).body);
    expect(budget.dropped.map((d) => d.reason)).toContain('OverBudget');
  });

  it('validates the question like the API: empty, then too long, with the version checked first', () => {
    const { backend } = setup();
    expect(problemOf(get(backend, '/api/v3/search', {}))).toMatchObject({ reason: 'empty_question', status: 400 });
    expect(problemOf(get(backend, '/api/v3/search', { q: '   ' })).reason).toBe('empty_question');
    expect(problemOf(get(backend, '/api/v3/search', { q: 'a'.repeat(3600) })).reason).toBe('question_too_long');
    expect(problemOf(get(backend, '/api/v9/search', { q: '' })).reason).toBe('unknown_version');
  });
});

describe('MockBackend chat', () => {
  it('answers with sources, saves the exchange and returns the shape the app reads', () => {
    const { backend } = setup();
    const reply = chat(backend, 'v3', { message: 'How do I select seats in V3?' });
    expect(reply.status).toBe(200);
    const res = readChatResponse(reply.body);
    expect(res.answer).toMatch(/V3 documentation/);
    expect(res.answer).not.toMatch(/Sources:/); // the Sources line is split off, as the API does
    expect(res.citations.length).toBeGreaterThan(0);
    expect(res.citations.every((c) => c.part >= 1 && c.text === `${c.title}, part ${c.part}`)).toBe(true);
    expect(res.usage.inputTokens).toBeGreaterThan(0);
    expect(res.usage.cacheHit).toBe(false);

    const messages = readHistory(get(backend, `/api/v3/conversations/${res.conversationId}/messages`).body);
    expect(messages.map((m) => m.role)).toEqual(['user', 'assistant']);
    expect(messages[0]).toMatchObject({ content: 'How do I select seats in V3?', usage: null, citations: [] });
    expect(messages[1].id).toBe(res.messageId);
    expect(messages[1].citations).toEqual(res.citations);
  });

  it('continues a conversation, and a short follow-up is searched together with the previous question', () => {
    const { backend } = setup();
    const first = answerOf(chat(backend, 'v3', { message: 'How do I select seats in V3?' }));
    const follow = chat(backend, 'v3', { message: 'What about children?', conversationId: first.conversationId });
    expect(follow.status).toBe(200);
    expect(answerOf(follow).conversationId).toBe(first.conversationId);
    expect(answerOf(follow).citations.length).toBeGreaterThan(0); 
    expect(readHistory(get(backend, `/api/v3/conversations/${first.conversationId}/messages`).body)).toHaveLength(4);

    const alone = answerOf(chat(backend, 'v3', { message: 'What about children?' }));
    expect(alone.citations).toHaveLength(0);
  });

  it('answers "not covered" without calling the model: no sources, no tokens', () => {
    const { backend } = setup();
    const res = answerOf(chat(backend, 'v2', { message: 'What is the capital of France?' }));
    expect(res.answer).toMatch(/couldn't find this in the V2 NDC Portal documentation/);
    expect(res.citations).toEqual([]);
    expect(res.usage).toEqual({ inputTokens: 0, outputTokens: 0, thinkingTokens: 0, cacheHit: false });
  });

  it('treats a conversation of another version, or an unknown one, as not found', () => {
    const { backend } = setup();
    const { conversationId } = answerOf(chat(backend, 'v3', { message: 'How do I select seats in V3?' }));
    const wrongVersion = chat(backend, 'v2', { message: 'authenticate', conversationId });
    expect([wrongVersion.status, problemOf(wrongVersion).reason]).toEqual([404, 'conversation_not_found']);
    expect(get(backend, `/api/v2/conversations/${conversationId}/messages`).status).toBe(404);
    const unknown = chat(backend, 'v3', { message: 'x', conversationId: '99999999-0000-4000-8000-000000000000' });
    expect(problemOf(unknown).reason).toBe('conversation_not_found');
  });

  it('validates like the API: version, empty, too long', () => {
    const { backend } = setup();
    expect(problemOf(chat(backend, 'v1', { message: 'hi' })).reason).toBe('unknown_version');
    expect(problemOf(chat(backend, 'v3', { message: '  ' }))).toMatchObject({ reason: 'empty_question', status: 400 });
    expect(problemOf(chat(backend, 'v3', {})).reason).toBe('empty_question');
    expect(problemOf(chat(backend, 'v3', null)).reason).toBe('empty_question');
    expect(problemOf(chat(backend, 'v3', { message: 'a'.repeat(3600) })).reason).toBe('question_too_long');
    expect(chat(backend, 'V3', { message: 'authenticate subscription key' }).status).toBe(200); // any case
  });

  describe('triggers', () => {
    it('mock:503 is a busy model service with Retry-After', () => {
      const { backend } = setup();
      const reply = chat(backend, 'v3', { message: 'seats mock:503' });
      expect([reply.status, problemOf(reply).reason, reply.headers?.['Retry-After']]).toEqual([503, 'llm_unavailable', '5']);
    });

    it('mock:502 and mock:empty fail only when the model would have been called', () => {
      const { backend } = setup();
      expect(problemOf(chat(backend, 'v3', { message: 'seats mock:502' })).reason).toBe('llm_error');
      expect(problemOf(chat(backend, 'v3', { message: 'seats mock:empty' })).reason).toBe('empty_answer');
      expect(chat(backend, 'v3', { message: 'capital of France mock:502' }).status).toBe(200); // not covered: no model call
    });

    it('mock:network is a lost connection', () => {
      const { backend } = setup();
      expect(chat(backend, 'v3', { message: 'seats mock:network' }).status).toBe(0);
    });

    it('mock:slow takes 9 seconds when latency is real', () => {
      const slow = new MockBackend({ latency: 1 });
      expect(chat(slow, 'v3', { message: 'seats mock:slow' }).delayMs).toBe(9000);
    });

    it('mock:nosources cites the first three sources, mock:none cites none, mock:cache is a cache hit', () => {
      const { backend } = setup();
      const noLine = answerOf(chat(backend, 'v3', { message: 'offerId FareCheck SeatMap mock:nosources' }));
      expect(noLine.citations.length).toBeGreaterThan(0);
      expect(noLine.citations.length).toBeLessThanOrEqual(3);
      expect(noLine.answer).not.toContain('mock:');
      const none = answerOf(chat(backend, 'v3', { message: 'offerId FareCheck SeatMap mock:none' }));
      expect(none.citations).toEqual([]);
      const cache = answerOf(chat(backend, 'v3', { message: 'offerId FareCheck SeatMap mock:cache' }));
      expect(cache.usage).toEqual({ inputTokens: 0, outputTokens: 0, thinkingTokens: 0, cacheHit: true });
    });
  });
});

describe('MockBackend state', () => {
  it('keeps documents and conversations in its storage and starts from them again', () => {
    let saved: MockState | null = null;
    const storage: MockStorage = { load: () => saved, save: (s) => (saved = structuredClone(s)) };
    const first = setup({ storage });
    const { conversationId } = answerOf(chat(first.backend, 'v3', { message: 'How do I select seats in V3?' }));

    const second = setup({ storage });
    expect(readHistory(get(second.backend, `/api/v3/conversations/${conversationId}/messages`).body)).toHaveLength(2);
  });

  it('reset goes back to the starting documents and forgets conversations', () => {
    const { backend } = setup();
    const { conversationId } = answerOf(chat(backend, 'v3', { message: 'How do I select seats in V3?' }));
    const doc = readDocuments(get(backend, '/api/documents', { version: 'v3' }).body)[0];
    backend.handle({ method: 'DELETE', path: `/api/documents/${doc.id}`, query: { version: 'v3' } });
    backend.reset();
    expect(readDocuments(get(backend, '/api/documents', { version: 'v3' }).body)).toHaveLength(3);
    expect(get(backend, `/api/v3/conversations/${conversationId}/messages`).status).toBe(404);
  });

  it('every reply that the app turns into an error is one the API sends', () => {
    // The reasons the app knows how to explain (api-error.ts) must cover everything the mock can answer with.
    const { backend } = setup();
    const replies = [
      get(backend, '/api/documents', { version: 'x' }),
      chat(backend, 'v3', { message: 'seats mock:503' }),
      chat(backend, 'v3', { message: 'seats mock:502' }),
      chat(backend, 'v3', { message: 'seats mock:empty' }),
      upload(backend, 'a.pdf', 'x'),
    ];
    for (const r of replies) {
      const error = ApiError.fromHttp({ status: r.status, error: r.body, headers: { get: (k: string) => r.headers?.[k] ?? null } } as never);
      expect(error.reason).toBe(problemOf(r).reason);
      expect(error.message.length).toBeGreaterThan(10);
    }
  });

  it('returns 404 for routes it does not know and 405 for a wrong method', () => {
    const { backend } = setup();
    expect(get(backend, '/api/nothing/here').status).toBe(404);
    expect(backend.handle({ method: 'PUT', path: '/api/documents', query: {} }).status).toBe(405);
    expect(get(backend, '/api/v3/chat').status).toBe(405);
  });

  it('a stored document view carries no internal fields', () => {
    const { backend } = setup();
    upload(backend, 'new.md', MD());
    const list = get(backend, '/api/documents', { version: 'v3' }).body as DocumentViewDto[];
    for (const d of list) expect(Object.keys(d).sort()).toEqual(['chunkCount', 'error', 'fileName', 'id', 'readyAt', 'status', 'title', 'uploadedAt', 'version']);
  });
});
