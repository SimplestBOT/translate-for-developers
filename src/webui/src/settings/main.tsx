import { createRoot } from 'react-dom/client'
import App from './App'
import { installBridge, post } from '../bridge/protocol'
import './settings.css'

installBridge()

// 全局错误上报（自动化诊断锚点：页面崩了管道可见，不白屏干等）
window.addEventListener('error', (e) => post('ui_event', 'page-error:' + e.message))
window.addEventListener('unhandledrejection', (e) =>
  post('ui_event', 'page-rejection:' + String(e.reason)),
)

// 注意：不包 StrictMode——实测在 WebView2 内嵌产物里 init 回环会挂
// （prod 下 StrictMode 本应 no-op，此处环境特例，详见阶段 4 记录）
createRoot(document.getElementById('root')!).render(<App />)
