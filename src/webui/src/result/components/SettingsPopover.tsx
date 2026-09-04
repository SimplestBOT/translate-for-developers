//=============================================================
// result/components/SettingsPopover.tsx - 主窗口设置 Popover（遗留项落地）
// 复用 settings 页三组件（LangSelect/ProviderPicker/HotkeyCard）与同一批
// 消息（setLang/setProvider/captureHotkey/applyHotkey/cancelCapture）；
// result 窗是宿主自开窗，PageBusiness self 分支直接承接这些消息。
// error 帧码区分：hotkey_*/no_baidu_keys/save_failed/no_capture 归本组件
// 提示，translate_failed 归翻译错误卡（App.tsx 过滤）。AHK 推的 init 无
// langs 字段，App 据此不挂载本组件（AHK 双轨行为不变）。
//=============================================================
import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { on, post } from '../../bridge/protocol'
import type {
  ErrorFrame,
  HotkeyPayload,
  LangInfo,
  ProviderId,
  ProviderUpdated,
} from '../../bridge/types'
import LangSelect from '../../settings/components/LangSelect'
import ProviderPicker from '../../settings/components/ProviderPicker'
import HotkeyCard from '../../settings/components/HotkeyCard'

interface PopoverInit {
  langs?: LangInfo[]
  src?: string
  tgt?: string
  providerKey?: string
  hotkeyKeys?: string[]
}

interface Props {
  init: PopoverInit | null
  /** 更新 App 的 init 展示字段（头部语言 chip / 提供商行） */
  onPatch: (p: { srcLangLabel?: string; tgtLangLabel?: string; provider?: string; providerKey?: string }) => void
  /** 变更生效后重译当前文本（setLang/setProvider 落盘后补一发 translate） */
  onRetranslate: () => void
}

const langLabel = (id: string) => (id === 'auto' ? 'AUTO' : id.toUpperCase())
const providerName = (p: ProviderId) => (p === 'baidu' ? '百度翻译' : p === 'deepl' ? 'DeepL' : p === 'llm' ? 'AI 大模型' : 'MyMemory')

export default function SettingsPopover(p: Props) {
  const [open, setOpen] = useState(false)
  // 局部覆盖态：init 只在窗口创建时推一次，用户改动后以本地为准
  const [srcOv, setSrcOv] = useState<string | null>(null)
  const [tgtOv, setTgtOv] = useState<string | null>(null)
  const [provider, setProvider] = useState<ProviderId | null>(null)
  const [hotkeyKeys, setHotkeyKeys] = useState<string[] | null>(null)
  const [capturing, setCapturing] = useState(false)
  const [capturedKeys, setCapturedKeys] = useState<string[] | null>(null)
  const [hkState, setHkState] = useState('')
  const [hint, setHint] = useState('')
  const rootRef = useRef<HTMLDivElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const capturingRef = useRef(false)
  capturingRef.current = capturing

  const src = srcOv ?? p.init?.src ?? 'auto'
  const tgt = tgtOv ?? p.init?.tgt ?? 'zh-CN'
  const prov = provider ?? ((['baidu','deepl','llm'].includes(p.init?.providerKey ?? '') ? p.init?.providerKey : 'mymemory') as ProviderId)
  const keys = hotkeyKeys ?? p.init?.hotkeyKeys ?? []
  const langs = p.init?.langs ?? []

  // 宿主→页面消息订阅（与 settings 页同款；挂载一次）
  useEffect(() => {
    const offs = [
      on('capturing', () => {
        setCapturing(true)
        setCapturedKeys(null)
        setHkState('捕获中…')
      }),
      on<HotkeyPayload>('captured', (d) => {
        setCapturing(false)
        setCapturedKeys(d.keys)
        setHkState('已捕获')
      }),
      on('captureCancelled', () => {
        setCapturing(false)
        setCapturedKeys(null)
        setHkState('')
      }),
      on<HotkeyPayload>('hotkeyUpdated', (d) => {
        setCapturing(false)
        setCapturedKeys(null)
        setHotkeyKeys(d.keys)
        setHkState('已更新')
      }),
      on<ProviderUpdated>('providerUpdated', (d) => {
        setProvider(d.provider)
        p.onPatch({ provider: providerName(d.provider), providerKey: d.provider })
        p.onRetranslate()
      }),
      on<ErrorFrame>('error', (d) => {
        // 只接设置类错误；translate_failed 由 App 归入翻译错误卡
        if (d.code === 'no_baidu_keys') setHint(d.message)
        else if (d.code === 'provider_not_ready') setHint(d.message)
        else if (d.code === 'hotkey_invalid') {
          setCapturing(false)
          setCapturedKeys(null)
          setHotkeyKeys([d.message])
        } else if (d.code === 'hotkey_busy') {
          setCapturing(false)
          setHkState('捕获中(其他窗口)')
        } else if (d.code === 'no_capture') {
          setCapturing(false)
          setHkState('')
        } else if (d.code === 'save_failed') setHint(d.message)
      }),
    ]
    return () => offs.forEach((off) => off())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 面板打开时：Esc=取消捕获（保持面板）/ 关面板；捕获阶段监听先于 App 的
  // bubble 监听，stopPropagation 阻止 App 的「Esc 关窗」
  useEffect(() => {
    if (!open) return
    const h = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      e.stopImmediatePropagation()
      if (capturingRef.current) post('cancelCapture')
      else setOpen(false)
    }
    window.addEventListener('keydown', h, true)
    return () => window.removeEventListener('keydown', h, true)
  }, [open])

  // 面板打开时点外部关闭（含 portal 到 body 的面板本体）；关闭时若捕获未结束则一并取消
  useEffect(() => {
    if (!open) return
    const h = (e: MouseEvent) => {
      const t = e.target as Node
      if (!rootRef.current?.contains(t) && !panelRef.current?.contains(t)) setOpen(false)
    }
    document.addEventListener('mousedown', h, true)
    return () => document.removeEventListener('mousedown', h, true)
  }, [open])
  useEffect(() => {
    if (open || !capturing) return
    post('cancelCapture')
    setCapturing(false)
    setCapturedKeys(null)
    setHkState('')
  }, [open, capturing])

  const changeLang = (which: 'src' | 'tgt', id: string) => {
    if (which === 'src') setSrcOv(id)
    else setTgtOv(id)
    post('setLang', which, id)
    p.onPatch(
      which === 'src'
        ? { srcLangLabel: langLabel(id) }
        : { tgtLangLabel: langLabel(id) },
    )
    p.onRetranslate()
  }

  return (
    <div className="spov-root" ref={rootRef}>
      <button
        className={'xbtn gear' + (open ? ' on' : '')}
        title="语言 / 提供商 / 热键"
        onClick={() => setOpen(!open)}
      >
        ⚙
      </button>
      {/* portal 到 body：header 的 .rv 入场动画带 transform，动画期间形成层叠
          上下文，面板若留在 header 内会被后绘制的正文卡片盖住（实测复现） */}
      {open &&
        createPortal(
          <div className="spov" ref={panelRef}>
            <div className="flabel">
              <span>语言</span>
            </div>
            <LangSelect
              label="翻译自"
              value={src}
              langs={langs}
              allowAuto
              onChange={(id) => changeLang('src', id)}
            />
            <LangSelect
              label="翻译成"
              value={tgt}
              langs={langs}
              allowAuto={false}
              onChange={(id) => changeLang('tgt', id)}
            />

            <div className="flabel spov-gap">
              <span>提供商</span>
            </div>
            <ProviderPicker
              value={prov}
              onChange={(id) => {
                setHint('')
                post('setProvider', id)
              }}
            />

            <div className="flabel spov-gap">
              <span>热键</span>
            </div>
            <HotkeyCard
              keys={keys}
              capturing={capturing}
              capturedKeys={capturedKeys}
              stateText={hkState}
              onChange={() => {
                setHint('')
                post('captureHotkey')
              }}
              onApply={() => post('applyHotkey')}
              onRecap={() => post('captureHotkey')}
              onCancel={() => post('cancelCapture')}
            />

            {hint && <div className="spov-hint">{hint}</div>}
          </div>,
          document.body,
        )}
    </div>
  )
}
