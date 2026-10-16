// SPDX-License-Identifier: LicenseRef-BRIDGE
// BRIDGE client build. `npm run build` writes the page into bridge_studio/static,
// which the Python service serves. `npm run dev` proxies the API and the socket
// to a service started with `python -m bridge_studio --dev` on port 8800.
import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const repoRoot = fileURLToPath(new URL("..", import.meta.url));

export default defineConfig({
  plugins: [react()],
  base: "/",
  build: {
    outDir: "../bridge_studio/static",
    emptyOutDir: true,
    chunkSizeWarningLimit: 1800,
    sourcemap: false,
  },
  server: {
    port: 5173,
    // The BRIDGE wordmark lives at the repository root (light.png, dark.png).
    fs: { allow: [repoRoot] },
    proxy: {
      "/api": "http://127.0.0.1:8800",
      "/docs": "http://127.0.0.1:8800",
      "/openapi.json": "http://127.0.0.1:8800",
      "/ws": { target: "ws://127.0.0.1:8800", ws: true },
    },
  },
});
