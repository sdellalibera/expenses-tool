import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";

import { api, ApiError } from "../api/client";
import type { ChatMessage } from "../types";
import { useUserId } from "./useUserId";

const WELCOME: ChatMessage = {
  id: "welcome",
  role: "assistant",
  text:
    "Hi! Tell me about a work trip and send me your receipts \u2014 I will file each one " +
    "under the right trip. Try: \u201cCreate a trip to Munich from 1 to 5 March\u201d, then snap a receipt.",
};

let messageCounter = 0;
const nextId = () => `m${++messageCounter}-${Date.now()}`;

function useChatState() {
  const userId = useUserId();
  const objectUrls = useRef(new Set<string>());
  const [messages, setMessages] = useState<ChatMessage[]>([WELCOME]);
  const [draft, setDraft] = useState("");
  const [pending, setPending] = useState<File[]>([]);
  const [previews, setPreviews] = useState<string[]>([]);
  const [conversationId, setConversationId] = useState<string>(() => crypto.randomUUID());
  const [sending, setSending] = useState(false);

  useEffect(() => {
    const urls = objectUrls.current;
    return () => {
      urls.forEach((url) => URL.revokeObjectURL(url));
      urls.clear();
    };
  }, []);

  const createPreview = useCallback((file: File) => {
    const url = URL.createObjectURL(file);
    objectUrls.current.add(url);
    return url;
  }, []);

  const addFiles = useCallback((files: File[]) => {
    if (!files.length) return;
    const urls = files.map(createPreview);
    setPending((current) => [...current, ...files]);
    setPreviews((current) => [...current, ...urls]);
  }, [createPreview]);

  const pushError = useCallback((text: string) => {
    setMessages((current) => [...current, { id: nextId(), role: "error", text }]);
  }, []);

  function removePending(index: number) {
    URL.revokeObjectURL(previews[index]);
    objectUrls.current.delete(previews[index]);
    setPending((current) => current.filter((_, fileIndex) => fileIndex !== index));
    setPreviews((current) => current.filter((_, fileIndex) => fileIndex !== index));
  }

  async function send(event?: FormEvent) {
    event?.preventDefault();

    const text = draft.trim();
    if ((!text && pending.length === 0) || sending) return;

    const attachments = pending.map((file, index) => ({ name: file.name, url: previews[index] }));
    const outgoing: ChatMessage = {
      id: nextId(),
      role: "user",
      text: text || "(receipt)",
      attachments,
    };
    const thinking: ChatMessage = { id: nextId(), role: "assistant", text: "Analysing\u2026", pending: true };

    setMessages((current) => [...current, outgoing, thinking]);
    setDraft("");
    const images = pending;
    setPending([]);
    setPreviews([]);
    setSending(true);

    try {
      const response = await api.chat({ userId, message: text, conversationId, images });
      setConversationId(response.conversationId);
      setMessages((current) =>
        current.map((message) =>
          message.id === thinking.id
            ? { id: message.id, role: "assistant", text: response.reply, toolCalls: response.toolCalls }
            : message,
        ),
      );
    } catch (error) {
      const detail =
        error instanceof ApiError
          ? `${error.message}${error.retryAfterSeconds ? ` Retry after ${error.retryAfterSeconds} seconds.` : ""}${error.status === 503 ? " (is the agent configured?)" : ""}`
          : error instanceof Error
            ? error.message
            : String(error);
      setMessages((current) =>
        current.map((message) =>
          message.id === thinking.id ? { id: message.id, role: "error", text: detail } : message,
        ),
      );
      setDraft(text);
      setPending(images);
      setPreviews(images.map(createPreview));
    } finally {
      setSending(false);
    }
  }

  return { messages, draft, setDraft, pending, previews, sending, addFiles, pushError, removePending, send };
}

const ChatSessionContext = createContext<ReturnType<typeof useChatState> | null>(null);

export function ChatSessionProvider({ children }: { children: ReactNode }) {
  const session = useChatState();
  return <ChatSessionContext.Provider value={session}>{children}</ChatSessionContext.Provider>;
}

export function useChatSession() {
  const session = useContext(ChatSessionContext);
  if (!session) throw new Error("useChatSession must be used within ChatSessionProvider");
  return session;
}