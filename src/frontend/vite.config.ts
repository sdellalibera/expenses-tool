import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

/**
 * The AppHost injects agent and records URLs through Aspire service discovery.
 *
 * Proxy agent chat separately from records and receipt photos.
 * Same-origin browser requests also work from a phone on the LAN without CORS,
 * mixed content, or hard-coded localhost.
 */
function agentUrl(): string {
  return (
    process.env["services__expenses-agent__http__0"] ??
    process.env["services__expenses-agent__https__0"] ??
    process.env.VITE_AGENT_API_URL ??
    "http://localhost:8000"
  );
}

function recordsUrl(): string {
  return (
    process.env["services__expenses-api__http__0"] ??
    process.env["services__expenses-api__https__0"] ??
    process.env.VITE_RECORDS_API_URL ??
    "http://localhost:5291"
  );
}

export default defineConfig(() => {
  const agentTarget = agentUrl();
  const recordsTarget = recordsUrl();
  const port = Number(process.env.PORT ?? 5173);

  const proxy = {
    "/chat": { target: agentTarget, changeOrigin: true, secure: false },
    "/health": { target: agentTarget, changeOrigin: true, secure: false },
    "/api": { target: recordsTarget, changeOrigin: true, secure: false },
  };

  return {
    plugins: [react()],
    server: { host: "0.0.0.0", port, strictPort: true, proxy },
    preview: { host: "0.0.0.0", port, proxy },
  };
});
