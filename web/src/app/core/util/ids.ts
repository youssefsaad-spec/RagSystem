let counter = 0;

/** crypto.randomUUID only exists in secure contexts (https or localhost); this works everywhere. */
export function localId(prefix = 'id'): string {
  counter += 1;
  try {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  } catch {
    /* fall back */
  }
  return `${prefix}-${Date.now().toString(36)}-${counter}-${Math.random().toString(36).slice(2, 8)}`;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isGuid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value);
}
