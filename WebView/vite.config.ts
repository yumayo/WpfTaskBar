import { defineConfig, Plugin } from 'vite'

function removeModuleType(): Plugin {
  return {
    name: 'remove-module-type',
    transformIndexHtml(html) {
      return html.replace(/\s*crossorigin/g, '').replace(/\s*type="module"/g, '')
    }
  }
}

export default defineConfig({
  base: './',
  plugins: [removeModuleType()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    rollupOptions: {
      input: {
        main: './index.html'
      },
      output: {
        format: 'iife',
        entryFileNames: 'assets/[name]-[hash].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash].[ext]'
      }
    }
  }
})
