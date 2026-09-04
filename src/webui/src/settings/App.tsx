//=============================================================
// settings/App.tsx - 设置页编排（阶段 4a React 版；优化 4 扩展）
// 只做消息编排与状态；业务在 C#（配置写方）。
// 消息契约 = docs/protocol.md；组件均为受控件。
// 优化 4：Provider 四选（MyMemory/百度/DeepL/AI 大模型）——密钥卡按所选
//   Provider 条件渲染；setProvider 门禁失败（provider_not_ready）引导到
//   对应配置卡（keysPulse 复用为 pulse 目标标记）。
//=============================================================
import { useEffect, useState } from 'react'
import { on, post } from '../bridge/protocol'
import type {
  BaiduSaved,
  DeeplSaved,
  ErrorFrame,
  HotkeyPayload,
  LangInfo,
  LlmSaved,
  ProviderId,
  ProviderUpdated,
  SettingsInit,
} from '../bridge/types'
import HotkeyCard from './components/HotkeyCard'
import LangSelect from './components/LangSelect'
import ProviderPicker from './components/ProviderPicker'
import BaiduKeysCard from './components/BaiduKeysCard'
import DeepLCard from './components/DeepLCard'
import LlmCard from './components/LlmCard'

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
  deeplKey: string
  deeplEndpoint: string
  llmPreset: string
  llmBaseUrl: string
  llmApiKey: string
  llmModel: string
  llmPrompt: string
  hotkeyKeys: string[]
  capturing: boolean
  capturedKeys: string[] | null
  hkState: string
  hint: string
  keysPulse: number
  deeplPulse: number
  llmPulse: number
  saveDone: number
  deeplSaveDone: number
  llmSaveDone: number
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
  deeplKey: '',
  deeplEndpoint: '',
  llmPreset: 'custom',
  llmBaseUrl: '',
  llmApiKey: '',
  llmModel: '',
  llmPrompt: '',
  hotkeyKeys: [],
  capturing: false,
  capturedKeys: null,
  hkState: '',
  hint: '',
  keysPulse: 0,
  deeplPulse: 0,
  llmPulse: 0,
  saveDone: 0,
  deeplSaveDone: 0,
  llmSaveDone: 0,
}

// Provider 显示名（页脚）
const PNAME: Record<ProviderId, string> = {
  mymemory: 'MyMemory',
  baidu: '百度翻译',
  deepl: 'DeepL',
  llm: 'AI 大模型',
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
          deeplKey: d.deeplKey || '',
          deeplEndpoint: d.deeplEndpoint || '',
          llmPreset: d.llmPreset || 'custom',
          llmBaseUrl: d.llmBaseUrl || '',
          llmApiKey: d.llmApiKey || '',
          llmModel: d.llmModel || '',
          llmPrompt: d.llmPrompt || '',
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
      on<DeeplSaved>('deeplSaved', () =>
        patch({ deeplSaveDone: Date.now() }),
      ),
      on<LlmSaved>('llmSaved', () =>
        patch({ llmSaveDone: Date.now() }),
      ),
      on<ErrorFrame>('error', (d) => {
        if (d.code === 'no_baidu_keys' || d.code === 'provider_not_ready') {
          // 门禁失败：提示 + 让对应配置卡抖动（脉冲键）
          patch({ hint: d.message })
          if (d.code === 'no_baidu_keys' || d.message.includes('百度'))
            patch({ keysPulse: Date.now() })
          else if (d.message.includes('DeepL')) patch({ deeplPulse: Date.now() })
          else if (d.message.includes('大模型')) patch({ llmPulse: Date.now() })
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
          <span className="chip">v1.6</span>
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

        {st.provider === 'baidu' && (
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
        )}

        {st.provider === 'deepl' && (
          <section
            className={'card rv' + (st.deeplPulse ? ' pulse' : '')}
            style={{ ['--d' as string]: '160ms' }}
            key={'deepl-' + st.deeplPulse}
          >
            <div className="flabel">
              <span>DeepL API</span>
              <b style={{ color: st.deeplKey ? 'var(--ok)' : 'var(--faint)' }}>
                {st.deeplKey ? '已配置' : '未配置'}
              </b>
            </div>
            <DeepLCard
              apiKey={st.deeplKey}
              endpoint={st.deeplEndpoint}
              saveDone={st.deeplSaveDone}
              onKeyChange={(v) => patch({ deeplKey: v })}
              onEndpointChange={(v) => patch({ deeplEndpoint: v })}
              onSave={(k, e) => post('saveDeepl', k, e)}
            />
          </section>
        )}

        {st.provider === 'llm' && (
          <section
            className={'card rv' + (st.llmPulse ? ' pulse' : '')}
            style={{ ['--d' as string]: '160ms' }}
            key={'llm-' + st.llmPulse}
          >
            <div className="flabel">
              <span>AI 大模型（OpenAI 兼容）</span>
              <b style={{ color: st.llmBaseUrl && st.llmModel ? 'var(--ok)' : 'var(--faint)' }}>
                {st.llmBaseUrl && st.llmModel ? '已配置' : '未配置'}
              </b>
            </div>
            <LlmCard
              preset={st.llmPreset}
              baseUrl={st.llmBaseUrl}
              apiKey={st.llmApiKey}
              model={st.llmModel}
              prompt={st.llmPrompt}
              saveDone={st.llmSaveDone}
              onPresetChange={(v) => patch({ llmPreset: v })}
              onBaseUrlChange={(v) => patch({ llmBaseUrl: v })}
              onApiKeyChange={(v) => patch({ llmApiKey: v })}
              onModelChange={(v) => patch({ llmModel: v })}
              onPromptChange={(v) => patch({ llmPrompt: v })}
              onSave={(pr, u, k, m, pt) => post('saveLlm', pr, u, k, m, pt)}
            />
          </section>
        )}
      </div>

      <footer className="rv" style={{ ['--d' as string]: '200ms' }}>
        <span className={'dot' + (st.provider === 'baidu' ? ' b' : '')} />
        <span>{PNAME[st.provider] ?? st.provider}</span>
        <span className="r">更改即时生效 · Esc 关闭</span>
      </footer>
    </div>
  )
}
