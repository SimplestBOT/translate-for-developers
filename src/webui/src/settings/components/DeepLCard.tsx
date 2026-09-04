// DeepLCard - DeepL API Key 表单（Key + 可选 Pro 端点；样式复用 Baidu 表单类）
import { useEffect, useRef, useState } from 'react'

interface Props {
  apiKey: string
  endpoint: string
  saveDone: number
  onKeyChange: (v: string) => void
  onEndpointChange: (v: string) => void
  onSave: (key: string, endpoint: string) => void
}

export default function DeepLCard(p: Props) {
  const [showKey, setShowKey] = useState(false)
  const [saved, setSaved] = useState(false)
  const [badKey, setBadKey] = useState(false)
  const keyRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!p.saveDone) return
    setSaved(true)
    const t = setTimeout(() => setSaved(false), 1400)
    return () => clearTimeout(t)
  }, [p.saveDone])

  const submit = () => {
    if (!p.apiKey.trim()) {
      setBadKey(true)
      setTimeout(() => setBadKey(false), 600)
      keyRef.current?.focus()
      return
    }
    p.onSave(p.apiKey.trim(), p.endpoint.trim())
  }

  return (
    <>
      <div className="field">
        <label>API Key</label>
        <div className="inrow">
          <input
            ref={keyRef}
            className={badKey ? 'bad' : ''}
            type={showKey ? 'text' : 'password'}
            placeholder="在 DeepL 账户页复制（免费版以 :fx 结尾）"
            spellCheck={false}
            autoComplete="off"
            value={p.apiKey}
            onChange={(e) => p.onKeyChange(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
          <button className="eye" type="button" onClick={() => setShowKey(!showKey)}>
            {showKey ? '隐藏' : '显示'}
          </button>
        </div>
      </div>
      <div className="field">
        <label>API 端点（可选，Pro 用户填写）</label>
        <div className="inrow">
          <input
            placeholder="默认免费端点；Pro 填 api.deepl.com"
            spellCheck={false}
            autoComplete="off"
            value={p.endpoint}
            onChange={(e) => p.onEndpointChange(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
        </div>
      </div>
      <div className="note">
        <span>🔒</span>
        <span>Key 经 Windows DPAPI 加密保存，仅本机可解</span>
      </div>
      <div className="kfoot">
        <button className={'btn pri' + (saved ? ' done' : '')} onClick={submit}>
          {saved ? '✓ 已保存' : '保存并启用'}
        </button>
      </div>
    </>
  )
}
