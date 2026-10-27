import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// The built page is served by the .NET API from wwwroot, so one App Service hosts both.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: "../src/ThriveLink.Api/wwwroot",
    emptyOutDir: true,
    // Recharts is most of the bundle; 700 kB (≈190 kB gzipped) is fine for an internal ops page.
    chunkSizeWarningLimit: 700,
  },
  server: {
    port: 5173,
    proxy: { "/api": "http://localhost:5080", "/health": "http://localhost:5080" },
  },
});
