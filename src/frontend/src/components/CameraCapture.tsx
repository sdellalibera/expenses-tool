import { useCallback, useEffect, useRef, useState } from "react";

interface Props {
  disabled?: boolean;
  onCapture: (file: File) => void;
  onError: (message: string) => void;
}

/**
 * Live camera preview with a shutter button.
 *
 * `facingMode: environment` asks for the rear camera on phones; on a laptop the
 * browser falls back to the built-in webcam. Falls back gracefully when the
 * browser exposes no camera API at all (the upload button still works).
 */
export default function CameraCapture({ disabled, onCapture, onError }: Props) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const [active, setActive] = useState(false);
  const [starting, setStarting] = useState(false);

  const stop = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (videoRef.current) videoRef.current.srcObject = null;
    setActive(false);
  }, []);

  useEffect(() => stop, [stop]);

  async function start() {
    if (!navigator.mediaDevices?.getUserMedia) {
      onError("This browser does not expose a camera. Use “Upload” instead.");
      return;
    }

    setStarting(true);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: "environment" } },
        audio: false,
      });
      streamRef.current = stream;
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play().catch(() => undefined);
      }
      setActive(true);
    } catch (error) {
      onError(`Unable to access the camera: ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      setStarting(false);
    }
  }

  async function shoot() {
    const video = videoRef.current;
    if (!video?.videoWidth || !video.videoHeight) return;

    const canvas = document.createElement("canvas");
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;

    const context = canvas.getContext("2d");
    if (!context) {
      onError("Could not capture the image.");
      return;
    }
    context.drawImage(video, 0, 0, canvas.width, canvas.height);

    const blob = await new Promise<Blob | null>((resolve) =>
      canvas.toBlob((value) => resolve(value), "image/jpeg", 0.92),
    );
    if (!blob) {
      onError("Could not encode the captured image.");
      return;
    }

    onCapture(new File([blob], `receipt-${Date.now()}.jpg`, { type: "image/jpeg" }));
    stop();
  }

  return (
    <div className={`camera ${active ? "camera-active" : ""}`}>
      <video ref={videoRef} className="camera-preview" playsInline muted hidden={!active} />

      {active ? (
        <div className="camera-actions">
          <button type="button" className="primary" onClick={shoot} disabled={disabled}>
            Take picture
          </button>
          <button type="button" className="ghost" onClick={stop}>
            Cancel
          </button>
        </div>
      ) : (
        <button type="button" className="ghost" onClick={start} disabled={disabled || starting}>
          {starting ? "Starting camera…" : "📷 Camera"}
        </button>
      )}
    </div>
  );
}
