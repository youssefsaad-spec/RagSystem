import { ApiError } from './api-error';
import { readChatResponse, readChunks, readCitations, readDocument, readDocuments, readHistory, readReport, readSearch } from './models';

const ID = '019a8c0e-7b8d-7c3a-9b1e-0123456789ab';

describe('readChatResponse', () => {
  it('reads the answer of POST /chat', () => {
    const r = readChatResponse({
      conversationId: ID,
      messageId: 'm1',
      answer: 'Use FareConfirmOffer.',
      citations: [{ title: 'NDC Portal APIs V3', part: 12, text: 'NDC Portal APIs V3, part 12' }],
      usage: { inputTokens: 10, outputTokens: 5, thinkingTokens: 0, cacheHit: true },
    });
    expect(r.conversationId).toBe(ID);
    expect(r.citations).toEqual([{ title: 'NDC Portal APIs V3', part: 12, text: 'NDC Portal APIs V3, part 12' }]);
    expect(r.usage.cacheHit).toBe(true);
  });

  it('fails loudly on a body that is not the API body, instead of showing "undefined"', () => {
    expect(() => readChatResponse('<html>')).toThrow(ApiError);
    expect(() => readChatResponse({ conversationId: ID })).toThrow(ApiError);
    expect(() => readChatResponse({ conversationId: ID, messageId: 'm', answer: '   ' })).toThrow(ApiError);
  });

  it('defaults missing usage to zero', () => {
    const r = readChatResponse({ conversationId: ID, messageId: 'm', answer: 'a' });
    expect(r.usage).toEqual({ inputTokens: 0, outputTokens: 0, thinkingTokens: 0, cacheHit: false });
    expect(r.citations).toEqual([]);
  });
});

describe('readCitations', () => {
  it('drops empty and duplicate sources and builds the text when it is missing', () => {
    const c = readCitations([
      { title: 'Doc', part: 2, text: 'Doc, part 2' },
      { title: 'Doc', part: 2, text: 'Doc, part 2' },
      { title: 'Other', part: 3 },
      {},
      null,
    ]);
    expect(c.map((x) => x.text)).toEqual(['Doc, part 2', 'Other, part 3']);
  });

  it('tolerates a missing list', () => {
    expect(readCitations(undefined)).toEqual([]);
  });
});

describe('readHistory', () => {
  it('keeps the messages in time order and drops rows it cannot use', () => {
    const list = readHistory([
      {
        id: 'b',
        role: 'assistant',
        content: 'A',
        createdAt: '2026-11-17T10:00:02Z',
        citations: [],
        usage: { inputTokens: 1, outputTokens: 1, thinkingTokens: 0, cacheHit: false },
      },
      { id: 'a', role: 'user', content: 'Q', createdAt: '2026-11-17T10:00:01Z', citations: [], usage: null },
      { id: 'x', role: 'system', content: 'ignored', createdAt: '2026-11-17T10:00:03Z' },
      'junk',
    ]);
    expect(list.map((m) => m.id)).toEqual(['a', 'b']);
    expect(list[0].usage).toBeNull();
    expect(list[1].usage?.inputTokens).toBe(1);
  });

  it('rejects a body that is not a list', () => {
    expect(() => readHistory({})).toThrow(ApiError);
  });
});

describe('documents', () => {
  const doc = {
    id: ID,
    version: 'v3',
    fileName: 'ndc-portal-apis-v3.md',
    title: 'NDC Portal APIs V3',
    status: 'Ready',
    chunkCount: 42,
    error: null,
    uploadedAt: '2026-11-17T10:00:00Z',
    readyAt: '2026-11-17T10:00:09Z',
  };

  it('reads a document and a list of documents', () => {
    expect(readDocument(doc)).toMatchObject({
      fileName: 'ndc-portal-apis-v3.md',
      title: 'NDC Portal APIs V3',
      chunkCount: 42,
      readyAt: '2026-11-17T10:00:09Z',
    });
    expect(readDocuments([doc, { ...doc, id: 'x', status: 'Processing', readyAt: null }])).toHaveLength(2);
  });

  it('falls back to the file name when a document has no title yet', () => {
    expect(readDocument({ ...doc, title: undefined }).title).toBe('ndc-portal-apis-v3.md');
  });

  it('reads chunks', () => {
    const [c] = readChunks([{ id: 'c1', ordinal: 0, content: '# Intro', estTokens: 3, embeddingModel: 'gemini-embedding-001' }]);
    expect(c).toEqual({ id: 'c1', ordinal: 0, content: '# Intro', estTokens: 3, embeddingModel: 'gemini-embedding-001' });
  });

  it('rejects lists that are not arrays', () => {
    expect(() => readDocuments({})).toThrow(ApiError);
    expect(() => readChunks(null)).toThrow(ApiError);
  });
});

describe('readReport', () => {
  it('reads a dry run with its chunks', () => {
    const r = readReport({
      fileName: 'a.md',
      version: 'v3',
      title: 'A',
      valid: true,
      errors: [],
      warnings: [{ code: 'W1', message: 'careful', line: 4 }],
      chunks: [{ ordinal: 0, text: 'hello', estTokens: 2 }],
    });
    expect(r.valid).toBe(true);
    expect(r.warnings[0]).toEqual({ code: 'W1', message: 'careful', line: 4 });
    expect(r.chunks).toEqual([{ ordinal: 0, text: 'hello', estTokens: 2 }]);
  });

  it('reads a rejected upload (422), which has no chunks', () => {
    const r = readReport({
      fileName: 'a.md',
      version: 'v3',
      title: '',
      valid: false,
      errors: [{ code: 'E1', message: 'bad', line: null }],
      warnings: [],
      chunks: [],
    });
    expect(r.valid).toBe(false);
    expect(r.errors[0].line).toBeNull();
    expect(r.chunks).toEqual([]);
  });
});

describe('readSearch', () => {
  it('reads kept sources and dropped candidates', () => {
    const r = readSearch({
      version: 'v3',
      question: 'seats?',
      candidates: 3,
      sources: [{ label: 'S1', citation: 'Doc, part 1', score: 0.81, estTokens: 120, chunkId: 'c1', content: 'text' }],
      dropped: [{ citation: 'Doc, part 5', score: 0.41, reason: 'BelowMinScore', chunkId: 'c2' }],
    });
    expect(r.candidates).toBe(3);
    expect(r.sources[0]).toMatchObject({ label: 'S1', score: 0.81 });
    expect(r.dropped[0].reason).toBe('BelowMinScore');
  });

  it('rejects a body without the lists', () => {
    expect(() => readSearch({ version: 'v3' })).toThrow(ApiError);
  });
});
