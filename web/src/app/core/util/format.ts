const number = new Intl.NumberFormat('en');

export function formatNumber(n: number | null | undefined): string {
  return n === null || n === undefined || !Number.isFinite(n) ? 'n/a' : number.format(n);
}

/** 75 -> "1 min 15 s", 3700 -> "1 h 2 min". */
export function formatDuration(totalSeconds: number): string {
  const s = Math.max(0, Math.ceil(totalSeconds));
  if (s < 60) return `${s} s`;
  const m = Math.floor(s / 60);
  if (m < 60) return s % 60 === 0 ? `${m} min` : `${m} min ${s % 60} s`;
  const h = Math.floor(m / 60);
  return m % 60 === 0 ? `${h} h` : `${h} h ${m % 60} min`;
}

export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return 'n/a';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** Safe date parsing: an invalid or missing value gives null instead of "Invalid Date". */
export function toDate(value: string | number | Date | null | undefined): Date | null {
  if (value === null || value === undefined) return null;
  const d = value instanceof Date ? value : new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

export function formatDateTime(value: string | number | Date | null | undefined): string {
  const d = toDate(value);
  return d ? d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : 'n/a';
}

export function formatTime(value: string | number | Date | null | undefined): string {
  const d = toDate(value);
  return d ? d.toLocaleTimeString(undefined, { timeStyle: 'short' }) : '';
}

export function formatRelative(value: string | number | Date | null | undefined, now = Date.now()): string {
  const d = toDate(value);
  if (!d) return '';
  const sec = Math.round((now - d.getTime()) / 1000);
  if (sec < 45) return 'just now';
  if (sec < 3600) return `${Math.round(sec / 60)} min ago`;
  if (sec < 86400) return `${Math.round(sec / 3600)} h ago`;
  if (sec < 86400 * 7) return `${Math.round(sec / 86400)} d ago`;
  return d.toLocaleDateString(undefined, { dateStyle: 'medium' });
}

export function truncate(text: string, max: number): string {
  const clean = text.replace(/\s+/g, ' ').trim();
  return clean.length <= max ? clean : clean.slice(0, max - 1).trimEnd() + '…';
}
