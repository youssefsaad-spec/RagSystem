import {
  AcceptedDocumentDto,
  ChatRequestDto,
  ChatResponseDto,
  ChunkViewDto,
  DocumentViewDto,
  MessageViewDto,
  ProblemDto,
  SearchResponseDto,
} from './backend-dtos';
import { SEED_DOCUMENTS } from './mock-corpus';
import { estimateTokens, processFile } from './mock-ingest';
import {
  inputTokens,
  notCovered,
  parseAnswer,
  retrieve,
  StoredChunk,
  toCitation,
  toSearchDropped,
  toSearchSource,
  writeAnswer,
} from './mock-retrieval';

/** A request as the mock sees it: the path without its query string, the query as a map, and the parsed body. */
export interface MockRequest {
  method: string;
  path: string;
  query: Record<string, string>;
  body?: unknown;
  /** For a multipart upload: the file that was sent. */
  file?: { name: string; size: number; text: string } | null;
  multipart?: boolean;
}

/** What the API would answer. `status` 0 means the connection failed. */
export interface MockReply {
  status: number;
  body?: unknown;
  headers?: Record<string, string>;
  /** How long the real call would take, in milliseconds. */
  delayMs: number;
}

interface StoredDocument extends DocumentViewDto {
  /** Chunk texts waiting to be stored once processing ends. */
  pending?: string[];
  /** The time (ms) at which processing ends. */
  processAt?: number;
}

interface StoredConversation {
  id: string;
  version: string;
  messages: MessageViewDto[];
}

export interface MockState {
  documents: StoredDocument[];
  chunks: StoredChunk[];
  conversations: StoredConversation[];
}

export interface MockStorage {
  load(): MockState | null;
  save(state: MockState): void;
}

export interface MockOptions {
  storage?: MockStorage;
  now?: () => number;
  newId?: () => string;
  /** How long a stored document stays Processing. */
  processingMs?: number;
  /** 0 removes every delay (for tests); 1 is realistic. */
  latency?: number;
}

/**
 * Words in a chat message that make the mock fail or behave in a special way, so every error screen can be reached
 * without breaking a real service. They are removed before the question is searched.
 */
export const MOCK_TRIGGERS = {
  'mock:503': 'the model service is busy: 503 llm_unavailable with Retry-After 5',
  'mock:502': 'the model refuses the request: 502 llm_error',
  'mock:empty': 'the model returns no text: 502 empty_answer',
  'mock:network': 'the connection fails (status 0)',
  'mock:slow': 'the answer takes 9 seconds',
  'mock:nosources': 'the model forgets its Sources line: the first 3 sources are cited',
  'mock:none': 'the model says the documentation does not cover it (Sources: none)',
  'mock:cache': 'an answer from the cache: usage is all zeros with cacheHit true',
} as const;

const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;
const MAX_QUESTION_TOKENS = 1000;
const VERSIONS = ['v2', 'v3'];
const GUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';

function problem(status: number, reason: string, title: string, detail: string, delayMs = 60, headers?: Record<string, string>): MockReply {
  const body: ProblemDto = { title, status, detail, reason };
  return { status, body, headers, delayMs };
}

const ok = (body: unknown, delayMs: number, status = 200, headers?: Record<string, string>): MockReply => ({ status, body, delayMs, headers });

function defaultId(): string {
  try {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  } catch {
    /* fall back */
  }
  const hex = () => Math.floor(Math.random() * 16).toString(16);
  return 'xxxxxxxx-xxxx-4xxx-8xxx-xxxxxxxxxxxx'.replace(/x/g, hex);
}

/**
 * The RagSystem API, in memory. It follows the same rules as the real one (validation order, status codes, `reason`
 * values, the retrieval steps, the Sources line) and answers with the shapes in backend-dtos.ts. It is not a model:
 * answers are short quotes from the best sources.
 */
export class MockBackend {
  private state: MockState;
  private readonly storage?: MockStorage;
  private readonly now: () => number;
  private readonly newId: () => string;
  private readonly processingMs: number;
  private readonly latency: number;

  constructor(options: MockOptions = {}) {
    this.storage = options.storage;
    this.now = options.now ?? Date.now;
    this.newId = options.newId ?? defaultId;
    this.processingMs = options.processingMs ?? 4000;
    this.latency = options.latency ?? 1;
    this.state = this.storage?.load() ?? this.seed();
    this.save();
  }

  /** Back to the documents the mock starts with, and no conversations. */
  reset(): void {
    this.state = this.seed();
    this.save();
  }

  handle(req: MockRequest): MockReply {
    const reply = this.route(req);
    return { ...reply, delayMs: Math.round(reply.delayMs * this.latency) };
  }

  // ---- Routing -------------------------------------------------------------------------------------------------

  private route(req: MockRequest): MockReply {
    this.settle();
    const path = req.path.replace(/\/+$/, '');
    const method = req.method.toUpperCase();
    let m: RegExpMatchArray | null;

    if (path.toLowerCase() === '/api/documents') {
      if (method === 'GET') return this.listDocuments(req);
      if (method === 'POST') return this.upload(req);
      return { status: 405, delayMs: 20 };
    }
    if ((m = path.match(new RegExp(`^/api/documents/(${GUID})$`, 'i')))) {
      if (method === 'GET') return this.getDocument(req, m[1]);
      if (method === 'DELETE') return this.deleteDocument(req, m[1]);
      return { status: 405, delayMs: 20 };
    }
    if ((m = path.match(new RegExp(`^/api/documents/(${GUID})/chunks$`, 'i')))) {
      return method === 'GET' ? this.chunks(req, m[1]) : { status: 405, delayMs: 20 };
    }
    if ((m = path.match(/^\/api\/([^/]+)\/chat$/i))) {
      return method === 'POST' ? this.chat(m[1], req.body as ChatRequestDto | null) : { status: 405, delayMs: 20 };
    }
    if ((m = path.match(/^\/api\/([^/]+)\/search$/i))) {
      return method === 'GET' ? this.search(m[1], req.query['q']) : { status: 405, delayMs: 20 };
    }
    if ((m = path.match(new RegExp(`^/api/([^/]+)/conversations/(${GUID})/messages$`, 'i')))) {
      return method === 'GET' ? this.messages(m[1], m[2]) : { status: 405, delayMs: 20 };
    }
    return { status: 404, delayMs: 20 };
  }

  private version(raw: string | undefined): string | null {
    return VERSIONS.find((v) => v === raw?.toLowerCase()) ?? null;
  }

  private unknownVersion(): MockReply {
    return problem(400, 'unknown_version', 'Unknown version', `Set the 'version' query parameter to one of: ${VERSIONS.join(', ')}.`);
  }

  // ---- Documents -----------------------------------------------------------------------------------------------

  private view(d: StoredDocument): DocumentViewDto {
    const { pending: _pending, processAt: _processAt, ...view } = d;
    return view;
  }

  private listDocuments(req: MockRequest): MockReply {
    const version = this.version(req.query['version']);
    if (!version) return this.unknownVersion();
    const list = this.state.documents
      .filter((d) => d.version === version)
      .sort((a, b) => a.fileName.localeCompare(b.fileName, 'en', { sensitivity: 'base' }))
      .map((d) => this.view(d));
    return ok(list, 150);
  }

  private getDocument(req: MockRequest, id: string): MockReply {
    const version = this.version(req.query['version']);
    if (!version) return this.unknownVersion();
    const doc = this.state.documents.find((d) => d.version === version && d.id === id);
    return doc ? ok(this.view(doc), 100) : this.documentNotFound(id);
  }

  private documentNotFound(id: string): MockReply {
    return problem(404, 'document_not_found', 'Document not found', `There is no document ${id} in this version.`);
  }

  private deleteDocument(req: MockRequest, id: string): MockReply {
    const version = this.version(req.query['version']);
    if (!version) return this.unknownVersion();
    const doc = this.state.documents.find((d) => d.version === version && d.id === id);
    if (!doc) return this.documentNotFound(id);
    this.state.documents = this.state.documents.filter((d) => d.id !== id);
    this.state.chunks = this.state.chunks.filter((c) => c.documentId !== id);
    this.save();
    return { status: 204, delayMs: 200 };
  }

  private chunks(req: MockRequest, id: string): MockReply {
    const version = this.version(req.query['version']);
    if (!version) return this.unknownVersion();
    if (!this.state.documents.some((d) => d.version === version && d.id === id)) return this.documentNotFound(id);
    const skip = Math.max(Number.parseInt(req.query['skip'] ?? '0', 10) || 0, 0);
    const requested = Number.parseInt(req.query['take'] ?? '100', 10);
    const take = Math.min(Math.max(Number.isNaN(requested) ? 100 : requested, 1), 500);
    const rows: ChunkViewDto[] = this.state.chunks
      .filter((c) => c.documentId === id)
      .sort((a, b) => a.ordinal - b.ordinal)
      .slice(skip, skip + take)
      .map((c) => ({ id: c.id, ordinal: c.ordinal, content: c.content, estTokens: c.estTokens, embeddingModel: 'gemini-embedding-001' }));
    return ok(rows, 150);
  }

  private upload(req: MockRequest): MockReply {
    const version = this.version(req.query['version']);
    if (!version) return this.unknownVersion();
    if (!req.multipart) return problem(400, 'not_multipart', 'Not a form upload', "Send the file as multipart/form-data in the field 'file'.");
    const file = req.file;
    if (!file || file.size === 0) return problem(400, 'no_file', 'No file', 'The form has no file, or the file is empty.');
    if (file.size > MAX_UPLOAD_BYTES) return problem(413, 'file_too_large', 'File too large', `The limit is ${MAX_UPLOAD_BYTES} bytes.`);
    if (!/\.(md|json)$/i.test(file.name)) {
      return problem(415, 'unsupported_file_type', 'Unsupported file type', 'Use a .md file or an OpenAPI .json file.');
    }

    const report = processFile(file.name, version, file.text);
    const dryRun = req.query['dryRun'] === 'true';
    const readDelay = 250 + Math.min(file.size / 400, 900);
    if (dryRun) return ok(report, readDelay);
    if (!report.valid) return ok({ ...report, chunks: [] }, readDelay, 422);

    const existing = this.state.documents.find((d) => d.version === version && d.fileName === file.name);
    if (existing?.status === 'Processing') {
      return problem(
        409,
        'document_processing',
        'Already processing',
        `Document ${existing.id} is still being processed. Wait until it is Ready or Failed, then upload again.`,
      );
    }

    const id = existing?.id ?? this.newId();
    const doc: StoredDocument = {
      id,
      version,
      fileName: file.name,
      title: report.title,
      status: 'Processing',
      chunkCount: existing?.chunkCount ?? 0,
      error: null,
      uploadedAt: new Date(this.now()).toISOString(),
      readyAt: null,
      pending: report.chunks.map((c) => c.text),
      processAt: this.now() + this.processingMs,
    };
    this.state.documents = existing ? this.state.documents.map((d) => (d.id === id ? doc : d)) : [...this.state.documents, doc];
    this.save();
    const accepted: AcceptedDocumentDto = { id, status: 'Processing' };
    return ok(accepted, readDelay, 202, { Location: `/api/documents/${id}?version=${version}` });
  }

  /** The background worker: documents whose time has come become Ready (or Failed, for a file named "...fail..."). */
  private settle(): void {
    let changed = false;
    for (const doc of this.state.documents) {
      if (doc.status !== 'Processing' || doc.processAt === undefined || doc.processAt > this.now()) continue;
      changed = true;
      const endedAt = new Date(doc.processAt).toISOString();
      if (doc.fileName.toLowerCase().includes('fail')) {
        doc.status = 'Failed';
        doc.error = 'The model service refused the request: mock failure (the file name contains "fail").';
      } else {
        const texts = doc.pending ?? [];
        this.state.chunks = this.state.chunks.filter((c) => c.documentId !== doc.id);
        texts.forEach((content, ordinal) =>
          this.state.chunks.push({
            id: this.newId(),
            documentId: doc.id,
            documentTitle: doc.title,
            ordinal,
            content,
            estTokens: estimateTokens(content),
          }),
        );
        doc.status = 'Ready';
        doc.chunkCount = texts.length;
        doc.readyAt = endedAt;
      }
      delete doc.pending;
      delete doc.processAt;
    }
    if (changed) this.save();
  }

  // ---- Search and chat -----------------------------------------------------------------------------------------

  private chunksOf(version: string): StoredChunk[] {
    const ready = new Set(this.state.documents.filter((d) => d.version === version && d.status === 'Ready').map((d) => d.id));
    return this.state.chunks.filter((c) => ready.has(c.documentId));
  }

  private checkQuestion(version: string | null, question: string | null | undefined, field: string): MockReply | null {
    if (!version) return this.unknownVersion();
    if (!question || !question.trim()) {
      return problem(400, 'empty_question', 'Empty question', `Set ${field} to your question.`);
    }
    if (estimateTokens(question) > MAX_QUESTION_TOKENS) {
      return problem(400, 'question_too_long', 'Question too long', `The limit is ${MAX_QUESTION_TOKENS} tokens (about ${MAX_QUESTION_TOKENS * 3.5} characters).`);
    }
    return null;
  }

  private search(rawVersion: string, q: string | undefined): MockReply {
    const version = this.version(rawVersion);
    const refused = this.checkQuestion(version, q, "the 'q' query parameter");
    if (refused) return refused;

    const outcome = retrieve(q!, this.chunksOf(version!));
    const body: SearchResponseDto = {
      version: version!,
      question: q!,
      candidates: outcome.candidates,
      sources: outcome.sources.map(toSearchSource),
      dropped: outcome.dropped.map(toSearchDropped),
    };
    return ok(body, 350);
  }

  private chat(rawVersion: string, request: ChatRequestDto | null): MockReply {
    const version = this.version(rawVersion);
    const message = request?.message;
    const refused = this.checkQuestion(version, message, "'message' in the request body");
    if (refused) return refused;

    let conversation: StoredConversation | undefined;
    if (request?.conversationId) {
      conversation = this.state.conversations.find((c) => c.id === request.conversationId && c.version === version);
      if (!conversation) return this.conversationNotFound();
    }

    const triggers = Object.keys(MOCK_TRIGGERS).filter((t) => message!.toLowerCase().includes(t));
    const has = (t: keyof typeof MOCK_TRIGGERS) => triggers.includes(t);
    const question = triggers.reduce((text, t) => text.replace(new RegExp(t, 'gi'), ''), message!).trim() || message!;
    const delayMs = has('mock:slow') ? 9000 : 700 + (question.length % 7) * 90;

    if (has('mock:network')) return { status: 0, delayMs: 300 };
    if (has('mock:503')) {
      return problem(503, 'llm_unavailable', 'Model service unavailable', 'The model service is not ready or is busy. Try again shortly.', 500, { 'Retry-After': '5' });
    }

    // Query rewrite (F27): a short follow-up is joined with the previous question, as the API's fallback does.
    const history = conversation?.messages.filter((m) => m.role === 'user') ?? [];
    const previous = history[history.length - 1]?.content;
    const words = question.split(/\s+/).filter(Boolean).length;
    const query = previous && words <= 5 ? `${previous} ${question}` : question;

    const outcome = retrieve(query, this.chunksOf(version!));

    let answer: string;
    let cited = outcome.sources.slice(0, 0);
    let usage = { inputTokens: 0, outputTokens: 0, thinkingTokens: 0, cacheHit: false };

    if (outcome.sources.length === 0) {
      answer = notCovered(version!); // no relevant chunks: the model is not called
    } else {
      if (has('mock:502')) return problem(502, 'llm_error', 'Model error', 'The model service refused the request: mock error (API key not valid).', 600);
      if (has('mock:empty')) return problem(502, 'empty_answer', 'Empty answer', 'The model returned no answer. Try again, or rephrase the question.', 600);

      const reply = has('mock:none')
        ? 'The documentation you gave me does not cover this question.\n\nSources: none'
        : writeAnswer(question, version!, outcome.sources, !has('mock:nosources'));
      const parsed = parseAnswer(reply, outcome.sources);
      answer = parsed.text;
      cited = parsed.cited;
      usage = has('mock:cache')
        ? { inputTokens: 0, outputTokens: 0, thinkingTokens: 0, cacheHit: true }
        : { inputTokens: inputTokens(question, outcome.sources), outputTokens: estimateTokens(reply), thinkingTokens: 0, cacheHit: false };
    }

    // Saving the turn: the conversation is created on the first message.
    const at = this.now();
    if (!conversation) {
      conversation = { id: this.newId(), version: version!, messages: [] };
      this.state.conversations.push(conversation);
    }
    const citations = cited.map(toCitation);
    const questionId = this.newId();
    const answerId = this.newId();
    conversation.messages.push(
      { id: questionId, role: 'user', content: message!, citations: [], usage: null, createdAt: new Date(at).toISOString() },
      { id: answerId, role: 'assistant', content: answer, citations, usage, createdAt: new Date(at + 1).toISOString() },
    );
    this.save();

    const body: ChatResponseDto = { conversationId: conversation.id, messageId: answerId, answer, citations, usage };
    return ok(body, delayMs);
  }

  private conversationNotFound(): MockReply {
    return problem(404, 'conversation_not_found', 'Conversation not found', 'There is no conversation with this id for this version.');
  }

  private messages(rawVersion: string, id: string): MockReply {
    const version = this.version(rawVersion);
    if (!version) return this.unknownVersion();
    const conversation = this.state.conversations.find((c) => c.id === id && c.version === version);
    return conversation ? ok(conversation.messages, 200) : this.conversationNotFound();
  }

  // ---- State ---------------------------------------------------------------------------------------------------

  private seed(): MockState {
    const documents: StoredDocument[] = [];
    const chunks: StoredChunk[] = [];
    const day = 86_400_000;
    SEED_DOCUMENTS.forEach((seed, i) => {
      const id = `d0000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}`;
      const uploadedAt = this.now() - (3 - (i % 3)) * day;
      documents.push({
        id,
        version: seed.version,
        fileName: seed.fileName,
        title: seed.title,
        status: seed.status,
        chunkCount: seed.chunks.length,
        error: seed.error ?? null,
        uploadedAt: new Date(uploadedAt).toISOString(),
        readyAt: seed.status === 'Ready' ? new Date(uploadedAt + 9000).toISOString() : null,
      });
      seed.chunks.forEach((content, ordinal) =>
        chunks.push({
          id: `c${String(i + 1).padStart(3, '0')}0000-0000-4000-8000-${String(ordinal + 1).padStart(12, '0')}`,
          documentId: id,
          documentTitle: seed.title,
          ordinal,
          content,
          estTokens: estimateTokens(content),
        }),
      );
    });
    return { documents, chunks, conversations: [] };
  }

  private save(): void {
    this.storage?.save(this.state);
  }
}
