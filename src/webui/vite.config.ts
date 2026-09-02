// webui 构建：按页构建 + 单文件内联产物
// 用法：npm run build（默认 settings）或 TFD_PAGE=result npm run build
// 产物 dist/<page>.html（自包含 JS/CSS）——AHK Wv2Html() 按页名优先加载，
// 不存在时自动回退 scripts/html/<page>.html（双轨可回退）。
// 说明：vite-plugin-singlefile 不支持多 html 入口（inlineDynamicImports 限制），
// 故每页独立构建；新增页面在 PAGES 登记 + 跑多轮 build。
// NavigateToString 上限 2MB，单页内联产物约 160-220KB，安全余量充足。
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { viteSingleFile } from 'vite-plugin-singlefile'
import { resolve } from 'path'

export const PAGES = ['settings', 'result', 'capture', 'config'] as const

const page = process.env.TFD_PAGE ?? 'settings'
if (!(PAGES as readonly string[]).includes(page)) {
  throw new Error(`未知页面：${page}（可选：${PAGES.join(', ')}）`)
}

export default defineConfig({
  plugins: [react(), viteSingleFile()],
  build: {
    outDir: 'dist',
    emptyOutDir: process.env.TFD_PAGE === undefined, // 按页构建时不清空其他页产物
    rollupOptions: {
      input: { [page]: resolve(__dirname, `${page}.html`) },
    },
  },
})
