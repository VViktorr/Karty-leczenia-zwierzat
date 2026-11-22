import {defineConfig} from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  base: "/",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    assetsDir: "assets",
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [{name: "vendor-react", test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/}],
        },
      },
    },
  },
  server: {host: "127.0.0.1", port: 5173},
});
