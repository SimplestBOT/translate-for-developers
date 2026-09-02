//=============================================================
// bridge/protocol.ts - 页面↔宿主消息契约实现（docs/protocol.md）
// 页面→宿主: chrome.webview.postMessage(JSON {v,type,requestId,payload:[args]})
// 宿主→页面: window.__recv({v,type,requestId,payload})
// 阶段 4 起各 React 页面共用本层；协议变更只改这里与 protocol.md。
//=============================================================

export interface Envelope<T = unknown> {
  v: number
  type: string
  requestId: number | string
  payload: T
}

type Handler = (payload: never, env: Envelope) => void

let REQ = 0
const handlers = new Map<string, Handler[]>()

/** 页面→宿主（payload 为参数数组，与旧页 post(t, ...args) 语义一致） */
export function post(type: string, ...args: unknown[]): void {
  const webview = (window as unknown as { chrome?: { webview?: { postMessage(s: string): void } } }).chrome?.webview
  if (!webview) return // 非宿主环境（vite dev 浏览器调试）静默
  webview.postMessage(JSON.stringify({ v: 1, type, requestId: ++REQ, payload: args }))
}

/** 订阅宿主→页面消息，返回取消函数（React useEffect cleanup 直接用） */
export function on<P>(type: string, handler: (payload: P, env: Envelope<P>) => void): () => void {
  const list = handlers.get(type) ?? []
  list.push(handler as Handler)
  handlers.set(type, list)
  return () => {
    const cur = handlers.get(type)
    if (!cur) return
    const i = cur.indexOf(handler as Handler)
    if (i >= 0) cur.splice(i, 1)
  }
}

function recv(raw: unknown): void {
  let env = raw as Envelope | string | null
  if (typeof env === 'string') {
    try {
      env = JSON.parse(env) as Envelope
    } catch {
      return
    }
  }
  if (!env || typeof env !== 'object' || typeof env.type !== 'string') return
  // 宿主对每帧双通道推送（PostWebMessageAsJson 主 + __recv 注入兜底，MainWindow.Push），
  // React 页两条通道都挂（installBridge），同一信封会到达两次；宿主冷启动期注入通道
  // 可能延迟 500ms+ 执行。settings 页处理器幂等无感，但 result 页 init 非幂等
  // （init→post('translate')），重复帧会重复发起翻译。在协议层按「短窗口内完全
  // 相同的信封」去重，保证 on() 处理器每帧只执行一次。
  // 注意两条通道的键序不同（PostWebMessageAsJson 经宿主解析再序列化，对象键被
  // 重排为字母序；__recv 注入保留 AHK 原始 JSON 文本键序），故 key 必须先规范化
  // （递归排序对象键）再比较。
  const key = JSON.stringify(canonical(env))
  const now = Date.now()
  if (key === lastFrameKey && now - lastFrameAt < 2000) return
  lastFrameKey = key
  lastFrameAt = now
  for (const h of handlers.get(env.type) ?? []) h(env.payload as never, env)
}

let lastFrameKey = ''
let lastFrameAt = 0

/** 递归排序对象键，产出键序无关的规范化结构（用于信封等价比较） */
function canonical(v: unknown): unknown {
  if (Array.isArray(v)) return v.map(canonical)
  if (v && typeof v === 'object') {
    const src = v as Record<string, unknown>
    const out: Record<string, unknown> = {}
    for (const k of Object.keys(src).sort()) out[k] = canonical(src[k])
    return out
  }
  return v
}

/** 装桥：挂 window.__recv 兜底 + 监听 WebMessage（宿主 PostWebMessage 正向通道）+ 禁右键 */
export function installBridge(): void {
  ;(window as unknown as { __recv?: unknown }).__recv = recv
  // 宿主 PostWebMessageAsJson/String 触发 message 事件（不依赖页面全局，主通道）
  const wv = (
    window as unknown as {
      chrome?: { webview?: { addEventListener(t: string, h: (e: { data?: unknown }) => void): void } }
    }
  ).chrome?.webview
  if (wv) wv.addEventListener('message', (e) => recv(e.data))
  window.addEventListener('contextmenu', (e) => e.preventDefault())
}
