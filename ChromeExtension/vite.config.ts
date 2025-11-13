import { defineConfig, Plugin } from 'vite';
import { resolve } from 'path';
import { copyFileSync, mkdirSync } from 'fs';

// カスタムコピープラグイン
function copyFiles(): Plugin {
  return {
    name: 'copy-files',
    closeBundle() {
      // manifest.jsonをコピー
      copyFileSync('manifest.json', 'dist/manifest.json');

      // popup.htmlをコピー
      mkdirSync('dist/src/popup', { recursive: true });
      copyFileSync('src/popup/popup.html', 'dist/src/popup/popup.html');

      console.log('Files copied successfully');
    }
  };
}

export default defineConfig({
  plugins: [copyFiles()],
  build: {
    outDir: 'dist',
    rollupOptions: {
      input: {
        background: resolve(__dirname, 'src/background/background.ts'),
        popup: resolve(__dirname, 'src/popup/popup.ts'),
        content: resolve(__dirname, 'src/content/content.ts')
      },
      output: {
        entryFileNames: 'src/[name]/[name].js',
        chunkFileNames: 'chunks/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash][extname]'
      }
    },
    emptyOutDir: true
  }
});
