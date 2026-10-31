/**
 * Random idempotency key. crypto.randomUUID is unavailable outside secure contexts (e.g. plain HTTP on
 * a LAN address), so fall back to getRandomValues, which is available everywhere.
 */
export function newIdempotencyKey(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}
