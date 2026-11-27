import {defineConfig} from "vite";
import react from "@vitejs/plugin-react";

// Vite 8 uses Rolldown. Grouping by library and usage (initial vs lazy
// screens) keeps the start-up bundle small without pulling PDF/AG Grid into
// the first screen. maxSize is a target, not a hard guarantee; pdf.worker is
// a separate, already minified asset loaded only when a PDF is opened.
const chunkTarget = 300 * 1024;

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
          groups: [
            {
              name: "vendor-react",
              test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/,
              priority: 100,
            },
            {
              name: "vendor-grid",
              test: /node_modules[\\/](ag-grid-community|ag-grid-react)[\\/]/,
              priority: 90,
              entriesAware: true,
              maxSize: chunkTarget,
            },
            {
              name: "vendor-pdf",
              test: /node_modules[\\/](pdfjs-dist|react-pdf)[\\/]/,
              priority: 90,
              entriesAware: true,
              maxSize: chunkTarget,
            },
            {
              name: "vendor-antd",
              test: /node_modules[\\/](antd|@ant-design|@rc-component|rc-[^\\/]+)[\\/]/,
              priority: 80,
              entriesAware: true,
              maxSize: chunkTarget,
            },
            {
              name: "vendor-fluent",
              test: /node_modules[\\/]@fluentui[\\/]/,
              priority: 80,
              entriesAware: true,
              maxSize: chunkTarget,
            },
            {
              name: "vendor-other",
              test: /node_modules[\\/]/,
              priority: 10,
              entriesAware: true,
              maxSize: chunkTarget,
            },
          ],
        },
      },
    },
  },
  server: {host: "127.0.0.1", port: 5173},
});
