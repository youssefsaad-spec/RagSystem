import { HttpContextToken } from '@angular/common/http';

/** Per request: how long to wait for the next response event before giving up (default 30 s). */
export const REQUEST_TIMEOUT_MS = new HttpContextToken<number>(() => 30_000);
