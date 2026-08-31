import { useCallback, useEffect, useMemo, useRef, useState } from "react";

interface LineItem {
  description: string | null;
  price: number | null;
}

interface ReportResponse {
  conversationId: string;
  recordId: string;
  userId: string;
  lineItems: LineItem[];
  total: number | null;
  category: string;
}

const AGENT_API_URL =
  (import.meta.env.VITE_AGENT_API_URL as string | undefined)?.replace(/\/$/, "") ??
  "";

const USER_ID_STORAGE_KEY = "expenses.userId";

function getOrCreateUserId(): string {
  if (typeof window === "undefined") return "anonymous";
  try {
    const existing = window.localStorage.getItem(USER_ID_STORAGE_KEY);
    if (existing) return existing;
    const fresh =
      typeof crypto !== "undefined" && "randomUUID" in crypto
        ? crypto.randomUUID()
        : `user-${Math.random().toString(36).slice(2, 10)}`;
    window.localStorage.setItem(USER_ID_STORAGE_KEY, fresh);
    return fresh;
  } catch {
    return "anonymous";
  }
}

export default function App() {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);

  const [file, setFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [userId] = useState<string>(() => getOrCreateUserId());
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<ReportResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [cameraActive, setCameraActive] = useState(false);
  const [cameraStarting, setCameraStarting] = useState(false);

  const apiBase = useMemo(() => {
    if (AGENT_API_URL) return AGENT_API_URL;
    return "";
  }, []);

  const stopCamera = useCallback(() => {
    const stream = streamRef.current;
    if (stream) {
      stream.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
    }
    if (videoRef.current) {
      videoRef.current.srcObject = null;
    }
    setCameraActive(false);
  }, []);

  useEffect(() => {
    return () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    };
  }, [previewUrl]);

  useEffect(() => {
    return () => {
      stopCamera();
    };
  }, [stopCamera]);

  const submitFile = useCallback(
    async (fileToSend: File) => {
      setSubmitting(true);
      setError(null);
      setResult(null);
      try {
        const formData = new FormData();
        formData.append("image", fileToSend, fileToSend.name || "receipt.jpg");
        if (note) formData.append("note", note);
        if (conversationId) formData.append("conversationId", conversationId);
        formData.append("userId", userId);

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
    },
    [apiBase, conversationId, note, userId],
  );

  async function startCamera() {
    setError(null);
    if (!navigator.mediaDevices?.getUserMedia) {
      setError("Camera API is not available in this browser.");
      return;
    }
    setCameraStarting(true);
    try {
      // Prompts the browser for camera permission; prefers the rear camera on mobile.
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: "environment" } },
        audio: false,
      });
      streamRef.current = stream;
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play().catch(() => undefined);
      }
      setCameraActive(true);
      if (previewUrl) URL.revokeObjectURL(previewUrl);
      setFile(null);
      setPreviewUrl(null);
      setResult(null);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setError(`Unable to access camera: ${message}`);
    } finally {
      setCameraStarting(false);
    }
  }

  async function capturePhoto() {
    const video = videoRef.current;
    if (!video || !video.videoWidth || !video.videoHeight) return;

    const canvas = document.createElement("canvas");
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    const ctx = canvas.getContext("2d");
    if (!ctx) {
      setError("Could not capture image from camera.");
      return;
    }
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

    const blob: Blob | null = await new Promise((resolve) =>
      canvas.toBlob((b) => resolve(b), "image/jpeg", 0.92),
    );
    if (!blob) {
      setError("Could not encode captured image.");
      return;
    }

    const captured = new File([blob], `receipt-${Date.now()}.jpg`, {
      type: "image/jpeg",
    });

    if (previewUrl) URL.revokeObjectURL(previewUrl);
    const url = URL.createObjectURL(captured);
    setFile(captured);
    setPreviewUrl(url);
    stopCamera();

    await submitFile(captured);
  }

  async function handleFileSelected(event: React.ChangeEvent<HTMLInputElement>) {
    const selected = event.target.files?.[0];
    if (!selected) return;
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    setFile(selected);
    setPreviewUrl(URL.createObjectURL(selected));
    setResult(null);
    setError(null);
    stopCamera();
    await submitFile(selected);
  }

  function reset() {
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    setFile(null);
    setPreviewUrl(null);
    setResult(null);
    setError(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
    stopCamera();
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
        <div className="preview-wrapper">
          <video
            ref={videoRef}
            className={`preview video ${cameraActive ? "" : "hidden"}`}
            playsInline
            muted
          />
          {!cameraActive && previewUrl && (
            <img className="preview" src={previewUrl} alt="Receipt preview" />
          )}
          {!cameraActive && !previewUrl && (
            <div className="preview placeholder" aria-hidden="true" />
          )}
        </div>

        <input
          ref={fileInputRef}
          type="file"
          accept="image/*"
          onChange={handleFileSelected}
        />

        <div className="button-row">
          {!cameraActive ? (
            <button onClick={startCamera} disabled={submitting || cameraStarting}>
              {cameraStarting
                ? "Starting camera…"
                : file
                  ? "Retake photo"
                  : "Open camera"}
            </button>
          ) : (
            <button onClick={capturePhoto} disabled={submitting}>
              Take picture
            </button>
          )}
          <button
            className="secondary"
            onClick={() => fileInputRef.current?.click()}
            disabled={submitting}
          >
            Upload image
          </button>
          {(file || cameraActive) && (
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

        {submitting && <p className="meta">Analyzing…</p>}
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
          <div className="meta">
            Category: <code>{result.category}</code>
            <br />
            Total:{" "}
            <code>
              {result.total !== null && result.total !== undefined
                ? result.total.toFixed(2)
                : "—"}
            </code>
          </div>
          {result.lineItems.length > 0 ? (
            <ul>
              {result.lineItems.map((item, idx) => (
                <li key={idx}>
                  {item.description ?? "(no description)"} —{" "}
                  <code>
                    {item.price !== null && item.price !== undefined
                      ? item.price.toFixed(2)
                      : "—"}
                  </code>
                </li>
              ))}
            </ul>
          ) : (
            <div>No line items detected.</div>
          )}
          <div className="meta">
            Conversation: <code>{result.conversationId}</code>
            <br />
            Record: <code>{result.recordId}</code>
            <br />
            User: <code>{result.userId}</code>
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
