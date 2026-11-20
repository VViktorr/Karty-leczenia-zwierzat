import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Split the large React/Ant Design vendor bundle into independently cached
// packages instead of merely increasing chunkSizeWarningLimit.
export default defineConfig({
  plugins: [react()],
  base: '/',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    assetsDir: 'assets',
    rollupOptions: {
      output: {
        onlyExplicitManualChunks: true,
        manualChunks(id) {
          const file=id.replaceAll('\\', '/');
          if (!file.includes('/node_modules/')) return;
          if (/\/node_modules\/(?:react|react-dom|scheduler)\//.test(file))
            return 'vendor-react';
          if (file.includes('/node_modules/@ant-design/icons') ||
              file.includes('/node_modules/@ant-design/cssinjs') ||
              file.includes('/node_modules/@ant-design/colors') ||
              file.includes('/node_modules/@ant-design/fast-color'))
            return 'vendor-antd-components';
          if (file.includes('/node_modules/rc-') ||
              file.includes('/node_modules/@rc-component/'))
            return 'vendor-antd-components';
          if (file.includes('/node_modules/antd/'))
            return 'vendor-antd';
        }
      }
    }
  },
  server: {host:'127.0.0.1',port:5173}
});
