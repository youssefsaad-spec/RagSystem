import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ChatApi } from '../../core/api/chat-api.service';
import { ChatResponse } from '../../core/api/models';
import { ToastService } from '../../core/ui/toast.service';
import { ChatStore } from './chat-store.service';
import { ConversationIndex } from './conversation-index.service';

const ID = '019a8c0e-7b8d-7c3a-9b1e-0123456789ab';

describe('ChatStore', () => {
  const ask = new Subject<ChatResponse>();

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: ChatApi, useValue: { ask: () => ask, messages: () => new Subject() } },
        { provide: ConversationIndex, useValue: { upsert: vi.fn(), remove: vi.fn() } },
        { provide: ToastService, useValue: { show: vi.fn() } },
      ],
    });
  });

  // Regression: the page looks its state up inside a computed(). When the first answer gave the conversation an id,
  // that computed re-ran and opened a blank new conversation, so the answer vanished from the screen.
  it('does not make a page re-open a blank conversation when a new conversation gets its id', () => {
    const store = TestBed.inject(ChatStore);
    const page = computed(() => store.open('v3', null));

    const first = page();
    first.send('How do I authenticate?');
    ask.next({
      conversationId: ID,
      messageId: 'm-1',
      answer: 'Use a key.',
      citations: [],
      usage: { inputTokens: 1, outputTokens: 1, thinkingTokens: 0, cacheHit: false },
    });

    expect(first.id()).toBe(ID);
    expect(page()).toBe(first);
    expect(store.open('v3', ID)).toBe(first);
    expect(store.open('v3', null)).not.toBe(first);
  });
});
