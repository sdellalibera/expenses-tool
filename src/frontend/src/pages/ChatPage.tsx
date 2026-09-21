import { useEffect, useRef } from "react";

import CameraCapture from "../components/CameraCapture";
import { useChatSession } from "../hooks/useChatSession";

/** `/home` — the main chat surface. */
export default function ChatPage() {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const transcriptRef = useRef<HTMLDivElement>(null);
  const { messages, draft, setDraft, pending, previews, sending, addFiles, pushError, removePending, send } = useChatSession();

  useEffect(() => {
    transcriptRef.current?.scrollTo({ top: transcriptRef.current.scrollHeight, behavior: "smooth" });
  }, [messages]);

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
