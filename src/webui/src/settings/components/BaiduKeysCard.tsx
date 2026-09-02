// BaiduKeysCard - 百度密钥表单（显示/隐藏 + 失败抖动 + 保存完成反馈）
import { useEffect, useRef, useState } from 'react'

interface Props {
  appid: string
  secret: string
  saveDone: number // >0 表示刚保存成功（触发按钮 done 态）
  onAppidChange: (v: string) => void
  onSecretChange: (v: string) => void
  onSave: (appid: string, secret: string) => void
}

export default function BaiduKeysCard(p: Props) {
  const [showSecret, setShowSecret] = useState(false)
  const [saved, setSaved] = useState(false)
  const [badAppid, setBadAppid] = useState(false)
  const [badSecret, setBadSecret] = useState(false)
  const appidRef = useRef<HTMLInputElement>(null)
  const secretRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!p.saveDone) return
    setSaved(true)
    const t = setTimeout(() => setSaved(false), 1400)
    return () => clearTimeout(t)
  }, [p.saveDone])

  const submit = () => {
    const a = p.appid.trim()
    const s = p.secret.trim()
    if (!a) {
      setBadAppid(true)
      setTimeout(() => setBadAppid(false), 600)
      appidRef.current?.focus()
      return
    }
    if (!s) {
      setBadSecret(true)
      setTimeout(() => setBadSecret(false), 600)
      secretRef.current?.focus()
      return
    }
    p.onSave(a, s)
  }

  return (
    <>
      <div className="field">
        <label>APP ID</label>
        <div className="inrow">
          <input
            ref={appidRef}
            className={badAppid ? 'bad' : ''}
            placeholder="例如：20250102000000000"
            spellCheck={false}
            autoComplete="off"
            value={p.appid}
            onChange={(e) => p.onAppidChange(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
        </div>
      </div>
      <div className="field">
        <label>密钥 Key</label>
        <div className="inrow">
          <input
            ref={secretRef}
            className={'secret-input' + (badSecret ? ' bad' : '')}
            type={showSecret ? 'text' : 'password'}
            placeholder="在百度翻译开放平台复制"
            spellCheck={false}
            autoComplete="off"
            value={p.secret}
            onChange={(e) => p.onSecretChange(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
          <button className="eye" type="button" onClick={() => setShowSecret(!showSecret)}>
            {showSecret ? '隐藏' : '显示'}
          </button>
        </div>
      </div>
      <div className="note">
        <span>🔒</span>
        <span>密钥仅保存在程序目录的 config.conf，不会上传到任何服务器</span>
      </div>
      <div className="kfoot">
        <button className={'btn pri' + (saved ? ' done' : '')} onClick={submit}>
          {saved ? '✓ 已保存' : '保存并启用'}
        </button>
      </div>
    </>
  )
}
