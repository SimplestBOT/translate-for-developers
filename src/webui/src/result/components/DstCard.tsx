// DstCard - 译文卡：loading（shimmer 骨架）/ done（unblur 入场）
interface Props {
  langLabel: string
  phase: 'loading' | 'done'
  text: string
}

export default function DstCard(p: Props) {
  const done = p.phase === 'done'
  return (
    <section className="card rv dstcard" style={{ ['--d' as string]: '130ms' }}>
      <div className="flabel">
        <span>
          译文 · <b>{p.langLabel}</b>
        </span>
        <span />
      </div>
      {!done ? (
        <div>
          <div className="sk w90" />
          <div className="sk w72" />
          <div className="sk w54" />
        </div>
      ) : (
        <div className="dstbox txt set">{p.text}</div>
      )}
    </section>
  )
}
