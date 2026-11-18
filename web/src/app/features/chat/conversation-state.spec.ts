import { computed } from '@angular/core';
import { Subject, throwError } from 'rxjs';
import { ApiError } from '../../core/api/api-error';
import { ChatApi } from '../../core/api/chat-api.service';
import { ChatResponse, HistoryMessage, MAX_QUESTION_CHARS } from '../../core/api/models';
import { ConversationIndex } from './conversation-index.service';
import { ChatEnv, ConversationState } from './conversation-state';

const ID = '019a8c0e-7b8d-7c3a-9b1e-0123456789ab';

function answer(overrides: Partial<ChatResponse> = {}): ChatResponse {
  return {
    conversationId: ID,
    messageId: 'm-answer',
    answer: 'Use the offerId from SeatMap.',
    citations: [{ title: 'Workflow', part: 1, text: 'Workflow, part 1' }],
    usage: { inputTokens: 10, outputTokens: 5, thinkingTokens: 0, cacheHit: false },
    ...overrides,
  };
}


function apiError(init: Partial<ConstructorParameters<typeof ApiError>[0]> = {}): ApiError {
  return new ApiError({ kind: 'unavailable', status: 503, title: 't', message: 'm', retryable: true, ...init });
}

function setup(id: string | null = null) {
  const ask = new Subject<ChatResponse>();
  const messages = new Subject<HistoryMessage[]>();
  const api = { ask: vi.fn(() => ask), messages: vi.fn(() => messages) };
  const index = { upsert: vi.fn(), remove: vi.fn() };
  const env: ChatEnv = {
    api: api as unknown as ChatApi,
    index: index as unknown as ConversationIndex,
    toast: { show: vi.fn() } as unknown as ChatEnv['toast'],
    idAssigned: vi.fn(),
    answered: vi.fn(),
  };
  return { state: new ConversationState('v3', id, env), api, ask, messages, index, env };
}

describe('ConversationState creation', () => {
  it('can be created inside a computed() (as the chat page does) without writing signals', () => {
    const value = computed(() => setup(ID).state.id());
    expect(value()).toBe(ID);
    expect(computed(() => setup(null).state.loadState())()).toBe('ready');
  });
});

describe('ConversationState sending', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('sends a first question, adopts the id and records the exchange', () => {
    const { state, api, ask, index, env } = setup();
    expect(state.send('  Which offerId?  ')).toBe(true);
    expect(api.ask).toHaveBeenCalledWith('v3', null, 'Which offerId?');
    expect(state.sending()).toBe(true);
    expect(state.messages()[0].status).toBe('pending');

    ask.next(answer());
    expect(state.sending()).toBe(false);
    expect(state.id()).toBe(ID);
    expect(state.messages().map((m) => [m.role, m.status])).toEqual([
      ['user', 'sent'],
      ['assistant', 'sent'],
    ]);
    expect(state.messages()[1].citations).toHaveLength(1);
    expect(index.upsert).toHaveBeenCalledWith(expect.objectContaining({ id: ID, version: 'v3', title: 'Which offerId?' }));
    expect(env.idAssigned).toHaveBeenCalledTimes(1);
    expect(env.answered).toHaveBeenCalledTimes(1);
  });

  it('refuses empty, over-long and double sends', () => {
    const { state, api } = setup();
    expect(state.send('   ')).toBe(false);
    expect(state.send('x'.repeat(MAX_QUESTION_CHARS + 1))).toBe(false);
    expect(state.send('ok')).toBe(true);
    expect(state.send('again')).toBe(false); // one at a time
    expect(api.ask).toHaveBeenCalledTimes(1);
  });

  it('clears the draft on send', () => {
    const { state } = setup();
    state.setDraft('hello');
    state.send(state.draft());
    expect(state.draft()).toBe('');
  });

  it('marks the message failed, keeps its text, and can send it again', () => {
    const { state, api, ask } = setup();
    state.send('question');
    ask.error(apiError({ kind: 'upstream', status: 502, retryAfterSec: undefined }));
    const failed = state.messages()[0];
    expect(failed.status).toBe('failed');
    expect(failed.content).toBe('question');
    expect(state.cooldownLeft()).toBe(0);

    const retry = new Subject<ChatResponse>();
    api.ask.mockReturnValueOnce(retry);
    state.retry(failed.localId);
    expect(state.messages()[0].status).toBe('pending');
    retry.next(answer());
    expect(state.messages().map((m) => m.status)).toEqual(['sent', 'sent']);
    expect(state.messages()).toHaveLength(2); // retried in place, not duplicated
  });

  it('holds the composer for Retry-After and releases it on time', () => {
    const { state, ask } = setup();
    state.send('q1');
    ask.error(apiError({ retryAfterSec: 12 }));
    expect(state.cooldownLeft()).toBe(12);
    expect(state.send('q2')).toBe(false);

    vi.advanceTimersByTime(5_000);
    expect(state.cooldownLeft()).toBe(7);
    vi.advanceTimersByTime(8_000);
    expect(state.cooldownLeft()).toBe(0);
    expect(state.send('q2')).toBe(true);
  });

  it('does not start a cooldown for an error that waiting will not fix', () => {
    const { state, ask } = setup();
    state.send('q');
    ask.error(apiError({ retryable: false, retryAfterSec: undefined, reason: 'llm_error' }));
    expect(state.cooldownLeft()).toBe(0);
  });

  it('stops waiting on cancel and says the server may still have answered', () => {
    const { state, ask } = setup();
    state.send('q');
    state.cancel();
    expect(state.sending()).toBe(false);
    expect(state.messages()[0]).toMatchObject({ status: 'failed' });
    expect(state.messages()[0].error?.kind).toBe('cancelled');
    expect(ask.observed).toBe(false);
  });

  it('moves a failed message back into the composer on edit, ahead of newer text', () => {
    const { state, ask } = setup();
    state.send('first');
    ask.error(apiError({ retryable: false, retryAfterSec: undefined }));
    state.setDraft('newer');
    state.edit(state.messages()[0].localId);
    expect(state.messages()).toHaveLength(0);
    expect(state.draft()).toBe('first\n\nnewer');
  });

  it('keeps the same conversation id across questions', () => {
    const { state, api, ask } = setup();
    state.send('q1');
    ask.next(answer());
    const second = new Subject<ChatResponse>();
    api.ask.mockReturnValueOnce(second);
    state.send('q2');
    expect(api.ask).toHaveBeenLastCalledWith('v3', ID, 'q2');
  });

  it('does not announce an id again for an existing conversation', () => {
    const { state, ask, env } = setup(ID);
    state.send('q');
    ask.next(answer());
    expect(env.idAssigned).not.toHaveBeenCalled();
  });

  it('survives a throwing api call', () => {
    const { state, api } = setup();
    api.ask.mockReturnValueOnce(throwError(() => apiError({ kind: 'network', status: 0, retryAfterSec: undefined })) as never);
    state.send('q');
    expect(state.sending()).toBe(false);
    expect(state.messages()[0].status).toBe('failed');
  });
});

describe('ConversationState loading', () => {
  const history = (id: string, role: 'user' | 'assistant', t: number): HistoryMessage => ({
    id,
    role,
    content: id,
    createdAt: new Date(t).toISOString(),
    citations: [],
    usage: null,
  });

  it('loads history of an existing conversation once while fresh', () => {
    const { state, api, messages } = setup(ID);
    state.load();
    expect(state.loadState()).toBe('loading');
    messages.next([history('a', 'user', 1000), history('b', 'assistant', 2000)]);
    expect(state.loadState()).toBe('ready');
    expect(state.messages().map((m) => m.localId)).toEqual(['a', 'b']);
    state.load();
    expect(api.messages).toHaveBeenCalledTimes(1); // recent result reused
  });

  it('reports a missing conversation and forgets it locally', () => {
    const { state, messages, index } = setup(ID);
    state.load();
    messages.error(apiError({ kind: 'not_found', status: 404, retryable: false, retryAfterSec: undefined }));
    expect(state.loadState()).toBe('error');
    expect(state.loadError()?.kind).toBe('not_found');
    expect(index.remove).toHaveBeenCalledWith(ID);
    expect(state.send('q')).toBe(false); // nothing can be sent into a conversation that is gone
  });

  it('shows a network failure and lets the user try again', () => {
    const { state, api, messages } = setup(ID);
    state.load();
    messages.error(apiError({ kind: 'network', status: 0, retryAfterSec: undefined }));
    expect(state.loadState()).toBe('error');
    const again = new Subject<HistoryMessage[]>();
    api.messages.mockReturnValueOnce(again);
    state.load(true);
    again.next([history('a', 'user', 1)]);
    expect(state.loadState()).toBe('ready');
  });

  it('keeps unsent messages after a refresh', () => {
    const { state, messages, ask } = setup(ID);
    state.load();
    messages.next([history('a', 'user', 1), history('b', 'assistant', 2)]);
    state.send('local');
    ask.error(apiError({ retryable: false, retryAfterSec: undefined }));
    state.load(true);
    messages.next([history('a', 'user', 1), history('b', 'assistant', 2)]);
    expect(state.messages().map((m) => m.content)).toEqual(['a', 'b', 'local']);
  });

  it('discards a history result that arrives after a send finished', () => {
    const { state, messages, ask } = setup(ID);
    state.load();
    messages.next([history('a', 'user', 1)]);

    state.load(true); // a refresh starts...
    state.send('q2'); // ...then a question is sent and answered
    ask.next(answer());
    messages.next([history('stale', 'user', 1)]); // the refresh answers late, without the new exchange

    expect(state.messages().some((m) => m.localId === 'stale')).toBe(false);
    expect(state.messages().map((m) => m.role)).toEqual(['user', 'user', 'assistant']);
  });
});

describe('ConversationState drafts', () => {
  afterEach(() => sessionStorage.clear());

  it('keeps the typed text for the same conversation when it is opened again', () => {
    const { state } = setup(ID);
    state.setDraft('half a question');
    expect(setup(ID).state.draft()).toBe('half a question');
    expect(setup(null).state.draft()).toBe(''); // another conversation does not see it
  });

  it('keeps a failed question out of the stored draft until the user edits it back', () => {
    const { state, ask } = setup();
    state.send('failed one');
    ask.error(apiError({ retryable: false, retryAfterSec: undefined }));
    expect(state.draft()).toBe('');
    state.edit(state.messages()[0].localId);
    expect(state.draft()).toBe('failed one');
  });
});
