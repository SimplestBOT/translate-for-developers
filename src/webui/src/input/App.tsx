//=============================================================
// input/App.tsx - 输入翻译页编排（优化 6，2026-09-05）
// 热键/托盘唤起 → 多行输入（Enter 翻译 · Shift+Enter 换行）→ post('translate',
// 文本)（协议扩展：payload 直接带输入内容）→ result/error 帧渲染译文。
// 复用 result 页组件：DstCard/ErrorCard/SettingsPopover（type-only import，
// 不增加包体）+ result.css 变量与卡片样式；init 与 result 页同构（Popover
// 复用 settings 组件）+ preText（--open input,文本 预填并自动翻译——自动化
// 验证路径；热键唤起为空串）。
// 窗口行为：Esc 关窗；已有 input 窗重复唤起 → 宿主激活窗口，页面监听
// window focus 重新聚焦输入框。
//=============================================================
import { useCallback, useEffect, useRef, useState } from 'react'
import { on, post } from '../bridge/protocol'
import type { ErrorFrame, LangInfo } from '../bridge/types'
import DstCard from '../result/components/DstCard'
import ErrorCard from '../result/components/ErrorCard'
import SettingsPopover from '../result/components/SettingsPopover'
import type { TranslationResult } from '../result/App'

/** input 页 init 载荷：与 result 页同构（Popover 需要同一份字段）+ preText */
export interface InputInit {
  srcLangLabel: string
  tgtLangLabel: string
  provider: string
  providerKey: string
  ndrag: boolean
  preText?: string
  // ---- 设置集中化扩展字段（SettingsPopover 复用） ----
  hotkey?: string
  hotkeyKeys?: string[]
  langs?: LangInfo[]
  src?: string
  tgt?: string
  hasKeys?: boolean
}

type Phase = 'idle' | 'loading' | 'done' | 'error'

interface State {
  inited: boolean
  ndrag: boolean
  init: InputInit | null
  text: string
  phase: Phase
  result: TranslationResult | null
  errMsg: string
}

const INITIAL: State = {
  inited: false,
  ndrag: false,
  init: null,
  text: '',
  phase: 'idle',
  result: null,
  errMsg: '',
}

export default function App() {
  const [st, setSt] = useState<State>(INITIAL)
  const patch = (p: Partial<State>) => setSt((s) => ({ ...s, ...p }))
  const t0Ref = useRef(0)
  const taRef = useRef<HTMLTextAreaElement>(null)
  // doTranslate/retranslate 的最新输入文本（Popover 挂载期订阅里避免旧闭包）
  const textRef = useRef('')
  textRef.current = st.text

  const doTranslate = useCallback((override?: string) => {
    const t = (override ?? textRef.current ?? '').trim()
    if (!t) return
    t0Ref.current = performance.now()
    setSt((s) => ({ ...s, text: override ?? s.text, phase: 'loading', result: null, errMsg: '' }))
    post('translate', t) // 协议扩展：payload 直接带文本（protocol.md 优化 6 注记）
  }, [])

  // 宿主→页面消息订阅（先订阅后 ready，时序原因见 4a）
  useEffect(() => {
    const offs = [
      on<InputInit>('init', (d) => {
        ;(window as unknown as { __ndrag?: boolean }).__ndrag = !!d.ndrag
        patch({
          inited: true,
          ndrag: !!d.ndrag,
          init: d,
          text: d.preText ?? '',
          phase: 'idle',
          result: null,
          errMsg: '',
        })
        setTimeout(() => {
          taRef.current?.focus()
          post('ui_event', 'input-rendered')
        }, 0)
        // 预填文本（--open input,文本）：自动翻译一次（全链路自动化验证路径）
        if (d.preText) setTimeout(() => doTranslate(d.preText), 0)
      }),
      on<TranslationResult>('result', (d) => {
        patch({ phase: 'done', result: d })
        setTimeout(() => post('ui_event', 'input-result-rendered'), 0)
      }),
      on<ErrorFrame>('error', (d) => {
        if (d.code && d.code !== 'translate_failed') return // 设置类错误归 Popover
        patch({ phase: 'error', errMsg: d.message || '未知错误' })
      }),
    ]
    return () => offs.forEach((off) => off())
  }, [doTranslate])

  // 订阅就绪后请求 init
  useEffect(() => {
    post('ready')
  }, [])

  // Esc 关窗（textarea 内同样生效——keydown 冒泡到 window）
  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if (e.key === 'Escape') post('close')
    }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [])

  // 重复唤起：宿主 Activate 已有窗 → WebView2 获焦 → 重新聚焦输入框
  useEffect(() => {
    const h = () => taRef.current?.focus()
    window.addEventListener('focus', h)
    return () => window.removeEventListener('focus', h)
  }, [])

  // Popover 语言/提供商变更后用当前输入重译（onRetranslate 无参签名）
  const retranslate = useCallback(() => {
    doTranslate()
  }, [doTranslate])

  // Popover 更新 init 展示字段（语言 chip 标签 / 提供商行）
  const patchInit = useCallback((p: Partial<InputInit>) => {
    setSt((s) => (s.init ? { ...s, init: { ...s.init, ...p } } : s))
  }, [])

  const init = st.init
  const canManage = (init?.langs?.length ?? 0) > 0
  const busy = st.phase === 'loading'
  return (
    <div className="wrap">
      <header
        className="rv"
        style={{ ['--d' as string]: '0ms' }}
        onMouseDown={(e) => {
          if (!st.ndrag && e.button === 0 && !(e.target as HTMLElement).closest('button,input,a,.link,textarea'))
            post('drag')
        }}
      >
        <div className="logo">译</div>
        <div className="brand">translator</div>
        <div
          className={'pair' + (canManage ? ' act' : '')}
          title={canManage ? '点击修改语言 / 提供商 / 热键' : undefined}
        >
          <span className="chip">{init?.srcLangLabel ?? '…'}</span>
          <span className="arr">→</span>
          <span className="chip acc">{init?.tgtLangLabel ?? '…'}</span>
        </div>
        {canManage && (
          <SettingsPopover init={init} onPatch={patchInit} onRetranslate={retranslate} />
        )}
        <button className="xbtn" title="关闭" onClick={() => post('close')}>
          ✕
        </button>
      </header>

      <section className="inbox rv" style={{ ['--d' as string]: '45ms' }}>
        <textarea
          ref={taRef}
          value={st.text}
          placeholder={'输入要翻译的文本…\nEnter 翻译 · Shift+Enter 换行 · Esc 关闭'}
          spellCheck={false}
          onChange={(e) => patch({ text: e.target.value })}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey && !e.ctrlKey && !e.metaKey) {
              e.preventDefault()
              doTranslate()
            }
          }}
        />
        <div className="inbox-bar">
          <span className="cnt">{st.text.length} 字</span>
          <button className="btn pri" disabled={!st.text.trim() || busy} onClick={() => doTranslate()}>
            {busy ? '翻译中…' : '翻译'}
          </button>
        </div>
      </section>

      {st.phase !== 'idle' && init && (
        <>
          <div className={'rail rv' + (busy ? ' loading' : '')} style={{ ['--d' as string]: '90ms' }}>
            <div className="track">
              <div className="beam" />
            </div>
            <span id="rlab">
              {busy
                ? '翻译中'
                : st.phase === 'error'
                  ? '失败'
                  : `${st.result?.elapsedMs ?? 0} ms`}
            </span>
          </div>
          {st.phase !== 'error' ? (
            <DstCard
              langLabel={init.tgtLangLabel}
              phase={st.phase === 'loading' ? 'loading' : 'done'}
              text={st.result?.translatedText ?? ''}
            />
          ) : (
            <ErrorCard message={st.errMsg} onRetry={retranslate} />
          )}
        </>
      )}

      {st.phase === 'idle' && <div className="dstcard empty-hint rv" style={{ ['--d' as string]: '130ms' }}>
        译文显示在这里。查报错、写注释、起变量名——直接贴进来。
      </div>}

      <footer className="rv" style={{ ['--d' as string]: '170ms' }}>
        <span className="prov">
          <span className={'dot' + (init?.providerKey === 'baidu' ? ' b' : '')} />
          <span>{init?.provider ?? ''}</span>
        </span>
        <span className="hint">Enter 翻译 · Esc 关闭</span>
        <CopyDstButton text={st.result?.translatedText ?? ''} disabled={st.phase !== 'done'} />
      </footer>
    </div>
  )
}

/** 复制译文按钮（✓ 已复制 1.3s 反馈，对齐 result 页） */
function CopyDstButton({ text, disabled }: { text: string; disabled: boolean }) {
  const [done, setDone] = useState(false)
  const timerRef = useRef<number>(0)
  const copy = () => {
    if (!text) return
    post('copy', text)
    setDone(true)
    window.clearTimeout(timerRef.current)
    timerRef.current = window.setTimeout(() => setDone(false), 1300)
  }
  useEffect(() => () => window.clearTimeout(timerRef.current), [])
  return (
    <button className={'btn pri' + (done ? ' done' : '')} disabled={disabled} onClick={copy}>
      {done ? '✓ 已复制' : '复制译文'}
    </button>
  )
}
