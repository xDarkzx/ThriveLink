import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// The built page is served by the .NET API from wwwroot, so one App Service hosts both.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: "../src/ThriveLink.Api/wwwroot",
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: { "/api": "http://localhost:5080", "/health": "http://localhost:5080" },
  },
});
