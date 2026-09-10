import { useCallback, useEffect, useRef, useState } from "react";

import { api, ApiError } from "../api/client";
import CameraCapture from "../components/CameraCapture";
import { useUserId } from "../hooks/useUserId";
import type { ChatMessage } from "../types";

const WELCOME: ChatMessage = {
  id: "welcome",
  role: "assistant",
  text:
    "Hi! Tell me about a work trip and send me your receipts — I will file each one " +
    "under the right trip. Try: “Create a trip to Munich from 1 to 5 March”, then snap a receipt.",
};

let messageCounter = 0;
const nextId = () => `m${++messageCounter}-${Date.now()}`;

/** `/home` — the main chat surface. */
export default function ChatPage() {
  const userId = useUserId();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const transcriptRef = useRef<HTMLDivElement>(null);

  const [messages, setMessages] = useState<ChatMessage[]>([WELCOME]);
  const [draft, setDraft] = useState("");
  const [pending, setPending] = useState<File[]>([]);
  const [previews, setPreviews] = useState<string[]>([]);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [sending, setSending] = useState(false);

  useEffect(() => {
    transcriptRef.current?.scrollTo({ top: transcriptRef.current.scrollHeight, behavior: "smooth" });
  }, [messages]);

  useEffect(() => () => previews.forEach((url) => URL.revokeObjectURL(url)), [previews]);

  const addFiles = useCallback((files: File[]) => {
    if (!files.length) return;
    setPending((current) => [...current, ...files]);
    setPreviews((current) => [...current, ...files.map((file) => URL.createObjectURL(file))]);
  }, []);

  const pushError = useCallback((text: string) => {
    setMessages((current) => [...current, { id: nextId(), role: "error", text }]);
  }, []);

  function removePending(index: number) {
    URL.revokeObjectURL(previews[index]);
    setPending((current) => current.filter((_, i) => i !== index));
    setPreviews((current) => current.filter((_, i) => i !== index));
  }

  async function send(event?: React.FormEvent) {
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
    const thinking: ChatMessage = { id: nextId(), role: "assistant", text: "Analysing…", pending: true };

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
          ? `${error.message}${error.status === 503 ? " (is the agent configured?)" : ""}`
          : error instanceof Error
            ? error.message
            : String(error);
      setMessages((current) =>
        current.map((message) =>
          message.id === thinking.id ? { id: message.id, role: "error", text: detail } : message,
        ),
      );
    } finally {
      setSending(false);
    }
  }

  return (
    <section className="chat" aria-label="Chat with the expenses agent">
      <div className="transcript" ref={transcriptRef}>
        {messages.map((message) => (
          <article key={message.id} className={`bubble bubble-${message.role} ${message.pending ? "is-pending" : ""}`}>
            {message.attachments && message.attachments.length > 0 && (
              <div className="bubble-attachments">
                {message.attachments.map((attachment) => (
                  <img key={attachment.name} src={attachment.url} alt={attachment.name} />
                ))}
              </div>
            )}
            <p>{message.text}</p>
            {message.toolCalls && message.toolCalls.length > 0 && (
              <ul className="tool-calls" aria-label="Tools used">
                {message.toolCalls.map((tool, index) => (
                  <li key={`${tool}-${index}`}>
                    <code>{tool}</code>
                  </li>
                ))}
              </ul>
            )}
          </article>
        ))}
      </div>

      {pending.length > 0 && (
        <div className="staged" aria-label="Receipts ready to send">
          {pending.map((file, index) => (
            <figure key={`${file.name}-${index}`}>
              <img src={previews[index]} alt={file.name} />
              <button type="button" aria-label={`Remove ${file.name}`} onClick={() => removePending(index)}>
                ×
              </button>
            </figure>
          ))}
        </div>
      )}

      <CameraCapture disabled={sending} onCapture={(file) => addFiles([file])} onError={pushError} />

      <form className="composer" onSubmit={send}>
        <input
          ref={fileInputRef}
          type="file"
          accept="image/*"
          multiple
          hidden
          data-testid="file-input"
          onChange={(event) => {
            addFiles(Array.from(event.target.files ?? []));
            event.target.value = "";
          }}
        />

        <button
          type="button"
          className="ghost"
          aria-label="Upload receipts"
          disabled={sending}
          onClick={() => fileInputRef.current?.click()}
        >
          📎
        </button>

        <textarea
          value={draft}
          rows={1}
          placeholder="Ask the agent, or attach a receipt…"
          aria-label="Message"
          disabled={sending}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              void send();
            }
          }}
        />

        <button type="submit" className="primary" disabled={sending || (!draft.trim() && pending.length === 0)}>
          {sending ? "…" : "Send"}
        </button>
      </form>
    </section>
  );
}
