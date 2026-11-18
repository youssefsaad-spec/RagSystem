import { ApiError } from './api-error';

export type ApiVersion = 'v2' | 'v3';
export const API_VERSIONS: readonly ApiVersion[] = ['v2', 'v3'];

export function isApiVersion(value: unknown): value is ApiVersion {
  return typeof value === 'string' && (API_VERSIONS as readonly string[]).includes(value);
}

export function versionLabel(version: ApiVersion): string {
  return version.toUpperCase();
}

/** The question limit of the API: the estimate (characters / 3.5, rounded up) must be at most 1,000 tokens. */
export const MAX_QUESTION_TOKENS = 1000;
export const CHARS_PER_TOKEN = 3.5;
export const MAX_QUESTION_CHARS = Math.floor(MAX_QUESTION_TOKENS * CHARS_PER_TOKEN);

export function estimateTokens(text: string): number {
  return text ? Math.ceil(text.length / CHARS_PER_TOKEN) : 0;
}

/** The server's upload limit (UploadLimits.MaxBytes, 10 MB). The server is the authority and answers 413 above it. */
export const DEFAULT_MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

// ---- Chat -----------------------------------------------------------------------------------------------------

/** A source of an answer: the document title and the part number in it (the first part is 1). */
export interface Citation {
  title: string;
  part: number;
  /** For example "NDC Portal APIs V3, part 12". */
  text: string;
}

export interface ChatUsage {
  inputTokens: number;
  outputTokens: number;
  thinkingTokens: number;
  cacheHit: boolean;
}

export interface ChatResponse {
  conversationId: string;
  messageId: string;
  answer: string;
  citations: Citation[];
  usage: ChatUsage;
}

export interface HistoryMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  createdAt: string;
  citations: Citation[];
  usage: ChatUsage | null;
}

// ---- Documents ------------------------------------------------------------------------------------------------

export type DocumentStatus = 'Processing' | 'Ready' | 'Failed' | string;

export interface DocumentView {
  id: string;
  version: string;
  fileName: string;
  title: string;
  status: DocumentStatus;
  chunkCount: number;
  error: string | null;
  uploadedAt: string;
  readyAt: string | null;
}

export interface ChunkView {
  id: string;
  ordinal: number;
  content: string;
  estTokens: number;
  embeddingModel: string;
}

export interface Issue {
  code: string;
  message: string;
  line: number | null;
}

export interface ReportChunk {
  ordinal: number;
  text: string;
  estTokens: number;
}

/** What a dry run answers, and what a rejected upload (422) answers without its chunks. */
export interface IngestReport {
  fileName: string;
  version: string;
  title: string;
  valid: boolean;
  errors: Issue[];
  warnings: Issue[];
  chunks: ReportChunk[];
}

export type UploadOutcome = { kind: 'accepted'; id: string; status: string } | { kind: 'dryRun'; report: IngestReport };

// ---- Search ---------------------------------------------------------------------------------------------------

export interface SearchSource {
  /** S1, S2... as the model would cite it. */
  label: string;
  citation: string;
  score: number;
  estTokens: number;
  chunkId: string;
  content: string;
}

export interface SearchDropped {
  citation: string;
  score: number;
  /** BelowMinScore, NearDuplicate or OverBudget. */
  reason: string;
  chunkId: string;
}

export interface SearchResult {
  version: string;
  question: string;
  /** How many chunks the vector search found before post-processing. */
  candidates: number;
  sources: SearchSource[];
  dropped: SearchDropped[];
}

// ---- Runtime validation ---------------------------------------------------------------------------------------
// The API is trusted, but a proxy or a wrong base URL can return anything with a 200. These readers fail loudly
// with a bad_response error instead of rendering "undefined".

type Obj = Record<string, unknown>;

function obj(v: unknown, what: string): Obj {
  if (!v || typeof v !== 'object' || Array.isArray(v)) throw ApiError.badResponse(`${what} is not an object`);
  return v as Obj;
}

function str(o: Obj, key: string, what: string): string {
  const v = o[key];
  if (typeof v !== 'string') throw ApiError.badResponse(`${what}.${key} is missing`);
  return v;
}

const optStr = (o: Obj, key: string): string | null => (typeof o[key] === 'string' ? (o[key] as string) : null);
const num = (o: Obj, key: string, fallback = 0): number =>
  typeof o[key] === 'number' && Number.isFinite(o[key]) ? (o[key] as number) : fallback;
const optNum = (o: Obj, key: string): number | null =>
  typeof o[key] === 'number' && Number.isFinite(o[key]) ? (o[key] as number) : null;

function array(v: unknown, what: string): unknown[] {
  if (!Array.isArray(v)) throw ApiError.badResponse(`${what} is not an array`);
  return v;
}

export function readCitations(v: unknown): Citation[] {
  if (!Array.isArray(v)) return [];
  const seen = new Set<string>();
  const out: Citation[] = [];
  for (const item of v) {
    if (!item || typeof item !== 'object') continue;
    const c = item as Obj;
    const title = typeof c['title'] === 'string' ? c['title'].trim() : '';
    const part = typeof c['part'] === 'number' && Number.isFinite(c['part']) ? (c['part'] as number) : 0;
    const text = typeof c['text'] === 'string' && c['text'].trim() ? c['text'].trim() : part > 0 ? `${title}, part ${part}` : title;
    if (!text) continue;
    if (seen.has(text)) continue;
    seen.add(text);
    out.push({ title: title || text, part, text });
  }
  return out;
}

export function readUsage(v: unknown): ChatUsage | null {
  if (!v || typeof v !== 'object') return null;
  const u = v as Obj;
  return {
    inputTokens: num(u, 'inputTokens'),
    outputTokens: num(u, 'outputTokens'),
    thinkingTokens: num(u, 'thinkingTokens'),
    cacheHit: u['cacheHit'] === true,
  };
}

export function readChatResponse(body: unknown): ChatResponse {
  const o = obj(body, 'chat response');
  const answer = str(o, 'answer', 'chat response');
  if (!answer.trim()) throw ApiError.badResponse('the answer is empty');
  return {
    conversationId: str(o, 'conversationId', 'chat response'),
    messageId: str(o, 'messageId', 'chat response'),
    answer,
    citations: readCitations(o['citations']),
    usage: readUsage(o['usage']) ?? { inputTokens: 0, outputTokens: 0, thinkingTokens: 0, cacheHit: false },
  };
}

export function readHistory(body: unknown): HistoryMessage[] {
  const list = array(body, 'the message list');
  const out: HistoryMessage[] = [];
  for (const item of list) {
    if (!item || typeof item !== 'object') continue;
    const m = item as Obj;
    const role = m['role'];
    if ((role !== 'user' && role !== 'assistant') || typeof m['id'] !== 'string' || typeof m['content'] !== 'string') continue;
    out.push({
      id: m['id'] as string,
      role,
      content: m['content'] as string,
      createdAt: typeof m['createdAt'] === 'string' ? (m['createdAt'] as string) : '',
      citations: readCitations(m['citations']),
      usage: role === 'assistant' ? readUsage(m['usage']) : null,
    });
  }
  // Stable by time; equal or missing timestamps keep the server order.
  return out
    .map((m, i) => ({ m, i, t: Date.parse(m.createdAt) }))
    .sort((a, b) => (Number.isNaN(a.t) || Number.isNaN(b.t) || a.t === b.t ? a.i - b.i : a.t - b.t))
    .map((x) => x.m);
}

export function readDocument(v: unknown): DocumentView {
  const o = obj(v, 'document');
  const fileName = str(o, 'fileName', 'document');
  return {
    id: str(o, 'id', 'document'),
    version: optStr(o, 'version') ?? '',
    fileName,
    title: optStr(o, 'title') ?? fileName,
    status: optStr(o, 'status') ?? 'Unknown',
    chunkCount: num(o, 'chunkCount'),
    error: optStr(o, 'error'),
    uploadedAt: optStr(o, 'uploadedAt') ?? '',
    readyAt: optStr(o, 'readyAt'),
  };
}

export function readDocuments(body: unknown): DocumentView[] {
  return array(body, 'the document list').map(readDocument);
}

export function readChunks(body: unknown): ChunkView[] {
  return array(body, 'the chunk list').map((item) => {
    const o = obj(item, 'chunk');
    return {
      id: str(o, 'id', 'chunk'),
      ordinal: num(o, 'ordinal'),
      content: optStr(o, 'content') ?? '',
      estTokens: num(o, 'estTokens'),
      embeddingModel: optStr(o, 'embeddingModel') ?? '',
    };
  });
}

function readIssues(v: unknown): Issue[] {
  if (!Array.isArray(v)) return [];
  return v
    .filter((x): x is Obj => !!x && typeof x === 'object')
    .map((i) => ({
      code: optStr(i, 'code') ?? 'issue',
      message: optStr(i, 'message') ?? '',
      line: optNum(i, 'line'),
    }));
}

export function readReport(body: unknown): IngestReport {
  const o = obj(body, 'report');
  const rawChunks = o['chunks'];
  return {
    fileName: optStr(o, 'fileName') ?? '',
    version: optStr(o, 'version') ?? '',
    title: optStr(o, 'title') ?? '',
    valid: o['valid'] === true,
    errors: readIssues(o['errors']),
    warnings: readIssues(o['warnings']),
    chunks: Array.isArray(rawChunks)
      ? rawChunks
          .filter((x): x is Obj => !!x && typeof x === 'object')
          .map((c) => ({ ordinal: num(c, 'ordinal'), text: optStr(c, 'text') ?? '', estTokens: num(c, 'estTokens') }))
      : [],
  };
}

export function readSearch(body: unknown): SearchResult {
  const o = obj(body, 'search result');
  const sources = array(o['sources'], 'search result.sources').map((item) => {
    const s = obj(item, 'source');
    return {
      label: optStr(s, 'label') ?? '',
      citation: optStr(s, 'citation') ?? '',
      score: num(s, 'score'),
      estTokens: num(s, 'estTokens'),
      chunkId: optStr(s, 'chunkId') ?? '',
      content: optStr(s, 'content') ?? '',
    };
  });
  const dropped = array(o['dropped'], 'search result.dropped').map((item) => {
    const d = obj(item, 'dropped candidate');
    return {
      citation: optStr(d, 'citation') ?? '',
      score: num(d, 'score'),
      reason: optStr(d, 'reason') ?? '',
      chunkId: optStr(d, 'chunkId') ?? '',
    };
  });
  return {
    version: str(o, 'version', 'search result'),
    question: optStr(o, 'question') ?? '',
    candidates: num(o, 'candidates'),
    sources,
    dropped,
  };
}
