// ErrorCard - 错误卡（! 图标 + 消息 + 重试/关闭）
import { post } from '../../bridge/protocol'

interface Props {
  message: string
  onRetry: () => void
}

export default function ErrorCard(p: Props) {
  return (
    <section className="errcard">
      <div className="eico">!</div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div className="etitle">翻译失败</div>
        <div className="emsg">{p.message}</div>
        <div className="erow">
          <button className="btn pri" onClick={p.onRetry}>
            重试
          </button>
          <button className="btn" onClick={() => post('close')}>
            关闭
          </button>
        </div>
      </div>
    </section>
  )
}
