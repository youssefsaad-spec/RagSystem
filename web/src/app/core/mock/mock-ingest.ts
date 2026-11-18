import { ChunkReportDto, DryRunReportDto, IssueReportDto } from './backend-dtos';

/** The same estimate as the API's TokenEstimator: ceil(chars / 3.5). */
export function estimateTokens(text: string): number {
  return Math.ceil(text.length / 3.5);
}

const MAX_TOKENS_PER_CHUNK = 500;
const DOC_TYPES = ['guide', 'whats-new'];
const REQUIRED = ['version', 'title', 'docType', 'updated'];

const error = (code: string, message: string, line: number | null): IssueReportDto => ({ code, message, line });

interface FrontMatter {
  title: string;
  body: string;
}

/** A port of Ingestion/Markdown/FrontMatterReader.cs: same rules, same codes. */
function readFrontMatter(text: string, uploadVersion: string): { matter: FrontMatter | null; errors: IssueReportDto[] } {
  const lines = text.replace(/^﻿/, '').replace(/\r\n/g, '\n').split('\n');
  if (lines[0].trim() !== '---') {
    return {
      matter: null,
      errors: [error('frontmatter.missing', "The file must start with a header between two '---' lines, with version, title, docType and updated.", 1)],
    };
  }
  const end = lines.findIndex((line, i) => i > 0 && line.trim() === '---');
  if (end < 0) return { matter: null, errors: [error('frontmatter.unterminated', "The header has no closing '---' line.", 1)] };

  const body = lines.slice(end + 1).join('\n');
  const values = new Map<string, string>();
  const errors: IssueReportDto[] = [];
  for (let i = 1; i < end; i++) {
    const line = lines[i].trim();
    if (!line || line.startsWith('#')) continue;
    const colon = line.indexOf(':');
    if (colon <= 0) {
      errors.push(error('frontmatter.invalid', `This header line is not 'key: value': ${line}`, i + 1));
      continue;
    }
    let value = line.slice(colon + 1).trim();
    if (value.length >= 2 && value[0] === value[value.length - 1] && (value[0] === '"' || value[0] === "'")) value = value.slice(1, -1);
    values.set(line.slice(0, colon).trim().toLowerCase(), value);
  }
  if (errors.length) return { matter: null, errors };

  for (const key of REQUIRED) {
    if (!values.get(key.toLowerCase())) errors.push(error('frontmatter.field', `The header is missing '${key}'.`, 1));
  }
  if (errors.length) return { matter: null, errors };

  const docType = values.get('doctype')!;
  if (!DOC_TYPES.includes(docType)) errors.push(error('frontmatter.docType', `docType '${docType}' is not allowed. Use one of: ${DOC_TYPES.join(', ')}.`, 1));
  const version = values.get('version')!;
  if (version.toLowerCase() !== uploadVersion.toLowerCase()) {
    errors.push(error('version.mismatch', `The header version '${version}' does not match the upload version '${uploadVersion}'.`, 1));
  }
  return { matter: { title: values.get('title')!, body }, errors };
}

/** Cuts text into pieces of at most the chunk size, on paragraph boundaries (a stand-in for the Semantic Kernel TextChunker). */
function cut(text: string): string[] {
  const chunks: string[] = [];
  let current = '';
  for (const paragraph of text.split(/\n\s*\n/)) {
    const piece = paragraph.trim();
    if (!piece) continue;
    const joined = current ? `${current}\n\n${piece}` : piece;
    if (current && estimateTokens(joined) > MAX_TOKENS_PER_CHUNK) {
      chunks.push(current);
      current = piece;
    } else {
      current = joined;
    }
  }
  if (current) chunks.push(current);
  return chunks;
}

function report(fileName: string, version: string, title: string, errors: IssueReportDto[], warnings: IssueReportDto[], texts: string[]): DryRunReportDto {
  const valid = errors.length === 0;
  const chunks: ChunkReportDto[] = valid ? texts.map((text, ordinal) => ({ ordinal, text, estTokens: estimateTokens(text) })) : [];
  return { fileName, version, title, valid, errors, warnings, chunks };
}

function readMarkdown(fileName: string, version: string, text: string): DryRunReportDto {
  const { matter, errors } = readFrontMatter(text, version);
  if (!matter) return report(fileName, version, '', errors, [], []);

  const warnings: IssueReportDto[] = [];
  const texts = cut(matter.body);
  if (texts.length === 0) errors.push(error('document.empty', 'The document has no text after its header.', null));
  if (texts.length > 0 && !/^\s*#/.test(matter.body)) {
    warnings.push(error('document.no_heading', 'The body does not start with a heading, so its first chunk has no title line.', null));
  }
  return report(fileName, version, matter.title, errors, warnings, texts);
}

function readOpenApi(fileName: string, version: string, text: string): DryRunReportDto {
  let doc: { info?: { title?: string }; paths?: Record<string, Record<string, unknown>> };
  try {
    doc = JSON.parse(text) as typeof doc;
  } catch (e) {
    return report(fileName, version, '', [error('openapi.invalid_json', `The file is not valid JSON: ${(e as Error).message}`, null)], [], []);
  }
  const paths = doc && typeof doc === 'object' ? doc.paths : undefined;
  if (!paths || typeof paths !== 'object') {
    return report(fileName, version, doc?.info?.title ?? '', [error('openapi.no_paths', "The file has no 'paths' object. Is it an OpenAPI document?", null)], [], []);
  }

  const errors: IssueReportDto[] = [];
  const prefix = `/api/${version.toUpperCase()}/`;
  const texts: string[] = [];
  for (const [route, operations] of Object.entries(paths)) {
    if (!route.toLowerCase().startsWith(prefix.toLowerCase())) {
      errors.push(error('openapi.path_version', `The path '${route}' does not start with '${prefix}' (the upload version is ${version}).`, null));
      continue;
    }
    for (const [method, operation] of Object.entries(operations ?? {})) {
      texts.push(`${method.toUpperCase()} ${route}\n${JSON.stringify(operation, null, 2)}`);
    }
  }
  if (errors.length === 0 && texts.length === 0) errors.push(error('document.empty', 'The file has no operations.', null));
  return report(fileName, version, doc.info?.title ?? fileName, errors, [], texts);
}

/** What the API's DocumentProcessor makes of a file: the report, with its chunks when the file is valid. */
export function processFile(fileName: string, version: string, text: string): DryRunReportDto {
  return fileName.toLowerCase().endsWith('.md') ? readMarkdown(fileName, version, text) : readOpenApi(fileName, version, text);
}
