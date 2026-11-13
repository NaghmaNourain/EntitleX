import type { ProblemDetails } from './types'

const CORRELATION_HEADER = 'X-Correlation-ID'

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, message: string, problem: ProblemDetails | null = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

export function createCorrelationId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID()
  }
  return `corr-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

type TokenGetter = () => string | null

let getAccessToken: TokenGetter = () => null

export function configureApiClient(tokenGetter: TokenGetter): void {
  getAccessToken = tokenGetter
}

export interface RequestOptions {
  method?: string
  body?: unknown
  headers?: Record<string, string>
  signal?: AbortSignal
  idempotencyKey?: string
  correlationId?: string
  auth?: boolean
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...options.headers,
  }

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }

  if (options.auth !== false) {
    const token = getAccessToken()
    if (token) {
      headers.Authorization = `Bearer ${token}`
    }
  }

  const correlationId = options.correlationId ?? createCorrelationId()
  headers[CORRELATION_HEADER] = correlationId

  if (options.idempotencyKey) {
    headers['Idempotency-Key'] = options.idempotencyKey
  }

  const response = await fetch(path, {
    method: options.method ?? (options.body !== undefined ? 'POST' : 'GET'),
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    signal: options.signal,
  })

  if (response.status === 204) {
    return undefined as T
  }

  const contentType = response.headers.get('content-type') ?? ''
  const isJson = contentType.includes('application/json') || contentType.includes('+json')
  const payload = isJson ? await response.json().catch(() => null) : await response.text()

  if (!response.ok) {
    const problem = (isJson ? payload : null) as ProblemDetails | null
    const message =
      problem?.detail ||
      problem?.title ||
      (typeof payload === 'string' && payload) ||
      `Request failed with status ${response.status}`
    throw new ApiError(response.status, message, problem)
  }

  return payload as T
}

export function formatValidationErrors(problem: ProblemDetails | null): string[] {
  if (!problem?.errors) {
    // Detail is already shown as ApiError.message — avoid duplicating it in the list.
    return []
  }
  return Object.entries(problem.errors).flatMap(([field, messages]) =>
    messages.map((m) => `${field}: ${m}`),
  )
}
