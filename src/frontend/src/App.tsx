import { useEffect, useMemo, useRef, useState } from "react";

interface ReportResponse {
  conversationId: string;
  blobName: string;
  analysisSummary: string;
}

const AGENT_API_URL =
  (import.meta.env.VITE_AGENT_API_URL as string | undefined)?.replace(/\/$/, "") ??
  "";

export default function App() {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<ReportResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Revoke the preview object URL when it changes or the component unmounts.
  useEffect(() => {
    return () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    };
  }, [previewUrl]);

  const apiBase = useMemo(() => {
    if (AGENT_API_URL) return AGENT_API_URL;
    // Fall back to same-origin (useful when frontend and api are reverse-proxied together).
    return "";
  }, []);

  function handleFileSelected(event: React.ChangeEvent<HTMLInputElement>) {
    const selected = event.target.files?.[0];
    if (!selected) return;
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    setFile(selected);
    setPreviewUrl(URL.createObjectURL(selected));
    setResult(null);
    setError(null);
  }

  function reset() {
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    setFile(null);
    setPreviewUrl(null);
    setResult(null);
    setError(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  async function submit() {
    if (!file) return;
    setSubmitting(true);
    setError(null);
    setResult(null);
    try {
      const formData = new FormData();
      formData.append("image", file, file.name || "receipt.jpg");
      if (note) formData.append("note", note);
      if (conversationId) formData.append("conversationId", conversationId);

      const response = await fetch(`${apiBase}/expenses/report`, {
        method: "POST",
        body: formData,
      });

      if (!response.ok) {
        const text = await response.text();
        throw new Error(`Upload failed (${response.status}): ${text}`);
      }

      const body = (await response.json()) as ReportResponse;
      setResult(body);
      setConversationId(body.conversationId);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="app">
      <header>
        <h1>📸 Capture an expense</h1>
        <p className="lead">
          Snap a photo of your receipt and the agent will extract the details.
        </p>
      </header>

      <section className="capture">
        {previewUrl ? (
          <img className="preview" src={previewUrl} alt="Receipt preview" />
        ) : (
          <div className="preview" aria-hidden="true" />
        )}

        <input
          ref={fileInputRef}
          type="file"
          accept="image/*"
          capture="environment"
          onChange={handleFileSelected}
        />

        <div className="button-row">
          <button onClick={() => fileInputRef.current?.click()} disabled={submitting}>
            {file ? "Retake / choose another" : "Open camera"}
          </button>
          {file && (
            <button className="secondary" onClick={reset} disabled={submitting}>
              Clear
            </button>
          )}
        </div>

        <textarea
          placeholder="Optional note (e.g. 'client lunch with Acme')"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          disabled={submitting}
        />

        <button onClick={submit} disabled={!file || submitting}>
          {submitting ? "Analyzing…" : "Create expense report"}
        </button>
      </section>

      {error && (
        <section className="result error">
          <strong>Error</strong>
          <div>{error}</div>
        </section>
      )}

      {result && (
        <section className="result">
          <strong>Agent analysis</strong>
          <div>{result.analysisSummary}</div>
          <div className="meta">
            Blob: <code>{result.blobName}</code>
            <br />
            Conversation: <code>{result.conversationId}</code>
          </div>
        </section>
      )}

      {!AGENT_API_URL && (
        <p className="meta">
          ⚠️ <code>VITE_AGENT_API_URL</code> is not set. Requests will be sent to the
          same origin.
        </p>
      )}
    </main>
  );
}
