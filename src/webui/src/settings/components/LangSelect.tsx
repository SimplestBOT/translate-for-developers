// LangSelect - 语言下拉（常用置顶分组 + 搜索 + ✓选中态）
// 受控组件；为遗留项「设置集中到主窗口」预留（可平移进 Popover）
import { useEffect, useMemo, useRef, useState } from 'react'
import type { LangInfo } from '../../bridge/types'

interface Props {
  label: string
  value: string
  langs: LangInfo[]
  allowAuto: boolean
  onChange: (id: string) => void
}

export default function LangSelect(p: Props) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const rootRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    const common: LangInfo[] = []
    const rest: LangInfo[] = []
    for (const L of p.langs) {
      if (!p.allowAuto && L.id === 'auto') continue
      const hit = !q || L.name.toLowerCase().includes(q) || L.id.toLowerCase().includes(q)
      if (!hit) continue
      ;(L.common ? common : rest).push(L)
    }
    return { common, rest }
  }, [p.langs, p.allowAuto, query])

  useEffect(() => {
    if (open) {
      setQuery('')
      setTimeout(() => inputRef.current?.focus(), 30)
    }
  }, [open])

  useEffect(() => {
    if (!open) return
    const h = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', h, true)
    return () => document.removeEventListener('mousedown', h, true)
  }, [open])

  const nameOf = (id: string) => p.langs.find((L) => L.id === id)?.name ?? id
  const cur = p.value

  const renderItem = (L: LangInfo) => (
    <div
      key={L.id}
      className={'dditem' + (L.id === cur ? ' on' : '')}
      onClick={() => {
        setOpen(false)
        p.onChange(L.id)
      }}
    >
      <span>{L.name}</span>
      <span className="code">{L.id}</span>
    </div>
  )

  return (
    <div className="lrow">
      <span className="lname">{p.label}</span>
      <div className="lsel" ref={rootRef}>
        <button className="sel" onClick={() => setOpen(!open)}>
          <span>{nameOf(cur)}</span>
          <span className="caret">▼</span>
        </button>
        {open && (
          <div className="dd">
            <div className="ddsearch">
              <input
                ref={inputRef}
                placeholder="搜索语言…"
                spellCheck={false}
                value={query}
                onChange={(e) => setQuery(e.target.value)}
              />
            </div>
            <div className="ddlist">
              {filtered.common.length > 0 && <div className="ddgroup">常用</div>}
              {filtered.common.map(renderItem)}
              {filtered.common.length > 0 && filtered.rest.length > 0 && (
                <div className="ddgroup">全部</div>
              )}
              {filtered.rest.map(renderItem)}
              {filtered.common.length === 0 && filtered.rest.length === 0 && (
                <div className="ddempty">没有匹配的语言</div>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
