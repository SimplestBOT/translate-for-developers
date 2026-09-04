// LlmCard - OpenAI-compatible 大模型配置（预设模板 + 自定义；不复制 Provider）
// 预设仅用于填充表单（Base URL/Model），落盘字段为最终事实；
// 自定义服务直接改 Base URL / Model / Key / Prompt 即可接入。
import { useEffect, useState } from 'react'

interface Props {
  preset: string
  baseUrl: string
  apiKey: string
  model: string
  prompt: string
  saveDone: number
  onPresetChange: (v: string) => void
  onBaseUrlChange: (v: string) => void
  onApiKeyChange: (v: string) => void
  onModelChange: (v: string) => void
  onPromptChange: (v: string) => void
  onSave: (preset: string, baseUrl: string, apiKey: string, model: string, prompt: string) => void
}

const PRESETS: Array<{ id: string; name: string; baseUrl: string; model: string }> = [
  { id: 'openai', name: 'OpenAI', baseUrl: 'https://api.openai.com/v1', model: 'gpt-4o-mini' },
  { id: 'deepseek', name: 'DeepSeek', baseUrl: 'https://api.deepseek.com/v1', model: 'deepseek-chat' },
  { id: 'kimi', name: 'Kimi', baseUrl: 'https://api.moonshot.cn/v1', model: 'moonshot-v1-8k' },
  { id: 'zhipu', name: '智谱', baseUrl: 'https://open.bigmodel.cn/api/paas/v4', model: 'glm-4-flash' },
  { id: 'ollama', name: 'Ollama（本地）', baseUrl: 'http://127.0.0.1:11434/v1', model: 'qwen2.5:7b' },
  { id: 'custom', name: '自定义', baseUrl: '', model: '' },
]

export default function LlmCard(p: Props) {
  const [showKey, setShowKey] = useState(false)
  const [saved, setSaved] = useState(false)
  const [badUrl, setBadUrl] = useState(false)
  const [badModel, setBadModel] = useState(false)

  useEffect(() => {
    if (!p.saveDone) return
    setSaved(true)
    const t = setTimeout(() => setSaved(false), 1400)
    return () => clearTimeout(t)
  }, [p.saveDone])

  // 预设切换 = 填充模板（Base URL / Model 预填；custom 清空让用户自填）
  const applyPreset = (id: string) => {
    p.onPresetChange(id)
    const pr = PRESETS.find((x) => x.id === id)
    if (pr) {
      p.onBaseUrlChange(pr.baseUrl)
      p.onModelChange(pr.model)
    }
  }

  const submit = () => {
    if (!p.baseUrl.trim()) {
      setBadUrl(true)
      setTimeout(() => setBadUrl(false), 600)
      return
    }
    if (!p.model.trim()) {
      setBadModel(true)
      setTimeout(() => setBadModel(false), 600)
      return
    }
    p.onSave(p.preset, p.baseUrl.trim(), p.apiKey.trim(), p.model.trim(), p.prompt.trim())
  }

  return (
    <>
      <div className="field">
        <label>服务商预设</label>
        <div className="inrow">
          <select value={p.preset} onChange={(e) => applyPreset(e.target.value)}>
            {PRESETS.map((x) => (
              <option key={x.id} value={x.id}>
                {x.name}
              </option>
            ))}
          </select>
        </div>
      </div>
      <div className="field">
        <label>Base URL</label>
        <div className="inrow">
          <input
            className={badUrl ? 'bad' : ''}
            placeholder="https://api.example.com/v1"
            spellCheck={false}
            autoComplete="off"
            value={p.baseUrl}
            onChange={(e) => p.onBaseUrlChange(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
        </div>
      </div>
      <div className="field">
        <label>模型 Model</label>
        <div className="inrow">
          <input
            className={badModel ? 'bad' : ''}
            placeholder="例如 gpt-4o-mini / deepseek-chat / qwen2.5:7b"
            spellCheck={false}
            autoComplete="off"
            value={p.model}
            onChange={(e) => p.onModelChange(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
        </div>
      </div>
      <div className="field">
        <label>API Key（本地服务可留空）</label>
        <div className="inrow">
          <input
            type={showKey ? 'text' : 'password'}
            placeholder="sk-…"
            spellCheck={false}
            autoComplete="off"
            value={p.apiKey}
            onChange={(e) => p.onApiKeyChange(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
          <button className="eye" type="button" onClick={() => setShowKey(!showKey)}>
            {showKey ? '隐藏' : '显示'}
          </button>
        </div>
      </div>
      <div className="field">
        <label>翻译 Prompt（留空用内置默认）</label>
        <div className="inrow">
          <textarea
            rows={4}
            spellCheck={false}
            placeholder="留空使用内置默认（只返回译文/保持格式/保护代码与占位符）"
            value={p.prompt}
            onChange={(e) => p.onPromptChange(e.target.value)}
          />
        </div>
      </div>
      <div className="note">
        <span>🔒</span>
        <span>Key 经 Windows DPAPI 加密保存；任何 OpenAI 兼容服务均可接入</span>
      </div>
      <div className="kfoot">
        <button className={'btn pri' + (saved ? ' done' : '')} onClick={submit}>
          {saved ? '✓ 已保存' : '保存并启用'}
        </button>
      </div>
    </>
  )
}
