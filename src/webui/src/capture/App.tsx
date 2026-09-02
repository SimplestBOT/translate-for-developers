//=============================================================
// capture/App.tsx - 热键捕获页编排（4c React 版）
// 行为与 legacy scripts/html/capture.html 逐项对齐：
//   init → chip 显示当前热键 → capturing 点阵（AHK ready 后自动 CaptureRound）
//   captured → 键帽展示 + 应用/重新捕获/取消 · error → 点阵 + alert
//   Esc 不关窗（__noEsc）：Esc 是捕获输入，由 AHK InputHook 处理（取消并关窗）
// 消息契约 = docs/protocol.md §3（capturing/captured/captureCancelled）
//=============================================================
import { useEffect, useState } from 'react'
import { on, post } from '../bridge/protocol'
import type { CaptureInit, ErrorFrame, HotkeyPayload } from '../bridge/types'
import CaptureBox from './components/CaptureBox'

export default function App() {
  const [init, setInit] = useState<CaptureInit | null>(null)
  const [ndrag, setNdrag] = useState(false)
  const [capturing, setCapturing] = useState(true)
  const [keys, setKeys] = useState<string[]>([])

  // 宿主→页面消息订阅（先订阅后 ready，时序原因见 4a）
  useEffect(() => {
    const offs = [
      on<CaptureInit>('init', (d) => {
        ;(window as unknown as { __ndrag?: boolean }).__ndrag = !!d.ndrag
        setNdrag(!!d.ndrag)
        setInit(d)
        setTimeout(() => post('ui_event', 'capture-rendered'), 0)
      }),
      on('capturing', () => {
        setCapturing(true)
        setKeys([])
      }),
      on<HotkeyPayload>('captured', (d) => {
        setCapturing(false)
        setKeys(d.keys ?? [])
      }),
      on('captureCancelled', () => {
        // 独立捕获窗里 Esc 由 AHK 直接关窗，此帧正常不到达；防御性回点阵
        setCapturing(true)
        setKeys([])
      }),
      on<ErrorFrame>('error', (d) => {
        setCapturing(true)
        setKeys([])
        window.alert(d.message || '应用失败')
      }),
    ]
    return () => offs.forEach((off) => off())
  }, [])

  // ready 在订阅就绪后发（AHK 收到后推 init 并启动 CaptureRound）
  useEffect(() => {
    post('ready')
  }, [])

  return (
    <div className="wrap">
      <header
        className="rv"
        style={{ ['--d' as string]: '0ms' }}
        onMouseDown={(e) => {
          if (!ndrag && e.button === 0 && !(e.target as HTMLElement).closest('button,input,a,.link'))
            post('drag')
        }}
      >
        <div className="logo">译</div>
        <div className="brand">translator</div>
        <div className="pair">
          <span className="chip">
            当前：<span>{init?.cur ?? ''}</span>
          </span>
        </div>
        <button className="xbtn" title="关闭" onClick={() => post('close')}>
          ✕
        </button>
      </header>

      <div className="hero rv" style={{ ['--d' as string]: '40ms' }}>
        <h1>按下新的热键组合</h1>
        <div className="sub">支持 Ctrl / Alt / Shift / Win 与鼠标侧键</div>
      </div>

      <CaptureBox capturing={capturing} keys={keys} />

      <div className={'acts' + (!capturing && keys.length > 0 ? ' on' : '')}>
        <button className="btn pri" onClick={() => post('apply')}>
          应用热键
        </button>
        <button
          className="btn"
          onClick={() => {
            setCapturing(true)
            setKeys([])
            post('recapture')
          }}
        >
          重新捕获
        </button>
        <button className="btn" onClick={() => post('cancel')}>
          取消
        </button>
      </div>

      <div className="subfoot rv" style={{ ['--d' as string]: '120ms' }}>
        按 <b>Esc</b> 取消并关闭
      </div>
    </div>
  )
}
