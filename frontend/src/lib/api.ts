/**
 * Thin fetch wrapper for the same-origin API.
 * - Cookies carry the session (HttpOnly); nothing auth-related is stored in JS-accessible storage.
 * - Unsafe requests echo the XSRF-TOKEN cookie in the X-XSRF-TOKEN header (double-submit CSRF).
 * - Errors are normalised from RFC 9457 problem details into ApiError.
 */

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
  correlationId?: string;
  [key: string]: unknown;
}

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly fieldErrors: Record<string, string[]>;
  readonly correlationId: string | undefined;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.code = problem.code;
    this.fieldErrors = problem.errors ?? {};
    this.correlationId = problem.correlationId;
  }

  get isUnauthorized() {
    return this.status === 401;
  }
}

type UnauthorizedHandler = () => void;
let onUnauthorized: UnauthorizedHandler | undefined;

/** Registered by the auth provider so an expired session anywhere sends the user to sign in. */
export function setUnauthorizedHandler(handler: UnauthorizedHandler | undefined) {
  onUnauthorized = handler;
}

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS']);

function readCookie(name: string): string | undefined {
  return document.cookie
    .split('; ')
    .find((row) => row.startsWith(`${name}=`))
    ?.slice(name.length + 1);
}

async function ensureCsrfToken(): Promise<string | undefined> {
  let token = readCookie('XSRF-TOKEN');
  if (!token) {
    await fetch('/api/v1/auth/csrf', { credentials: 'same-origin' });
    token = readCookie('XSRF-TOKEN');
  }
  return token ? decodeURIComponent(token) : undefined;
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  headers?: Record<string, string>;
  signal?: AbortSignal;
  /** Auth endpoints handle 401 themselves and must not trigger the global session-expired flow. */
  skipUnauthorizedHandler?: boolean;
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = options.method ?? 'GET';
  const headers: Record<string, string> = { Accept: 'application/json', ...options.headers };

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  if (!SAFE_METHODS.has(method)) {
    const token = await ensureCsrfToken();
    if (token) {
      headers['X-XSRF-TOKEN'] = token;
    }
  }

  let response: Response;
  try {
    response = await fetch(`/api/v1${path}`, {
      method,
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      credentials: 'same-origin',
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }
    throw new ApiError(0, {
      title: 'Network error',
      detail: 'The server could not be reached. Check your connection.',
    });
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const isJson = response.headers.get('content-type')?.includes('json') ?? false;
  const payload: unknown = isJson ? await response.json() : undefined;

  if (!response.ok) {
    if (response.status === 401 && !options.skipUnauthorizedHandler) {
      onUnauthorized?.();
    }
    throw new ApiError(response.status, (payload as ProblemDetails | undefined) ?? { title: response.statusText });
  }

  return payload as T;
}

export function toQueryString(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  }
  const query = search.toString();
  return query ? `?${query}` : '';
}

/** User-facing message for any error thrown by a query or mutation. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403) return 'You do not have permission to perform this action.';
    if (error.status === 429) return 'Too many requests. Please wait a moment and try again.';
    if (error.status >= 500) {
      return `Something went wrong on the server.${error.correlationId ? ` Reference: ${error.correlationId}` : ''}`;
    }
    return error.message;
  }
  return 'An unexpected error occurred.';
}
