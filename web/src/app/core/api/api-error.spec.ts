import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ApiError, parseRetryAfter, readProblem } from './api-error';

function http(status: number, body: unknown = null, headers: Record<string, string> = {}): ApiError {
  return ApiError.fromHttp(new HttpErrorResponse({ status, error: body, headers: new HttpHeaders(headers), url: '/api/x' }));
}

describe('ApiError.fromHttp', () => {
  it('treats status 0 as a network failure that can be retried', () => {
    const e = http(0);
    expect(e.kind).toBe('network');
    expect(e.retryable).toBe(true);
  });

  it('uses the reason of a problem+json body over the status', () => {
    const e = http(400, { title: 'Bad request', detail: 'x', reason: 'empty_question', status: 400 });
    expect(e.kind).toBe('validation');
    expect(e.reason).toBe('empty_question');
    expect(e.retryable).toBe(false);
    expect(e.message).toMatch(/question/i);
  });

  it.each(['llm_unavailable'])('maps %s to unavailable and retryable', (reason) => {
    const e = http(503, { reason }, { 'Retry-After': '60' });
    expect(e.kind).toBe('unavailable');
    expect(e.retryable).toBe(true);
    expect(e.retryAfterSec).toBe(60);
  });

  it('maps llm_error (502) to upstream', () => {
    const e = http(502, { reason: 'llm_error' });
    expect(e.kind).toBe('upstream');
    expect(e.retryable).toBe(true);
  });

  it('copes with an HTML error page from a proxy', () => {
    const e = http(502, '<html><body>Bad Gateway</body></html>');
    expect(e.kind).toBe('upstream');
    expect(e.message).not.toContain('<html');
  });

  it('turns a 200 whose body is not JSON into bad_response', () => {
    const e = ApiError.fromHttp(new HttpErrorResponse({ status: 200, error: { error: new Error('x'), text: '<html>' }, url: '/api/x' }));
    expect(e.kind).toBe('bad_response');
  });

  it('reads the reference id of a 500', () => {
    const e = http(500, { title: 'An error occurred', traceId: '00-abc-01', status: 500 });
    expect(e.kind).toBe('server');
    expect(e.traceId).toBe('00-abc-01');
  });

  it('joins validation errors of a 400', () => {
    const e = http(400, { title: 'One or more validation errors occurred.', errors: { name: ['Name is required.'], x: ['Too big.'] } });
    expect(e.message).toBe('Name is required. Too big.');
  });

  it('keeps the 422 body so the report can be shown', () => {
    const body = { fileName: 'a.md', valid: false, errors: [{ code: 'E1', message: 'bad', line: 3 }] };
    const e = http(422, body);
    expect(e.kind).toBe('unprocessable');
    expect(e.body).toBe(body);
  });

  it.each([
    [409, 'conflict'],
    [413, 'payload_too_large'],
    [415, 'unsupported_media'],
    [404, 'not_found'],
    [504, 'upstream'],
    [503, 'unavailable'],
  ])('maps status %i to %s', (status, kind) => {
    expect(http(status).kind).toBe(kind);
  });
});

describe('ApiError.fromHttp with the problem bodies of the RagSystem API', () => {
  it('turns llm_unavailable (503 with Retry-After) into a retryable wait', () => {
    const e = http(503, { status: 503, title: 'Model service unavailable', reason: 'llm_unavailable' }, { 'Retry-After': '30' });
    expect(e.kind).toBe('unavailable');
    expect(e.retryAfterSec).toBe(30);
    expect(e.message).toMatch(/30 s/);
  });

  it('keeps the server wording for an llm_error', () => {
    const e = http(502, { reason: 'llm_error', detail: 'The model service refused the request: API key not valid.' });
    expect(e.kind).toBe('upstream');
    expect(e.message).toContain('API key not valid');
  });

  it('maps empty_answer and conversation_not_found', () => {
    expect(http(502, { reason: 'empty_answer' }).message).toMatch(/empty answer/i);
    const gone = http(404, { reason: 'conversation_not_found' });
    expect(gone.kind).toBe('not_found');
    expect(gone.reason).toBe('conversation_not_found');
  });

  it('maps the upload problems by their reason', () => {
    expect(http(409, { reason: 'document_processing', detail: 'Document 1 is still being processed.' }).kind).toBe('conflict');
    expect(http(400, { reason: 'no_file' }).kind).toBe('validation');
    expect(http(404, { reason: 'document_not_found' }).kind).toBe('not_found');
    expect(http(413, { reason: 'file_too_large', detail: 'The limit is 10485760 bytes.' }).message).toContain('10485760');
    expect(http(415, { reason: 'unsupported_file_type' }).kind).toBe('unsupported_media');
  });

  it('reads the correlation id once the API sends one', () => {
    expect(http(500, { correlationId: 'abc-123' }).traceId).toBe('abc-123');
  });
});

describe('parseRetryAfter', () => {
  it('reads seconds', () => expect(parseRetryAfter('30')).toBe(30));
  it('clamps to at least one second', () => expect(parseRetryAfter('0')).toBe(1));
  it('clamps to a day', () => expect(parseRetryAfter('9999999')).toBe(86_400));
  it('reads an HTTP date', () => {
    const now = Date.parse('2026-01-01T00:00:00Z');
    expect(parseRetryAfter('Thu, 01 Jan 2026 00:01:00 GMT', now)).toBe(60);
  });
  it('ignores junk and empty values', () => {
    expect(parseRetryAfter('soon')).toBeUndefined();
    expect(parseRetryAfter(null)).toBeUndefined();
  });
});

describe('readProblem', () => {
  it('returns nothing for HTML, arrays and null', () => {
    expect(readProblem('<html>')).toEqual({});
    expect(readProblem([1])).toEqual({});
    expect(readProblem(null)).toEqual({});
  });
});
