/** Shapes returned by the records API and the expenses agent's chat endpoint. */

export interface Trip {
  id: string;
  userId: string;
  name: string;
  destination?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  purpose?: string | null;
  currency?: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface TripSummary {
  trip: Trip;
  expenseCount: number;
  totalAmount: number;
}

export interface ExpenseLineItem {
  description: string;
  category: string;
  price: number;
  quantity: number;
}

export interface Expense {
  id: string;
  userId: string;
  tripId: string;
  merchant: string;
  category: string;
  date: string;
  totalAmount: number;
  currency: string;
  lineItems: ExpenseLineItem[];
  notes?: string | null;
  sourceImage?: string | null;
  photoUrl?: string | null;
  conversationId?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ConversationMessage {
  role: "user" | "assistant" | "system" | "tool";
  text: string;
  toolCalls: string[];
  attachments: string[];
  createdAt: string;
}

export interface Conversation {
  id: string;
  userId: string;
  title: string;
  tripId?: string | null;
  messages: ConversationMessage[];
  createdAt: string;
  updatedAt: string;
}

export interface ConversationSummary {
  id: string;
  userId: string;
  title: string;
  tripId?: string | null;
  messageCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface ChatResponse {
  conversationId: string;
  userId: string;
  reply: string;
  toolCalls: string[];
  attachments: string[];
}

/** A message as rendered in the chat transcript. */
export interface ChatMessage {
  id: string;
  role: "user" | "assistant" | "error";
  text: string;
  toolCalls?: string[];
  attachments?: { name: string; url?: string }[];
  pending?: boolean;
}
