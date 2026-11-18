import { ChatCitationDto, SearchDroppedDto, SearchSourceDto } from './backend-dtos';
import { estimateTokens } from './mock-ingest';

/** A stored chunk with the title of its document, as the vector search sees it. */
export interface StoredChunk {
  id: string;
  documentId: string;
  documentTitle: string;
  ordinal: number;
  content: string;
  estTokens: number;
}

/** The values of the "Retrieval" section of appsettings.json (the budget is smaller here so a few chunks already fill it). */
export const MOCK_RETRIEVAL = {
  topK: 20,
  minScore: 0.6,
  /** The API compares embeddings (cosine > 0.95); the mock compares the words of two chunks (Jaccard). */
  duplicateSimilarity: 0.8,
  contextBudgetTokens: 500,
  wrapperTokens: 12,
} as const;

const STOPWORDS = new Set(
  'the and for are but not you all can had her was one our out has have how what when where which who why does did with from that this these those into about there their they them then than its your will would should could any use used using get got make made send sent call calls api apis'.split(' '),
);

/** Cuts the common endings so "change", "changed" and "changes" match: a few suffixes, then a final "e". */
function stem(word: string): string {
  let out = word;
  for (const suffix of ['ing', 'ed', 'es', 's']) {
    if (out.length > suffix.length + 3 && out.endsWith(suffix)) {
      out = out.slice(0, -suffix.length);
      break;
    }
  }
  return out.length > 4 && out.endsWith('e') ? out.slice(0, -1) : out;
}

/** The distinctive words of a text: lower case, no stop words, lightly stemmed. */
export function keywords(text: string): string[] {
  const words = text.toLowerCase().match(/[a-z0-9_]+/g) ?? [];
  // Short words are noise, except a version tag such as "v3".
  return [...new Set(words.filter((w) => (w.length >= 3 || /\d/.test(w)) && !STOPWORDS.has(w)).map(stem))];
}

/** A small stable number in [-1, 1] from an id, so two chunks with the same words do not score exactly the same. */
function jitter(id: string): number {
  let h = 0;
  for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) >>> 0;
  return ((h % 2001) - 1000) / 1000;
}

/** A stand-in for cosine similarity: the share of the question's words found in the chunk, mapped to 0.25 .. 0.90. */
export function scoreChunk(questionWords: string[], chunk: StoredChunk): number {
  if (questionWords.length === 0) return 0.2;
  const words = new Set(keywords(chunk.content));
  const matched = questionWords.filter((w) => words.has(w)).length;
  const coverage = matched / questionWords.length;
  return Math.min(0.97, Math.max(0, 0.25 + 0.65 * coverage + 0.015 * jitter(chunk.id)));
}

function jaccard(a: string, b: string): number {
  const x = new Set(keywords(a));
  const y = new Set(keywords(b));
  if (x.size === 0 || y.size === 0) return 0;
  let shared = 0;
  for (const w of x) if (y.has(w)) shared++;
  return shared / (x.size + y.size - shared);
}

export interface Retrieved {
  chunk: StoredChunk;
  score: number;
  label: string;
}

export interface RetrievalOutcome {
  candidates: number;
  sources: Retrieved[];
  dropped: { chunk: StoredChunk; score: number; reason: SearchDroppedDto['reason'] }[];
}

/** A port of ResultPostProcessor: weak matches, near-duplicates, token budget, document order, labels S1, S2... */
export function retrieve(question: string, chunks: StoredChunk[]): RetrievalOutcome {
  const words = keywords(question);
  const ranked = chunks
    .map((chunk) => ({ chunk, score: scoreChunk(words, chunk) }))
    .sort((a, b) => b.score - a.score)
    .slice(0, MOCK_RETRIEVAL.topK);

  const dropped: RetrievalOutcome['dropped'] = [];

  const strong = ranked.filter((c) => {
    if (c.score >= MOCK_RETRIEVAL.minScore) return true;
    dropped.push({ ...c, reason: 'BelowMinScore' });
    return false;
  });

  const distinct: typeof strong = [];
  for (const c of strong) {
    if (distinct.some((d) => jaccard(d.chunk.content, c.chunk.content) > MOCK_RETRIEVAL.duplicateSimilarity)) {
      dropped.push({ ...c, reason: 'NearDuplicate' });
    } else {
      distinct.push(c);
    }
  }

  const kept: typeof strong = [];
  let used = 0;
  for (const c of distinct) {
    const cost = c.chunk.estTokens + MOCK_RETRIEVAL.wrapperTokens;
    if (kept.length > 0 && used + cost > MOCK_RETRIEVAL.contextBudgetTokens) {
      dropped.push({ ...c, reason: 'OverBudget' });
      continue;
    }
    kept.push(c);
    used += cost;
  }

  const documentRank = new Map<string, number>();
  kept.forEach((c, rank) => {
    if (!documentRank.has(c.chunk.documentId)) documentRank.set(c.chunk.documentId, rank);
  });
  const sources = [...kept]
    .sort((a, b) => documentRank.get(a.chunk.documentId)! - documentRank.get(b.chunk.documentId)! || a.chunk.ordinal - b.chunk.ordinal)
    .map((c, index) => ({ ...c, label: `S${index + 1}` }));

  const rankOf = new Map(ranked.map((c, i) => [c.chunk.id, i]));
  dropped.sort((a, b) => rankOf.get(a.chunk.id)! - rankOf.get(b.chunk.id)!);
  return { candidates: ranked.length, sources, dropped };
}

export const citationText = (title: string, ordinal: number): string => `${title}, part ${ordinal + 1}`;

export const toSearchSource = (s: Retrieved): SearchSourceDto => ({
  label: s.label,
  citation: citationText(s.chunk.documentTitle, s.chunk.ordinal),
  score: Math.round(s.score * 1000) / 1000,
  estTokens: s.chunk.estTokens,
  chunkId: s.chunk.id,
  content: s.chunk.content,
});

export const toSearchDropped = (d: RetrievalOutcome['dropped'][number]): SearchDroppedDto => ({
  citation: citationText(d.chunk.documentTitle, d.chunk.ordinal),
  score: Math.round(d.score * 1000) / 1000,
  reason: d.reason,
  chunkId: d.chunk.id,
});

// ---- The model's side: writing an answer and reading its "Sources:" line --------------------------------------------

/** PromptRules.NotCovered: the answer when retrieval found nothing relevant (the model is not called). */
export const notCovered = (version: string): string =>
  `I couldn't find this in the ${version.toUpperCase()} NDC Portal documentation, so I can't answer it. ` +
  'Try rephrasing, or ask about a specific endpoint, field, enum or booking flow.';

const plain = (line: string): string =>
  line
    .replace(/^[\s>#*\-]+/, '')
    .replace(/\|/g, ' ')
    .replace(/[*_`]/g, '')
    .replace(/\s+/g, ' ')
    .trim();

/** The line of a source that shares most words with the question: what a model would quote. */
function bestLine(question: string, content: string): string {
  const words = keywords(question);
  const lines = content
    .split('\n')
    .map(plain)
    .filter((l) => l.length > 25 && !/^[-: ]+$/.test(l));
  if (lines.length === 0) return plain(content).slice(0, 200);
  let best = lines[0];
  let bestHits = -1;
  for (const line of lines) {
    const lineWords = new Set(keywords(line));
    const hits = words.filter((w) => lineWords.has(w)).length;
    if (hits > bestHits) {
      best = line;
      bestHits = hits;
    }
  }
  return best.length > 260 ? best.slice(0, 257).trimEnd() + '...' : best;
}

/** What the mock "model" writes for the sources it was given: a short Markdown answer ending with a Sources line. */
export function writeAnswer(question: string, version: string, sources: Retrieved[], withSourcesLine = true): string {
  const top = [...sources].sort((a, b) => b.score - a.score).slice(0, 3);
  const bullets = top.map((s) => `- ${bestLine(question, s.chunk.content)}`);
  const text = [`According to the ${version.toUpperCase()} documentation:`, '', ...bullets].join('\n');
  return withSourcesLine ? `${text}\n\nSources: ${top.map((s) => s.label).join(', ')}` : text;
}

export interface ParsedAnswer {
  text: string;
  cited: Retrieved[];
  notCovered: boolean;
  sourcesMissing: boolean;
}

const SOURCES_LINE = /^[\s>*_\-•]*sources?[\s*_]*[:：][\s*_]*(?<list>.*?)[\s*_.]*$/i;
const FALLBACK_CITATIONS = 3;

/** A port of AnswerParser.Parse: splits the Sources line off and finds the sources it names. */
export function parseAnswer(reply: string, sources: Retrieved[]): ParsedAnswer {
  const lines = reply.replace(/\r\n/g, '\n').split('\n');
  let last = -1;
  for (let i = lines.length - 1; i >= 0; i--) {
    if (lines[i].trim()) {
      last = i;
      break;
    }
  }
  if (last < 0) return { text: '', cited: [], notCovered: false, sourcesMissing: false };

  const match = SOURCES_LINE.exec(lines[last]);
  if (!match) return { text: reply.trim(), cited: sources.slice(0, FALLBACK_CITATIONS), notCovered: false, sourcesMissing: true };

  const text = lines.slice(0, last).join('\n').trim();
  const list = match.groups?.['list'] ?? '';
  if (list.trim().toLowerCase() === 'none') return { text, cited: [], notCovered: true, sourcesMissing: false };

  const byLabel = new Map(sources.map((s) => [s.label.toLowerCase(), s]));
  const labels = [...list.matchAll(/(?<![A-Za-z0-9])S(\d+)(?![A-Za-z0-9])/gi)].map((m) => `s${m[1]}`);
  const cited = [...new Set(labels)].filter((l) => byLabel.has(l)).map((l) => byLabel.get(l)!);
  return cited.length > 0
    ? { text, cited, notCovered: false, sourcesMissing: false }
    : { text, cited: sources.slice(0, FALLBACK_CITATIONS), notCovered: false, sourcesMissing: true };
}

export const toCitation = (s: Retrieved): ChatCitationDto => ({
  title: s.chunk.documentTitle,
  part: s.chunk.ordinal + 1,
  text: citationText(s.chunk.documentTitle, s.chunk.ordinal),
});

/** Input tokens of a mock call: a fixed allowance for the rules plus the sources and the question. */
export function inputTokens(question: string, sources: Retrieved[]): number {
  return 240 + estimateTokens(question) + sources.reduce((sum, s) => sum + s.chunk.estTokens + MOCK_RETRIEVAL.wrapperTokens, 0);
}
