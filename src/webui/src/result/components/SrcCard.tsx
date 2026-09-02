// SrcCard - 原文卡（字符数徽标 + 等宽文本区）
interface Props {
  text: string
  langLabel: string
}

export default function SrcCard(p: Props) {
  return (
    <section className="card rv" style={{ ['--d' as string]: '50ms' }}>
      <div className="flabel">
        <span>
          原文 · <b>{p.langLabel}</b>
        </span>
        <span>{p.text.length} 字符</span>
      </div>
      <div className="srcbox txt">{p.text}</div>
    </section>
  )
}
