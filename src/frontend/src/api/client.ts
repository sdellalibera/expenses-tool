import type {
  ChatResponse,
  Conversation,
  ConversationSummary,
  Expense,
  TripSummary,
} from "../types";

/**
 * Base URL of the expenses agent.
 *
 * In development the Vite dev server proxies `/chat`, `/api` and `/health` to
 * the agent (see `vite.config.ts`), so the default empty base keeps every call
 * same-origin. That is what makes the app work from a phone on the LAN without
 * CORS or mixed-content problems.
 */
const API_BASE = (import.meta.env.VITE_AGENT_API_URL as string | undefined)?.replace(/\/+$/, "") ?? "";

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, init);

  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const body = await response.json();
      if (body?.detail) detail = typeof body.detail === "string" ? body.detail : JSON.stringify(body.detail);
    } catch {
      /* keep the status line */
    }
    throw new ApiError(detail, response.status);
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
  health: () => request<{ status: string; agentReady: boolean; error: string | null }>("/health"),

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

    return request<ChatResponse>("/chat", { method: "POST", body: form });
  },

  listTrips: (userId: string, status?: string) =>
    request<TripSummary[]>(`/api/trips${query({ userId, status })}`),

  getTrip: (userId: string, tripId: string) =>
    request<TripSummary>(`/api/trips/${encodeURIComponent(tripId)}${query({ userId })}`),

  deleteTrip: (userId: string, tripId: string) =>
    request<{ deleted: boolean }>(`/api/trips/${encodeURIComponent(tripId)}${query({ userId })}`, {
      method: "DELETE",
    }),

  listExpenses: (userId: string, tripId?: string) =>
    request<Expense[]>(`/api/expenses${query({ userId, tripId })}`),

  getExpense: (userId: string, expenseId: string) =>
    request<Expense>(`/api/expenses/${encodeURIComponent(expenseId)}${query({ userId })}`),

  deleteExpense: (userId: string, expenseId: string) =>
    request<{ deleted: boolean }>(`/api/expenses/${encodeURIComponent(expenseId)}${query({ userId })}`, {
      method: "DELETE",
    }),

  listConversations: (userId: string) =>
    request<ConversationSummary[]>(`/api/conversations${query({ userId })}`),

  getConversation: (userId: string, conversationId: string) =>
    request<Conversation>(`/api/conversations/${encodeURIComponent(conversationId)}${query({ userId })}`),
};
