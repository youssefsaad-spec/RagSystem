import { Routes, UrlSegment } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'chat' },
  {
    path: 'chat',
    children: [
      { path: '', pathMatch: 'full', loadComponent: () => import('./features/chat/chat-redirect').then((m) => m.ChatRedirect) },
      {
        // One route for /chat/:version and /chat/:version/:id, so the page is kept (not rebuilt) when a new
        // conversation receives its id and the address changes.
        matcher: (segments) => {
          if (segments.length !== 1 && segments.length !== 2) return null;
          const posParams: Record<string, UrlSegment> = { version: segments[0] };
          if (segments.length === 2) posParams['id'] = segments[1];
          return { consumed: segments, posParams };
        },
        title: 'Chat · NDC Docs Assistant',
        loadComponent: () => import('./features/chat/chat.page').then((m) => m.ChatPage),
      },
    ],
  },
  {
    // Open until API keys exist (F29); the guard for admins goes here then.
    path: 'admin',
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'documents' },
      {
        path: 'documents',
        title: 'Documents · NDC Docs Assistant',
        loadComponent: () => import('./features/admin/documents/documents.page').then((m) => m.DocumentsPage),
      },
    ],
  },
  {
    path: 'search',
    title: 'Search check · NDC Docs Assistant',
    loadComponent: () => import('./features/search/search.page').then((m) => m.SearchPage),
  },
  {
    path: 'tuning',
    title: 'Retrieval tuning · NDC Docs Assistant',
    loadComponent: () => import('./features/tuning/tuning.page').then((m) => m.TuningPage),
  },
  {
    path: '**',
    title: 'Not found · NDC Docs Assistant',
    loadComponent: () => import('./features/not-found/not-found.page').then((m) => m.NotFoundPage),
  },
];
