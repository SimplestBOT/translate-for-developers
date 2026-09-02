// result.html 页面入口（4b：React 版结果窗，双轨与 legacy scripts/html/result.html 对齐）
// 注意：不用 StrictMode（React19+WebView2 内嵌产物 init 回环会挂，见阶段 4a 记录）
import { createRoot } from 'react-dom/client'
import App from './App'
import { installBridge, post } from '../bridge/protocol'
import './result.css'

installBridge()

// 全局错误上报（自动化诊断锚点）
window.addEventListener('error', (e) => post('ui_event', 'page-error:' + e.message))
window.addEventListener('unhandledrejection', (e) =>
  post('ui_event', 'page-rejection:' + String(e.reason)),
)

createRoot(document.getElementById('root')!).render(<App />)
