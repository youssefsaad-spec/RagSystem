import { HttpErrorResponse } from '@angular/common/http';

export type ApiErrorKind =
  | 'network' //          the request never reached the server (offline, DNS, server down)
  | 'timeout' //          the client gave up waiting
  | 'cancelled' //        the user stopped waiting
  | 'not_found' //        404
  | 'validation' //       400
  | 'conflict' //         409
  | 'payload_too_large' // 413
  | 'unsupported_media' // 415
  | 'unprocessable' //    422
  | 'unavailable' //      503: the model service is busy or down
  | 'upstream' //         502 / 504: the model call failed, or the gateway could not reach the API
  | 'server' //           other 5xx
  | 'bad_response' //     2xx with a body this app cannot use
  | 'unknown';

/** Fields of an application/problem+json body that this app reads. The API adds a non-standard `reason`. */
export interface Problem {
  title?: string;
  detail?: string;
  reason?: string;
  traceId?: string;
  errors?: string[];
}

export interface ApiErrorInit {
  kind: ApiErrorKind;
  status: number;
  title: string;
  message: string;
  reason?: string;
  retryable: boolean;
  /** Seconds, from the Retry-After header (clamped). */
  retryAfterSec?: number;
  traceId?: string;
  /** The parsed body, kept for 422 upload reports. */
  body?: unknown;
}

/** What the UI shows and how it reacts. Always built by {@link ApiError.fromHttp} or the named factories. */
export class ApiError extends Error implements ApiErrorInit {
  readonly kind: ApiErrorKind;
  readonly status: number;
  readonly title: string;
  readonly reason?: string;
  readonly retryable: boolean;
  readonly retryAfterSec?: number;
  readonly traceId?: string;
  readonly body?: unknown;

  constructor(init: ApiErrorInit) {
    super(init.message);
    this.name = 'ApiError';
    this.kind = init.kind;
    this.status = init.status;
    this.title = init.title;
    this.reason = init.reason;
    this.retryable = init.retryable;
    this.retryAfterSec = init.retryAfterSec;
    this.traceId = init.traceId;
    this.body = init.body;
  }

  static timeout(seconds: number): ApiError {
    return new ApiError({
      kind: 'timeout',
      status: 0,
      title: 'No answer yet',
      message: `The server did not respond within ${seconds} seconds. It may still be working on it, so check the conversation before sending the question again.`,
      reason: 'client_timeout',
      retryable: true,
    });
  }

  static cancelled(): ApiError {
    return new ApiError({
      kind: 'cancelled',
      status: 0,
      title: 'Stopped waiting',
      message:
        'You stopped waiting for this answer. The server may still have processed the question, so refresh the conversation before asking again.',
      reason: 'client_cancelled',
      retryable: true,
    });
  }

  static conversationNotFound(): ApiError {
    return new ApiError({
      kind: 'not_found',
      status: 404,
      title: 'Conversation not found',
      message: 'This conversation no longer exists, or it belongs to a different API version. Start a new conversation to continue.',
      reason: 'conversation_not_found',
      retryable: false,
    });
  }

  static badResponse(what: string): ApiError {
    return new ApiError({
      kind: 'bad_response',
      status: 200,
      title: 'Unexpected response',
      message: `The server answered, but not in the format this app expects (${what}). Check that the app is connected to the RagSystem API.`,
      reason: 'bad_response',
      retryable: true,
    });
  }

  static unknown(cause: unknown): ApiError {
    const text = cause instanceof Error ? cause.message : String(cause);
    return new ApiError({
      kind: 'unknown',
      status: 0,
      title: 'Something went wrong',
      message: text ? `Unexpected error: ${text}` : 'An unexpected error occurred.',
      retryable: true,
    });
  }

  /** Turns anything an HttpClient call can fail with into an ApiError. */
  static fromHttp(err: HttpErrorResponse): ApiError {
    const retryAfterSec = parseRetryAfter(err.headers?.get('Retry-After') ?? null);
    const problem = readProblem(err.error);
    const status = err.status;

    // A 2xx whose body failed to parse (for example the dev server returning index.html for /api/...).
    if (status >= 200 && status < 300) return ApiError.badResponse('the body is not valid JSON');

    // Status 0: the browser never got a response.
    if (status === 0) {
      return new ApiError({
        kind: 'network',
        status: 0,
        title: 'Cannot reach the server',
        message: 'The request did not get through. Check your connection and that the API is running, then try again.',
        reason: 'network',
        retryable: true,
      });
    }

    // A known `reason` from the API wins; otherwise the status code decides (this covers proxy HTML pages).
    const mapped = (problem.reason && REASONS[problem.reason]) || undefined;
    const base = mapped ?? byStatus(status, problem);
    const message = base.message(problem, retryAfterSec);

    return new ApiError({
      kind: base.kind,
      status,
      title: base.title,
      message,
      reason: problem.reason ?? base.reason,
      retryable: base.retryable,
      retryAfterSec: base.retryable ? retryAfterSec : undefined,
      traceId: problem.traceId,
      body: err.error,
    });
  }
}

export function isApiError(value: unknown): value is ApiError {
  return value instanceof ApiError;
}

interface Rule {
  kind: ApiErrorKind;
  title: string;
  reason?: string;
  retryable: boolean;
  message: (p: Problem, retryAfterSec?: number) => string;
}

const serverDetail = (p: Problem, fallback: string) => (p.detail?.trim() ? p.detail.trim() : fallback);

/** Messages for the `reason` values the API sends. */
const REASONS: Record<string, Rule> = {
  empty_question: {
    kind: 'validation',
    title: 'Empty message',
    retryable: false,
    message: () => 'Write a question before sending.',
  },
  question_too_long: {
    kind: 'validation',
    title: 'Question too long',
    retryable: false,
    message: () => 'The question is over the limit of about 1,000 tokens (roughly 3,500 characters). Shorten it or split it into parts.',
  },
  unknown_version: {
    kind: 'validation',
    title: 'Unknown API version',
    retryable: false,
    message: (p) => serverDetail(p, 'Use V2 or V3.'),
  },
  conversation_not_found: {
    kind: 'not_found',
    title: 'Conversation not found',
    retryable: false,
    message: () => 'This conversation no longer exists, or it belongs to a different API version. Start a new conversation to continue.',
  },
  document_not_found: {
    kind: 'not_found',
    title: 'Document not found',
    retryable: false,
    message: (p) => serverDetail(p, 'It does not exist in this version or was already removed.'),
  },
  document_processing: {
    kind: 'conflict',
    title: 'Already processing',
    retryable: false,
    message: (p) => serverDetail(p, 'This document is still being processed. Wait until it is Ready or Failed, then upload it again.'),
  },
  not_multipart: {
    kind: 'validation',
    title: 'Not a form upload',
    retryable: false,
    message: () => 'The file must be sent as a form upload.',
  },
  no_file: {
    kind: 'validation',
    title: 'No file',
    retryable: false,
    message: () => 'The upload has no file, or the file is empty.',
  },
  llm_unavailable: {
    kind: 'unavailable',
    title: 'Model unavailable',
    retryable: true,
    message: (_p, s) => `The model service is busy or temporarily down. ${waitText(s)}`,
  },
  llm_error: {
    kind: 'upstream',
    title: 'The model request failed',
    retryable: true,
    message: (p) =>
      `${serverDetail(p, 'The model could not complete this request.')} Trying again may work; if it keeps failing, contact the administrator.`,
  },
  empty_answer: {
    kind: 'upstream',
    title: 'Empty answer',
    retryable: true,
    message: () => 'The model returned an empty answer. Try again, or rephrase the question.',
  },
};

function waitText(seconds?: number): string {
  if (!seconds) return 'Try again in a moment.';
  return `You can try again in ${seconds >= 60 ? `${Math.ceil(seconds / 60)} min` : `${seconds} s`}.`;
}

function byStatus(status: number, p: Problem): Rule {
  const detail = (fallback: string) => (p.detail?.trim() ? p.detail.trim() : fallback);
  switch (status) {
    case 400:
      return {
        kind: 'validation',
        title: p.title?.trim() || 'Invalid request',
        retryable: false,
        message: () => (p.errors?.length ? p.errors.join(' ') : detail('The server rejected the request as invalid.')),
      };
    case 404:
      return {
        kind: 'not_found',
        title: p.title?.trim() || 'Not found',
        retryable: false,
        message: () => detail('It does not exist or was already removed.'),
      };
    case 408:
      return {
        kind: 'timeout',
        title: 'Request timed out',
        retryable: true,
        message: () => 'The server timed out waiting for the request. Try again.',
      };
    case 409:
      return {
        kind: 'conflict',
        title: p.title?.trim() || 'Conflict',
        retryable: false,
        message: () => detail('The request conflicts with the current state. Refresh and try again.'),
      };
    case 413:
      return {
        kind: 'payload_too_large',
        title: 'File too large',
        reason: 'payload_too_large',
        retryable: false,
        message: () => detail('The file is larger than the server accepts.'),
      };
    case 415:
      return {
        kind: 'unsupported_media',
        title: 'Unsupported file type',
        reason: 'unsupported_media',
        retryable: false,
        message: () => detail('Upload a Markdown (.md) document or an OpenAPI (.json) file.'),
      };
    case 422:
      return {
        kind: 'unprocessable',
        title: 'Document rejected',
        reason: 'unprocessable',
        retryable: false,
        message: () => detail('The document has errors and was not stored.'),
      };
    case 502:
    case 504:
      return {
        kind: 'upstream',
        title: status === 504 ? 'The server took too long' : 'Bad gateway',
        reason: 'upstream_unreachable',
        retryable: true,
        message: () =>
          'The API could not be reached or did not answer in time. If it was just restarted, wait a few seconds and try again.',
      };
    case 503:
      return {
        kind: 'unavailable',
        title: 'Temporarily unavailable',
        reason: 'unavailable',
        retryable: true,
        message: (_p, s) => `${detail('The service is temporarily unavailable.')} ${waitText(s)}`.trim(),
      };
    default:
      return {
        kind: 'server',
        title: status >= 500 ? 'Server error' : `Request failed (${status})`,
        reason: 'server_error',
        retryable: status >= 500,
        message: () =>
          status >= 500
            ? 'The server hit an unexpected error. Try again; if it keeps happening, send the reference below (if any) to the administrator.'
            : detail('The request could not be completed.'),
      };
  }
}

/** Reads title/detail/reason from a problem+json object, a JSON string, or ignores HTML and empty bodies. */
export function readProblem(body: unknown): Problem {
  let value: unknown = body;
  if (typeof value === 'string') {
    const text = value.trim();
    if (!text.startsWith('{')) return {};
    try {
      value = JSON.parse(text);
    } catch {
      return {};
    }
  }
  if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
  const o = value as Record<string, unknown>;
  const str = (v: unknown) => (typeof v === 'string' && v.trim() ? v.trim() : undefined);

  let errors: string[] | undefined;
  if (o['errors'] && typeof o['errors'] === 'object') {
    errors = Object.values(o['errors'] as Record<string, unknown>)
      .flatMap((v) => (Array.isArray(v) ? v : [v]))
      .filter((v): v is string => typeof v === 'string');
  }
  return {
    title: str(o['title']),
    detail: str(o['detail']),
    reason: str(o['reason']),
    // The API will send `correlationId` once F03/F37 exist; ASP.NET's own problem bodies carry `traceId`.
    traceId: str(o['correlationId']) ?? str(o['traceId']),
    errors,
  };
}

/** Retry-After is either delta-seconds or an HTTP date. The result is clamped to 1 s .. 24 h. */
export function parseRetryAfter(value: string | null, now = Date.now()): number | undefined {
  if (!value) return undefined;
  const text = value.trim();
  const clamp = (n: number) => Math.min(86_400, Math.max(1, Math.ceil(n)));
  if (/^\d+$/.test(text)) return clamp(Number(text));
  const date = Date.parse(text);
  return Number.isNaN(date) ? undefined : clamp((date - now) / 1000);
}

/** One line for toasts and logs. */
export function describe(error: ApiError): string {
  return `${error.title}. ${error.message}`;
}
