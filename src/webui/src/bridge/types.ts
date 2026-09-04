// bridge/types.ts - 协议载荷类型（与 docs/protocol.md §3 对齐）

export interface LangInfo {
  id: string
  name: string
  auto?: boolean
  common?: boolean
}

export type ProviderId = 'mymemory' | 'baidu' | 'deepl' | 'llm'

/** settings 页 init 载荷（优化 4：新增 deepl/llm 配置字段） */
export interface SettingsInit {
  hotkey: string
  hotkeyKeys: string[]
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
  ndrag: boolean
  langs: LangInfo[]
}

/** captured / hotkeyUpdated 载荷 */
export interface HotkeyPayload {
  hk: string
  keys: string[]
}

/** 统一错误帧载荷 */
export interface ErrorFrame {
  code: string
  message: string
}

/** providerUpdated 载荷 */
export interface ProviderUpdated {
  provider: ProviderId
}

/** baiduSaved 载荷 */
export interface BaiduSaved {
  ok: boolean
}

/** deeplSaved 载荷（优化 4） */
export interface DeeplSaved {
  ok: boolean
}

/** llmSaved 载荷（优化 4） */
export interface LlmSaved {
  ok: boolean
}

/** capture 页 init 载荷（AHK CaptureMsgHandler） */
export interface CaptureInit {
  cur: string
  ndrag: boolean
}

/** config 页 init 载荷（AHK ConfigMsgHandler） */
export interface ConfigInit {
  appid: string
  secret: string
  ndrag: boolean
}
