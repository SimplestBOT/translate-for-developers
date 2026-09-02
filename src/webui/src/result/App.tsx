//=============================================================
// result/App.tsx - 翻译结果页编排（4b React 版）
// 行为与 legacy scripts/html/result.html 逐项对齐：
//   init → 渲染原文/语言 chip/提供商 → loading → post('translate')
//   result → 译文展示（elapsedMs 徽标）· error → 错误卡 + 重试
//   复制按钮 ×2（✓ 已复制反馈）· Esc 关闭 · Enter 复制译文 · Ctrl+C 复制
// 数据全部来自协议帧；翻译由宿主 C# Core 完成（页面永不接触 Provider JSON）
//=============================================================
import { useCallback, useEffect, useRef, useState } from 'react'
import { on, post } from '../bridge/protocol'
import type { ErrorFrame, LangInfo } from '../bridge/types'
import SrcCard from './components/SrcCard'
import DstCard from './components/DstCard'
import ErrorCard from './components/ErrorCard'
import SettingsPopover from './components/SettingsPopover'

/** result 页 init 载荷（protocol.md §3；testError 为 4a 起调试字段）。
 *  5d 起宿主自开窗追发设置字段（hotkey/langs/src/tgt/hasKeys）——主窗口
 *  设置 Popover 用；AHK 推的 init 缺省这些字段，SettingsPopover 据此
 *  降级不挂载，AHK 双轨行为不变。 */
export interface ResultInit {
  srcText: string
  srcLangLabel: string
  tgtLangLabel: string
  provider: string
  providerKey: string
  ndrag: boolean
  testError?: boolean
  // ---- 5d 设置集中化扩展字段（AHK 双轨 init 无）----
  hotkey?: string
  hotkeyKeys?: string[]
  langs?: LangInfo[]
  src?: string
  tgt?: string
  hasKeys?: boolean
}

/** 协议 §4 统一模型（宿主 C# Core 产出） */
export interface TranslationResult {
  sourceText: string
  translatedText: string
  sourceLanguage: string
  targetLanguage: string
  provider: string
  elapsedMs: number
}

type Phase = 'loading' | 'done' | 'error'

interface State {
  inited: boolean
  ndrag: boolean
  init: ResultInit | null
  phase: Phase
  result: TranslationResult | null
  errMsg: string
}

const INITIAL: State = {
  inited: false,
  ndrag: false,
  init: null,
  phase: 'loading',
  result: null,
  errMsg: '',
}

export default function App() {
  const [st, setSt] = useState<State>(INITIAL)
  const patch = (p: Partial<State>) => setSt((s) => ({ ...s, ...p }))
  const t0Ref = useRef(0)

  // 宿主→页面消息订阅（先订阅后 ready，时序原因见 4a）。
  // error 帧码区分：translate_failed → 翻译错误卡；其余（hotkey_*/no_baidu_keys/
  // save_failed/no_capture）→ 设置 Popover 自己的订阅提示（App 不展示）。
  useEffect(() => {
    const offs = [
      on<ResultInit>('init', (d) => {
        ;(window as unknown as { __ndrag?: boolean }).__ndrag = !!d.ndrag
        t0Ref.current = performance.now()
        patch({
          inited: true,
          ndrag: !!d.ndrag,
          init: d,
          phase: 'loading',
          result: null,
          errMsg: '',
        })
        post('translate')
        setTimeout(() => post('ui_event', 'result-rendered'), 0)
      }),
      on<TranslationResult>('result', (d) =>
        patch({ phase: 'done', result: d }),
      ),
      on<ErrorFrame>('error', (d) => {
        if (d.code && d.code !== 'translate_failed') return // 设置类错误归 Popover
        patch({ phase: 'error', errMsg: d.message || '未知错误' })
      }),
    ]
    return () => offs.forEach((off) => off())
  }, [])

  // 订阅就绪后请求 init（init → translate 由上面 init handler 触发）
  useEffect(() => {
    post('ready')
  }, [])

  // Esc 关闭（result 页无捕获态，直接关）+ Ctrl+C / Enter 快捷复制
  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        post('close')
        return
      }
      const dst = st.result?.translatedText
      if (!dst) return
      if ((e.ctrlKey || e.metaKey) && e.key === 'c' && !String(window.getSelection()).length)
        post('copy', dst)
      if (e.key === 'Enter' && !e.ctrlKey && !e.metaKey) post('copy', dst)
    }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [st.result])

  // 重译当前文本（错误卡「重试」与设置 Popover 的语言/提供商变更共用）。
  // useCallback + 函数式 setState：Popover 在挂载期订阅里引用，避免旧闭包。
  const retranslate = useCallback(() => {
    t0Ref.current = performance.now()
    setSt((s) =>
      s.init ? { ...s, phase: 'loading', result: null, errMsg: '' } : s,
    )
    post('translate')
  }, [])

  // Popover 更新 init 展示字段（语言 chip 标签 / 提供商行）
  const patchInit = useCallback((p: Partial<ResultInit>) => {
    setSt((s) => (s.init ? { ...s, init: { ...s.init, ...p } } : s))
  }, [])

  const init = st.init
  // 设置入口：仅在宿主自开窗模式（init 携带 langs）渲染；AHK 双轨无此入口
  const canManage = (init?.langs?.length ?? 0) > 0
  return (
    <div className="wrap">
      <header
        className="rv"
        style={{ ['--d' as string]: '0ms' }}
        onMouseDown={(e) => {
          if (!st.ndrag && e.button === 0 && !(e.target as HTMLElement).closest('button,input,a,.link'))
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
          <SettingsPopover
            init={init}
            onPatch={patchInit}
            onRetranslate={retranslate}
          />
        )}
        <button className="xbtn" title="关闭" onClick={() => post('close')}>
          ✕
        </button>
      </header>

      {init && (
        <>
          <SrcCard text={init.srcText} langLabel={init.srcLangLabel} />

          <div className={'rail rv' + (st.phase === 'loading' ? ' loading' : '')} style={{ ['--d' as string]: '90ms' }}>
            <div className="track">
              <div className="beam" />
            </div>
            <span id="rlab">
              {st.phase === 'loading'
                ? '翻译中'
                : st.phase === 'error'
                  ? '失败'
                  : `完成 · ${st.result?.elapsedMs ?? Math.round(performance.now() - t0Ref.current)} ms`}
            </span>
          </div>

          {st.phase !== 'error' ? (
            <DstCard
              langLabel={init.tgtLangLabel}
              phase={st.phase}
              text={st.result?.translatedText ?? ''}
            />
          ) : (
            <ErrorCard message={st.errMsg} onRetry={retranslate} />
          )}

          <footer className="rv" style={{ ['--d' as string]: '170ms' }}>
            <span className="prov">
              <span className={'dot' + (init.providerKey === 'baidu' ? ' b' : '')} />
              <span>{init.provider}</span>
            </span>
            <span className="hint">Esc 关闭 · Enter 复制</span>
            <button
              className="btn"
              disabled={st.phase !== 'done'}
              onClick={() => st.init && post('copy', st.init.srcText)}
            >
              复制原文
            </button>
            <CopyDstButton text={st.result?.translatedText ?? ''} disabled={st.phase !== 'done'} />
          </footer>
        </>
      )}
    </div>
  )
}

/** 复制译文按钮（✓ 已复制 1.3s 反馈，对齐 legacy copyText） */
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
    <button
      className={'btn pri' + (done ? ' done' : '')}
      disabled={disabled}
      onClick={copy}
    >
      {done ? '✓ 已复制' : '复制译文'}
    </button>
  )
}
