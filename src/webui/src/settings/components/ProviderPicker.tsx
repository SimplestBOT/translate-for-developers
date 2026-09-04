// ProviderPicker - 提供商单选卡片（MyMemory / 百度 / DeepL / AI 大模型）
import type { ProviderId } from '../../bridge/types'

interface Props {
  value: ProviderId
  onChange: (p: ProviderId) => void
}

const CARDS: Array<{ id: ProviderId; name: string; desc: string }> = [
  { id: 'mymemory', name: 'MyMemory', desc: '免费 · 无需注册 · 每日约 5 万字符额度' },
  { id: 'baidu', name: '百度翻译', desc: '质量更稳 · 支持长文分片 · 需免费注册' },
  { id: 'deepl', name: 'DeepL', desc: '高质量 · 支持 30+ 语言 · 有免费额度' },
  { id: 'llm', name: 'AI 大模型', desc: '最高质量 · OpenAI/DeepSeek/Kimi/智谱/Ollama' },
]

export default function ProviderPicker(p: Props) {
  return (
    <div className="provrow">
      {CARDS.map((c) => (
        <button
          key={c.id}
          className={'pcard' + (p.value === c.id ? ' on' : '')}
          onClick={() => p.onChange(c.id)}
        >
          <span className="pmark" />
          <span className="pbody">
            <b>{c.name}</b>
            <i>{c.desc}</i>
          </span>
          <span className={'ptag' + (p.value === c.id ? '' : ' hide')}>当前</span>
        </button>
      ))}
    </div>
  )
}
