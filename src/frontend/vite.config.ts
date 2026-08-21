import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The agent API URL is injected by the Aspire AppHost via the
// `services__expenses-agent__http__0` environment variable, which Vite exposes
// to the browser through `import.meta.env.VITE_AGENT_API_URL`. See main.tsx.
export default defineConfig(({ mode }) => {
  const port = Number(process.env.PORT ?? 5173);

  // Promote Aspire-injected service discovery env vars into VITE_-prefixed ones
  // so they are available in the browser bundle.
  const agentUrl =
    process.env["services__expenses-agent__https__0"] ??
    process.env["services__expenses-agent__http__0"] ??
    process.env.VITE_AGENT_API_URL ??
    "";

  process.env.VITE_AGENT_API_URL = agentUrl;

  return {
    plugins: [react()],
    server: {
      host: "0.0.0.0",
      port,
      strictPort: true,
    },
    preview: {
      host: "0.0.0.0",
      port,
    },
  };
});
