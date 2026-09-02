//=============================================================
// settings/App.tsx - 设置页编排（阶段 4a React 版）
// 只做消息编排与状态；业务仍在 AHK（热键捕获流）与 C#（配置写方）。
// 消息契约 = docs/protocol.md；与 legacy scripts/html/settings.html 逐项对齐。
// 组件均为受控件（LangSelect/ProviderPicker/BaiduKeysCard/HotkeyCard），
// 为遗留项「设置集中到翻译主窗口（组件/Popover）」预留复用结构。
//=============================================================
import { useEffect, useState } from 'react'
import { on, post } from '../bridge/protocol'
import type {
  BaiduSaved,
  ErrorFrame,
  HotkeyPayload,
  LangInfo,
  ProviderId,
  ProviderUpdated,
  SettingsInit,
} from '../bridge/types'
import HotkeyCard from './components/HotkeyCard'
import LangSelect from './components/LangSelect'
import ProviderPicker from './components/ProviderPicker'
import BaiduKeysCard from './components/BaiduKeysCard'

interface State {
  inited: boolean
  ndrag: boolean
  langs: LangInfo[]
  src: string
  tgt: string
  provider: ProviderId
  hasKeys: boolean
  appid: string
  secret: string
  hotkeyKeys: string[]
  capturing: boolean
  capturedKeys: string[] | null
  hkState: string
  hint: string
  keysPulse: number
  saveDone: number
}

const INITIAL: State = {
  inited: false,
  ndrag: true,
  langs: [],
  src: 'auto',
  tgt: 'zh-CN',
  provider: 'mymemory',
  hasKeys: false,
  appid: '',
  secret: '',
  hotkeyKeys: [],
  capturing: false,
  capturedKeys: null,
  hkState: '',
  hint: '',
  keysPulse: 0,
  saveDone: 0,
}

export default function App() {
  const [st, setSt] = useState<State>(INITIAL)
  const patch = (p: Partial<State>) => setSt((s) => ({ ...s, ...p }))

  // 宿主→页面消息订阅（挂载一次）
  useEffect(() => {
    // 注意顺序：必须先完成全部订阅再发 ready——宿主收到 ready 会立即回 push init，
    // 若订阅未注册消息会被静默丢弃（无 handler 的消息按协议直接忽略）
    const offs = [
      on<SettingsInit>('init', (d) => {
        ;(window as unknown as { __ndrag?: boolean }).__ndrag = !!d.ndrag
        patch({
          inited: true,
          ndrag: !!d.ndrag,
          langs: d.langs ?? [],
          src: d.src,
          tgt: d.tgt,
          provider: d.provider,
          hasKeys: !!d.hasKeys,
          appid: d.appid || '',
          secret: d.secret || '',
          hotkeyKeys: d.hotkeyKeys ?? [],
          capturing: false,
          capturedKeys: null,
          hkState: '',
          hint: '',
        })
        // 渲染完成上报（自动化断言锚点；协议规则：未知类型收端静默忽略）
        setTimeout(() => post('ui_event', 'settings-rendered'), 0)
      }),
      on('capturing', () =>
        patch({ capturing: true, capturedKeys: null, hkState: '捕获中…' }),
      ),
      on<HotkeyPayload>('captured', (d) =>
        patch({ capturing: false, capturedKeys: d.keys, hkState: '已捕获' }),
      ),
      on('captureCancelled', () =>
        patch({ capturing: false, capturedKeys: null, hkState: '' }),
      ),
      on<HotkeyPayload>('hotkeyUpdated', (d) =>
        patch({
          capturing: false,
          capturedKeys: null,
          hotkeyKeys: d.keys,
          hkState: '已更新',
        }),
      ),
      on<ProviderUpdated>('providerUpdated', (d) => patch({ provider: d.provider })),
      on<BaiduSaved>('baiduSaved', () =>
        patch({ hasKeys: true, saveDone: Date.now() }),
      ),
      on<ErrorFrame>('error', (d) => {
        if (d.code === 'no_baidu_keys') {
          patch({ hint: d.message, keysPulse: Date.now() })
        } else if (d.code === 'hotkey_invalid') {
          patch({ hint: '', capturing: false, hotkeyKeys: [d.message] })
        } else if (d.code === 'hotkey_busy') {
          patch({ capturing: false, hkState: '捕获中(其他窗口)' })
        } else {
          patch({ hint: d.message })
        }
      }),
    ]
    return () => offs.forEach((off) => off())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 订阅就绪后请求 init（与 legacy settings.html 的时序一致）
  useEffect(() => {
    post('ready')
  }, [])

  // Esc 语义：捕获中=取消捕获，否则关闭窗口（与 legacy 一致）
  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      if (st.capturing) post('cancelCapture')
      else post('close')
    }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [st.capturing])

  const startCapture = () => {
    patch({ hint: '' })
    post('captureHotkey')
  }

  return (
    <div className="wrap">
      <header
        className="rv"
        style={{ ['--d' as string]: '0ms' }}
        onMouseDown={(e) => {
          // 原生拖动不可用时退回消息拖动（协议 drag；ndrag=true 时宿主原生处理）
          if (!st.ndrag && e.button === 0 && !(e.target as HTMLElement).closest('button,input,a,.link'))
            post('drag')
        }}
      >
        <div className="logo">译</div>
        <div className="brand">translator · 设置</div>
        <div className="pair">
          <span className="chip">v1.5</span>
        </div>
        <button className="xbtn" title="关闭" onClick={() => post('close')}>
          ✕
        </button>
      </header>

      <div className="scroller">
        <section className="card rv" style={{ ['--d' as string]: '40ms' }}>
          <HotkeyCard
            keys={st.hotkeyKeys}
            capturing={st.capturing}
            capturedKeys={st.capturedKeys}
            stateText={st.hkState}
            onChange={startCapture}
            onApply={() => post('applyHotkey')}
            onRecap={() => {
              patch({ capturing: true, capturedKeys: null, hkState: '捕获中…' })
              post('captureHotkey')
            }}
            onCancel={() => post('cancelCapture')}
          />
        </section>

        <section className="card rv" style={{ ['--d' as string]: '80ms' }}>
          <div className="flabel">
            <span>翻译语言</span>
          </div>
          <LangSelect
            label="源语言"
            value={st.src}
            langs={st.langs}
            allowAuto
            onChange={(id) => {
              patch({ src: id })
              post('setLang', 'src', id)
            }}
          />
          <LangSelect
            label="目标语言"
            value={st.tgt}
            langs={st.langs}
            allowAuto={false}
            onChange={(id) => {
              patch({ tgt: id })
              post('setLang', 'tgt', id)
            }}
          />
        </section>

        <section className="card rv" style={{ ['--d' as string]: '120ms' }}>
          <div className="flabel">
            <span>翻译服务</span>
          </div>
          <ProviderPicker
            value={st.provider}
            onChange={(p) => {
              if (p === st.provider) return
              patch({ hint: '', provider: p })
              post('setProvider', p)
            }}
          />
          <div className={'phint' + (st.hint ? ' show' : '')}>{st.hint}</div>
        </section>

        <section
          className={'card rv' + (st.keysPulse ? ' pulse' : '')}
          style={{ ['--d' as string]: '160ms' }}
          key={'keys-' + st.keysPulse}
        >
          <div className="flabel">
            <span>百度翻译密钥</span>
            <b style={{ color: st.hasKeys ? 'var(--ok)' : 'var(--faint)' }}>
              {st.hasKeys ? '已配置' : '未配置'}
            </b>
          </div>
          <BaiduKeysCard
            appid={st.appid}
            secret={st.secret}
            saveDone={st.saveDone}
            onAppidChange={(v) => patch({ appid: v })}
            onSecretChange={(v) => patch({ secret: v })}
            onSave={(a, s) => post('saveBaidu', a, s)}
          />
        </section>
      </div>

      <footer className="rv" style={{ ['--d' as string]: '200ms' }}>
        <span className={'dot' + (st.provider === 'baidu' ? ' b' : '')} />
        <span>{st.provider === 'baidu' ? '百度翻译' : 'MyMemory'}</span>
        <span className="r">更改即时生效 · Esc 关闭</span>
      </footer>
    </div>
  )
}
