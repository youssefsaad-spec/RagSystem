import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { API_VERSIONS, ApiVersion, isApiVersion, versionLabel } from '../../core/api/models';
import { PreferencesService } from '../../core/prefs/preferences.service';
import { ConfirmService } from '../../core/ui/confirm.service';
import { OnlineService } from '../../core/ui/online.service';
import { formatNumber, formatRelative, formatTime } from '../../core/util/format';
import { isGuid } from '../../core/util/ids';
import { ErrorNoticeComponent } from '../../shared/error-notice/error-notice.component';
import { ChatStore } from './chat-store.service';
import { ComposerComponent } from './composer.component';
import { ConversationIndex, ConversationMeta } from './conversation-index.service';
import { MessageComponent } from './message.component';

const STARTERS: Record<ApiVersion, string[]> = {
  v2: [
    'What is the booking flow from search to payment?',
    'How do I authenticate with the API?',
    'What is the difference between Hold and BookAndPay?',
    'What does FareConfirmOffer do?',
  ],
  v3: [
    'What changed in V3 compared with V2?',
    'How do I select seats in V3?',
    'Which endpoints return a new offerId?',
    'What is the booking flow from search to payment?',
  ],
};

@Component({
  selector: 'app-chat-page',
  imports: [RouterLink, MessageComponent, ComposerComponent, ErrorNoticeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './chat.page.html',
  styleUrl: './chat.page.css',
})
export class ChatPage {
  /** Route parameters (bound by withComponentInputBinding). */
  readonly version = input<string>();
  readonly id = input<string>();

  private readonly store = inject(ChatStore);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly index = inject(ConversationIndex);
  private readonly prefs = inject(PreferencesService);
  protected readonly online = inject(OnlineService);

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly composer = viewChild(ComposerComponent);

  protected readonly versions = API_VERSIONS;
  protected readonly label = versionLabel;
  protected readonly format = formatNumber;
  protected readonly relative = formatRelative;
  protected readonly clock = formatTime;
  protected readonly railOpen = signal(false);
  protected readonly atBottom = signal(true);

  /** The version of the URL, case-insensitive (the API lower-cases it too). Null when it is not V2 or V3. */
  protected readonly ver = computed<ApiVersion | null>(() => {
    const v = this.version()?.toLowerCase();
    return isApiVersion(v) ? v : null;
  });

  /** What is wrong with the link, if anything. */
  protected readonly badLink = computed<'version' | 'id' | null>(() => {
    if (!this.ver()) return 'version';
    const id = this.id();
    return id !== undefined && !isGuid(id) ? 'id' : null;
  });

  protected readonly state = computed(() => {
    this.store.revision(); // a discarded "new conversation" must be looked up again
    const v = this.ver();
    if (!v || this.badLink()) return null;
    return this.store.open(v, this.id() ?? null);
  });

  protected readonly conversations = this.index.items;
  protected readonly starters = computed(() => STARTERS[this.ver() ?? 'v3']);

  protected readonly blockedReason = computed(() => {
    const s = this.state();
    if (!s) return null;
    if (s.loadState() === 'loading') return 'Loading the conversation.';
    if (s.loadState() === 'error') return 'This conversation cannot be continued.';
    return null;
  });

  constructor() {
    // Remember the version, tell the store what is on screen, and load history.
    effect((onCleanup) => {
      const s = this.state();
      const v = this.ver();
      untracked(() => {
        if (v) this.prefs.setLastVersion(v);
        this.store.setActive(s);
        s?.load();
        this.atBottom.set(true);
        this.railOpen.set(false);
      });
      queueMicrotask(() => this.composer()?.focus());
      onCleanup(() => this.store.setActive(null));
    });

    // A new conversation got its id from the first answer: put it in the address without a reload.
    effect(() => {
      const s = this.state();
      const v = this.ver();
      const newId = s?.id();
      if (s && v && newId && this.id() === undefined) {
        void this.router.navigate(['/chat', v, newId], { replaceUrl: true });
      }
    });

    // Keep the newest message in view, unless the reader scrolled up to read.
    effect(() => {
      const s = this.state();
      s?.messages(); // re-run whenever a message is added or changes
      s?.sending();
      if (this.atBottom()) untracked(() => requestAnimationFrame(() => this.scrollToEnd(false)));
    });
  }

  protected send(text: string): void {
    const s = this.state();
    if (!s) return;
    this.atBottom.set(true);
    s.send(text);
  }

  protected switchVersion(v: ApiVersion): void {
    const s = this.state();
    if (s && v !== this.ver()) {
      // The typed text comes along when the other version's new conversation is still empty.
      const carry = s.draft();
      const target = this.store.open(v, null);
      if (carry && !target.draft()) target.setDraft(carry);
    }
    void this.router.navigate(['/chat', v]);
  }

  protected newConversation(): void {
    const v = this.ver() ?? this.prefs.lastVersion();
    this.store.startNew(v);
    void this.router.navigate(['/chat', v]);
    this.railOpen.set(false);
  }

  protected askInNewConversation(localId: string): void {
    const s = this.state();
    const v = this.ver();
    if (!s || !v) return;
    const text = s.messages().find((m) => m.localId === localId)?.content;
    if (!text) return;
    s.discard(localId);
    this.store.startNew(v);
    const fresh = this.store.open(v, null);
    fresh.setDraft(text);
    void this.router.navigate(['/chat', v]).then(() => fresh.send(text));
  }

  protected useStarter(text: string): void {
    this.state()?.setDraft(text);
    this.composer()?.focus();
  }

  protected async forget(c: ConversationMeta): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Remove from this list?',
      message: `“${c.title}” is only forgotten in this browser. The conversation stays on the server and can be reopened with its link.`,
      confirmLabel: 'Remove from list',
      danger: true,
    });
    if (!ok) return;
    this.index.remove(c.id);
    if (this.id() === c.id) void this.router.navigate(['/chat', c.version]);
  }

  protected goLatest(): void {
    this.atBottom.set(true);
    this.scrollToEnd(true);
  }

  protected onScroll(): void {
    const el = this.scroller()?.nativeElement;
    if (!el) return;
    this.atBottom.set(el.scrollHeight - el.scrollTop - el.clientHeight < 80);
  }

  private scrollToEnd(smooth: boolean): void {
    const el = this.scroller()?.nativeElement;
    if (!el) return;
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    el.scrollTo({ top: el.scrollHeight, behavior: smooth && !reduce ? 'smooth' : 'auto' });
  }

  /** For an answer, the question right before it. */
  protected previousQuestion(messages: { role: string; content: string }[], index: number): string | null {
    const before = messages[index - 1];
    return messages[index]?.role === 'assistant' && before?.role === 'user' ? before.content : null;
  }

  protected trackMessage(_: number, m: { localId: string }): string {
    return m.localId;
  }
}
