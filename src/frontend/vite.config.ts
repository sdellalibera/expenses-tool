import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

/**
 * The AppHost injects the agent URL through Aspire service discovery
 * (`services__expenses-agent__http__0`).
 *
 * The dev server *proxies* `/chat`, `/api` and `/health` to that URL instead of
 * letting the browser call it directly. Keeping the API same-origin means the
 * app also works when you open it from your phone on the LAN — no CORS, no
 * mixed content, no hard-coded localhost.
 */
function agentUrl(): string {
  return (
    process.env["services__expenses-agent__http__0"] ??
    process.env["services__expenses-agent__https__0"] ??
    process.env.VITE_AGENT_API_URL ??
    "http://localhost:8000"
  );
}

export default defineConfig(() => {
  const target = agentUrl();
  const port = Number(process.env.PORT ?? 5173);

  const proxy = {
    "/chat": { target, changeOrigin: true, secure: false },
    "/api": { target, changeOrigin: true, secure: false },
    "/health": { target, changeOrigin: true, secure: false },
  };

  return {
    plugins: [react()],
    server: { host: "0.0.0.0", port, strictPort: true, proxy },
    preview: { host: "0.0.0.0", port, proxy },
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: ["./src/test/setup.ts"],
      css: false,
    },
  };
});
