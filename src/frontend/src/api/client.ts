import type {
  ChatResponse,
  Conversation,
  ConversationSummary,
  Expense,
  TripSummary,
} from "../types";

/**
 * Empty bases keep requests and receipt images same-origin through Vite's
 * separate agent and records proxies, including from a phone on the LAN.
 * Explicit overrides allow calling either service directly.
 */
const AGENT_API_BASE = import.meta.env.VITE_AGENT_API_URL?.replace(/\/+$/, "") ?? "";
const RECORDS_API_BASE = import.meta.env.VITE_RECORDS_API_URL?.replace(/\/+$/, "") ?? "";

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly retryAfterSeconds: number | null = null,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(base: string, path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${base}${path}`, init);

  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const body = await response.json();
      if (body?.detail) detail = typeof body.detail === "string" ? body.detail : JSON.stringify(body.detail);
    } catch {
      /* keep the status line */
    }
    const retryAfter = Number(response.headers.get("Retry-After"));
    throw new ApiError(detail, response.status, Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

function query(params: Record<string, string | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) search.set(key, value);
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

export const api = {
  health: () =>
    request<{ status: string; agentReady: boolean; error: string | null }>(AGENT_API_BASE, "/health"),

  chat: async (input: {
    userId: string;
    message: string;
    conversationId?: string | null;
    images?: File[];
  }): Promise<ChatResponse> => {
    const form = new FormData();
    form.append("userId", input.userId);
    form.append("message", input.message);
    if (input.conversationId) form.append("conversationId", input.conversationId);
    for (const image of input.images ?? []) {
      form.append("images", image, image.name || "receipt.jpg");
    }

    return request<ChatResponse>(AGENT_API_BASE, "/chat", { method: "POST", body: form });
  },

  listTrips: (userId: string, status?: string) =>
    request<TripSummary[]>(RECORDS_API_BASE, `/api/trips${query({ userId, status })}`),

  getTrip: (userId: string, tripId: string) =>
    request<TripSummary>(RECORDS_API_BASE, `/api/trips/${encodeURIComponent(tripId)}${query({ userId })}`),

  listExpenses: (userId: string, tripId?: string) =>
    request<Expense[]>(RECORDS_API_BASE, `/api/expenses${query({ userId, tripId })}`),

  getExpense: (userId: string, expenseId: string) =>
    request<Expense>(RECORDS_API_BASE, `/api/expenses/${encodeURIComponent(expenseId)}${query({ userId })}`),

  expensePhotoUrl: (userId: string, expenseId: string) =>
    `${RECORDS_API_BASE}/api/expenses/${encodeURIComponent(expenseId)}/photo${query({ userId })}`,

  listConversations: (userId: string) =>
    request<ConversationSummary[]>(RECORDS_API_BASE, `/api/conversations${query({ userId })}`),

  getConversation: (userId: string, conversationId: string) =>
    request<Conversation>(
      RECORDS_API_BASE,
      `/api/conversations/${encodeURIComponent(conversationId)}${query({ userId })}`,
    ),
};
