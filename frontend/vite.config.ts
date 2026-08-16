import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { VitePWA } from "vite-plugin-pwa";

// PWA config: installable on phone home screen, no app-store review cycle.
// Phase 1 notifications are in-app via SignalR. Web push (Phase 2) is wired
// here in a follow-up by adding 'registerType: "autoUpdate"' plus a push
// subscription flow against the same backend.
export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: "prompt",
      manifest: {
        name: "Incident Management",
        short_name: "Incidents",
        description: "Structured incident reporting and resolution",
        theme_color: "#1e3a8a",
        background_color: "#ffffff",
        display: "standalone",
        start_url: "/",
        icons: [
          { src: "/icon-192.png", sizes: "192x192", type: "image/png" },
          { src: "/icon-512.png", sizes: "512x512", type: "image/png" },
        ],
      },
    }),
  ],
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:5080",
      "/hubs": { target: "http://localhost:5080", ws: true },
    },
  },
});
