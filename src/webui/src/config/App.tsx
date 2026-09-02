//=============================================================
// config/App.tsx - 百度密钥配置页编排（4d React 版）
// 行为与 legacy scripts/html/config.html 逐项对齐：
//   init 回填 appid/secret · openurl 仅允许百度域名（AHK 侧校验）
//   保存空值→抖动聚焦；save_failed 错误帧→双输入抖动 · Enter 提交
//   保存成功由 AHK 直接关窗（无回帧，与 legacy 一致）
// 消息契约 = docs/protocol.md §2（saveBaidu/openurl）
//=============================================================
import { useEffect, useRef, useState } from 'react'
import { on, post } from '../bridge/protocol'
import type { ConfigInit, ErrorFrame } from '../bridge/types'

export default function App() {
  const [ndrag, setNdrag] = useState(false)
  const [appid, setAppid] = useState('')
  const [secret, setSecret] = useState('')
  const [showSecret, setShowSecret] = useState(false)
  const [badAppid, setBadAppid] = useState(false)
  const [badSecret, setBadSecret] = useState(false)
  const appidRef = useRef<HTMLInputElement>(null)
  const secretRef = useRef<HTMLInputElement>(null)

  // 宿主→页面消息订阅（先订阅后 ready，时序原因见 4a）
  useEffect(() => {
    const offs = [
      on<ConfigInit>('init', (d) => {
        ;(window as unknown as { __ndrag?: boolean }).__ndrag = !!d.ndrag
        setNdrag(!!d.ndrag)
        setAppid(d.appid || '')
        setSecret(d.secret || '')
        setTimeout(() => post('ui_event', 'config-rendered'), 0)
      }),
      on<ErrorFrame>('error', (d) => {
        if (d.code !== 'save_failed') return
        bad(setBadAppid, appidRef)
        bad(setBadSecret, secretRef)
      }),
    ]
    return () => offs.forEach((off) => off())
  }, [])

  useEffect(() => {
    post('ready')
  }, [])

  const bad = (set: (v: boolean) => void, ref: React.RefObject<HTMLInputElement | null>) => {
    set(true)
    setTimeout(() => set(false), 600)
    ref.current?.focus()
  }

  const submit = () => {
    const a = appid.trim()
    const s = secret.trim()
    if (!a) {
      bad(setBadAppid, appidRef)
      return
    }
    if (!s) {
      bad(setBadSecret, secretRef)
      return
    }
    post('saveBaidu', a, s)
  }

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
          <span className="chip">百度翻译 · 标准版</span>
        </div>
        <button className="xbtn" title="关闭" onClick={() => post('close')}>
          ✕
        </button>
      </header>

      <section className="card rv" style={{ ['--d' as string]: '50ms' }}>
        <div className="flabel">
          <span>开通步骤 · 标准版免费</span>
          <span
            className="link"
            onClick={() => post('openurl', 'https://fanyi-api.baidu.com/choose')}
          >
            打开 fanyi-api.baidu.com ↗
          </span>
        </div>
        <ol className="steps">
          <li>
            <span className="snum">1</span>
            <span>
              登录百度翻译开放平台，进入控制台，按提示完成<b>开发者认证</b>（实名）
            </span>
          </li>
          <li>
            <span className="snum">2</span>
            <span>
              产品服务 → 通用文本翻译 → <b>立即开通</b>（选择标准版，免费）
            </span>
          </li>
          <li>
            <span className="snum">3</span>
            <span>在「我的服务 → 通用文本翻译」的服务信息中复制 APP ID 与密钥</span>
          </li>
        </ol>
      </section>

      <section className="card rv" style={{ ['--d' as string]: '90ms' }}>
        <div className="field">
          <label>APP ID</label>
          <div className="inrow">
            <input
              ref={appidRef}
              className={badAppid ? 'bad' : ''}
              placeholder="例如：20250102000000000"
              spellCheck={false}
              autoComplete="off"
              value={appid}
              onChange={(e) => setAppid(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && submit()}
            />
          </div>
        </div>
        <div className="field">
          <label>密钥 Key</label>
          <div className="inrow">
            <input
              ref={secretRef}
              id="secret"
              className={badSecret ? 'bad' : ''}
              type={showSecret ? 'text' : 'password'}
              placeholder="在服务信息页复制"
              spellCheck={false}
              autoComplete="off"
              value={secret}
              onChange={(e) => setSecret(e.target.value)}
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
      </section>

      <div className="cfoot rv" style={{ ['--d' as string]: '130ms' }}>
        <button className="btn" onClick={() => post('close')}>
          稍后再说
        </button>
        <button className="btn pri" onClick={submit}>
          保存并启用
        </button>
      </div>
    </div>
  )
}
