// HotkeyCard - 热键卡片：空闲（键帽数组）/ 捕获中（三点动画 + 大框）/ 已捕获（可应用/重捕/取消）
// 视觉与消息语义与 legacy settings.html 热键区一致
import { useEffect, useState } from 'react'

interface Props {
  keys: string[]
  capturing: boolean
  capturedKeys: string[] | null
  stateText: string
  onChange: () => void
  onApply: () => void
  onRecap: () => void
  onCancel: () => void
}

export default function HotkeyCard(p: Props) {
  // 捕获中态的临时数据：capturedKeys 切换时清输入焦点残留
  const [capMode, setCapMode] = useState(false)
  useEffect(() => {
    setCapMode(p.capturing || p.capturedKeys != null)
  }, [p.capturing, p.capturedKeys])

  return (
    <>
      <div className="flabel">
        <span>翻译热键</span>
        <b>{p.stateText}</b>
      </div>
      {!capMode ? (
        <div className="hkrow">
          <div className="kcaps">
            {p.keys.map((k, i) => (
              <span key={i} style={{ display: 'contents' }}>
                {i > 0 && <span className="kcap plus">+</span>}
                <span className="kcap" style={{ ['--d' as string]: i * 70 + 'ms' }}>
                  {k}
                </span>
              </span>
            ))}
          </div>
          <button className="btn" onClick={p.onChange}>
            更改热键
          </button>
        </div>
      ) : (
        <div>
          <div className="bigbox">
            {p.capturedKeys == null ? (
              <>
                <span className="pdot" style={{ animationDelay: '0ms' }} />
                <span className="pdot" style={{ animationDelay: '160ms' }} />
                <span className="pdot" style={{ animationDelay: '320ms' }} />
              </>
            ) : (
              p.capturedKeys.map((k, i) => (
                <span key={i} style={{ display: 'contents' }}>
                  {i > 0 && <span className="kcap plus">+</span>}
                  <span className="kcap" style={{ ['--d' as string]: i * 80 + 'ms' }}>
                    {k}
                  </span>
                </span>
              ))
            )}
          </div>
          <div className="capnote">
            按 <b>Esc</b> 取消捕获
          </div>
          <div
            className="acts"
            style={{ visibility: p.capturedKeys != null ? 'visible' : 'hidden' }}
          >
            <button className="btn pri" onClick={p.onApply}>
              应用热键
            </button>
            <button className="btn" onClick={p.onRecap}>
              重新捕获
            </button>
            <button className="btn" onClick={p.onCancel}>
              取消
            </button>
          </div>
        </div>
      )}
    </>
  )
}
