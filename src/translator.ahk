#Requires AutoHotkey v2.0
#SingleInstance Force

;=============================================================
; translator - 选中文本按热键即翻译（任意软件可用）
; 翻译服务：MyMemory（免费 API，无需注册）/ 百度翻译（免费版，需注册）
; 源语言自动检测（可手动指定），目标语言 30+ 种可选
; 托盘菜单：切换提供商、配置百度密钥、更改热键、选语言，全部即时生效
;=============================================================

;---------------------- 配置区 ----------------------
gHotkey      := "^!t"      ; 默认翻译热键：^=Ctrl  !=Alt  t=T
gSourceLang  := "auto"     ; 源语言：auto=自动检测，或具体语言 ID
gTargetLang  := "zh-CN"    ; 目标语言（见下方语言表）
gApiTimeout  := 15000      ; API 超时毫秒
gProvider    := "mymemory" ; 翻译提供商：mymemory / baidu
gBaiduAppid  := ""         ; 百度翻译 APP ID（托盘菜单「配置百度翻译密钥」填入）
gBaiduSecret := ""         ; 百度翻译密钥
;-----------------------------------------------------

;=============================================================
; 语言表：ID => [显示名, 百度代码]
; ID 即 MyMemory 的 ISO 639-1 / RFC3066 代码（auto=自动检测）
;=============================================================
gLangs := Map(
    "auto",   ["自动检测", "auto"],
    "zh-CN",  ["简体中文", "zh"],
    "zh-TW",  ["繁体中文", "cht"],
    "en",     ["英语", "en"],
    "ja",     ["日语", "jp"],
    "ko",     ["韩语", "kor"],
    "fr",     ["法语", "fra"],
    "de",     ["德语", "de"],
    "es",     ["西班牙语", "spa"],
    "pt",     ["葡萄牙语", "pt"],
    "ru",     ["俄语", "ru"],
    "it",     ["意大利语", "it"],
    "ar",     ["阿拉伯语", "ara"],
    "hi",     ["印地语", "hi"],
    "th",     ["泰语", "th"],
    "vi",     ["越南语", "vi"],
    "id",     ["印尼语", "id"],
    "tr",     ["土耳其语", "tr"],
    "nl",     ["荷兰语", "nl"],
    "pl",     ["波兰语", "pl"],
    "uk",     ["乌克兰语", "uk"],
    "el",     ["希腊语", "el"],
    "cs",     ["捷克语", "cs"],
    "sv",     ["瑞典语", "sv"],
    "hu",     ["匈牙利语", "hu"],
    "ro",     ["罗马尼亚语", "ro"],
    "da",     ["丹麦语", "da"],
    "fi",     ["芬兰语", "fi"],
    "no",     ["挪威语", "no"],
    "ms",     ["马来语", "ms"],
    "fil",    ["菲律宾语", "fil"],
    "bn",     ["孟加拉语", "bn"],
    "ur",     ["乌尔都语", "ur"],
    "fa",     ["波斯语", "fa"],
    "he",     ["希伯来语", "iw"]
)

; 全局状态
gCaptureIh     := ""
gSideBtn       := ""
gMenuHotkeyLbl := ""
gMenuProviderLbl := ""
gSrcMenuLbl    := ""
gTgtMenuLbl    := ""
gLastLangMenu  := "src"
gConfigFile    := A_ScriptDir . "\config.conf"
hotkeyConf     := A_ScriptDir . "\hotkey.conf"

; 读取持久化配置（provider / 百度密钥 / 热键 / 语言）
LoadConfig()

;=============================================================
; 自检模式：translator.exe -selftest
;=============================================================
if A_Args.Length > 0 and A_Args[1] = "-selftest" {
    sample := "This function computes the Fast Fourier Transform of the input signal."
    res := TranslateText(sample)
    ShowResult(sample, res)
    Sleep 6000
    ExitApp()
}

; 注册热键 + 托盘菜单 + 启动提示
Hotkey(gHotkey, TranslateSelected, "On")
UpdateMenuLabels()
ToolTip("translator 已就绪`n热键：" . FormatHotkey(gHotkey) . "`n" . LangDisplay(gSourceLang) . " → " . LangDisplay(gTargetLang), 0, 0)
SetTimer(() => ToolTip(), -4000)

;=============================================================
; 热键入口：复制选中文本 -> 翻译 -> 弹窗
;=============================================================
TranslateSelected(*) {
    ClipSaved := ClipboardAll()          ; 保存整个剪贴板（含格式）
    A_Clipboard := ""
    Send("^c")
    if !ClipWait(1.5, 1) {
        A_Clipboard := ClipSaved         ; 恢复剪贴板
        ShowError("未检测到选中的文本`n`n请先在窗口里选中文本再按热键。")
        return
    }
    text := Trim(A_Clipboard)
    A_Clipboard := ClipSaved             ; 恢复用户剪贴板
    if text = "" {
        ShowError("选中内容为空。")
        return
    }
    translated := TranslateText(text)
    if translated = ""
        return
    ShowResult(text, translated)
}

;=============================================================
; 翻译分发：按当前提供商选择
;=============================================================
TranslateText(text) {
    if gProvider = "baidu" and gBaiduAppid != "" and gBaiduSecret != ""
        return TranslateBaidu(text)
    return TranslateMyMemory(text)
}

;=============================================================
; MyMemory 免费 API（源语言 auto 时自动检测）
;=============================================================
TranslateMyMemory(text) {
    global
    ; MyMemory 免费 API 单次请求限 500 字符，长文本按字符分片翻译
    parts := SplitTextByChars(text, 450)
    result := ""
    ; 源语言：auto 时用 Autodetect（MyMemory 有效写法），否则用语言 ID
    srcCode := gSourceLang = "auto" ? "Autodetect" : gSourceLang
    for part in parts {
        url := "https://api.mymemory.translated.net/get?q=" . UrlEncode(part) . "&langpair=" . UrlEncode(srcCode . "|" . gTargetLang)
        try {
            http := ComObject("WinHttp.WinHttpRequest.5.1")
            http.Open("GET", url, false)
            http.SetTimeouts(gApiTimeout, gApiTimeout, gApiTimeout, gApiTimeout)
            http.Send()
            status := http.Status
            if status != 200 {
                ShowError("MyMemory 返回错误（HTTP " . status . "）。`n请检查网络后重试。")
                return ""
            }
            body := http.ResponseText
            dq := Chr(34)
            if RegExMatch(body, dq . "translatedText" . dq . ":" . dq . "(.*?)" . dq, &m) {
                result .= DecodeUnicode(m[1])
            } else {
                if RegExMatch(body, dq . "responseDetails" . dq . ":" . dq . "(.*?)" . dq, &e)
                    ShowError("MyMemory 翻译失败：" . DecodeUnicode(e[1]))
                else
                    ShowError("MyMemory 翻译失败：无法解析服务响应。")
                return ""
            }
        } catch as err {
            ShowError("网络请求失败：" . err.Message)
            return ""
        }
    }
    return result
}

;=============================================================
; 百度翻译开放平台（免费标准版，需注册 https://fanyi-api.baidu.com）
; 源语言 auto 时 from=auto（百度原生支持自动检测）
;=============================================================
TranslateBaidu(text) {
    global
    ; 百度标准版单次 q 限 6000 字节，长文本分片翻译
    parts := SplitTextByBytes(text, 5000)
    result := ""
    fromCode := BaiduLang(gSourceLang)   ; auto → "auto"
    toCode := BaiduLang(gTargetLang)
    for part in parts {
        salt := Random(100000, 999999)
        sign := Md5Hex(gBaiduAppid . part . salt . gBaiduSecret)
        url := "https://fanyi-api.baidu.com/api/trans/vip/translate?q=" . UrlEncode(part)
            . "&from=" . fromCode . "&to=" . toCode . "&appid=" . gBaiduAppid . "&salt=" . salt . "&sign=" . sign
        try {
            http := ComObject("WinHttp.WinHttpRequest.5.1")
            http.Open("GET", url, false)
            http.SetTimeouts(gApiTimeout, gApiTimeout, gApiTimeout, gApiTimeout)
            http.Send()
            body := http.ResponseText
            dq := Chr(34)
            ; 百度对多行 q 返回多个 trans_result，循环提取所有 dst
            outPart := ""
            mStart := 1
            gotDst := false
            while RegExMatch(body, dq . "dst" . dq . ":" . dq . "(.*?)" . dq, &m, mStart) {
                outPart .= DecodeUnicode(m[1]) . "`n"
                gotDst := true
                mStart := m.Pos + m.Len
            }
            if gotDst {
                result .= RTrim(outPart, "`n")
            } else {
                if RegExMatch(body, dq . "error_code" . dq . ":" . dq . "(.*?)" . dq, &e) and RegExMatch(body, dq . "error_msg" . dq . ":" . dq . "(.*?)" . dq, &em)
                    ShowError("百度翻译错误 " . e[1] . "：" . DecodeUnicode(em[1]) . "`n`n如为 52003/54001，请检查 APP ID 和密钥是否正确；`n如为 54003，请确认已开通该翻译服务。")
                else
                    ShowError("百度翻译失败：无法解析响应。")
                return ""
            }
        } catch as err {
            ShowError("网络请求失败：" . err.Message)
            return ""
        }
    }
    return result
}

;=============================================================
; 按字符数分割长文本（MyMemory 免费 API 单次限 500 字符）
;=============================================================
SplitTextByChars(text, maxChars) {
    parts := []
    current := ""
    lines := StrSplit(text, "`n")
    for line in lines {
        lineLen := StrLen(line)
        if lineLen > maxChars {
            if current != "" {
                parts.Push(current)
                current := ""
            }
            for ch in StrSplit(line) {
                if StrLen(current) + 1 > maxChars and current != "" {
                    parts.Push(current)
                    current := ""
                }
                current .= ch
            }
            if current != "" {
                parts.Push(current)
                current := ""
            }
        } else {
            if current = ""
                current := line
            else
                current .= "`n" . line
            if StrLen(current) > maxChars {
                parts.Push(current)
                current := ""
            }
        }
    }
    if current != ""
        parts.Push(current)
    return parts
}

;=============================================================
; 按 UTF-8 字节数分割长文本（百度 API 单次 q 限 6000 字节）
;=============================================================
SplitTextByBytes(text, maxBytes) {
    parts := []
    current := ""
    currBytes := 0
    lines := StrSplit(text, "`n")
    for line in lines {
        lineBytes := StrPut(line, "UTF-8") - 1
        if lineBytes > maxBytes {
            if current != "" {
                parts.Push(current)
                current := ""
                currBytes := 0
            }
            for ch in StrSplit(line) {
                b := StrPut(ch, "UTF-8") - 1
                if currBytes + b > maxBytes and current != "" {
                    parts.Push(current)
                    current := ch
                    currBytes := b
                } else {
                    current .= ch
                    currBytes += b
                }
            }
            if current != "" {
                parts.Push(current)
                current := ""
                currBytes := 0
            }
        } else {
            addBytes := lineBytes + (current = "" ? 0 : 1)
            if currBytes + addBytes > maxBytes and current != "" {
                parts.Push(current)
                current := line
                currBytes := lineBytes
            } else {
                if current = ""
                    current := line
                else
                    current .= "`n" . line
                currBytes += addBytes
            }
        }
    }
    if current != ""
        parts.Push(current)
    return parts
}

;=============================================================
; 语言工具：显示名 / 百度代码 / 菜单构建
;=============================================================
LangDisplay(id) {
    global
    if gLangs.Has(id)
        return gLangs[id][1]
    return id
}
BaiduLang(id) {
    global
    if gLangs.Has(id)
        return gLangs[id][2]
    return id
}
; 构建语言选择子菜单（type: "src" 含自动检测 / "tgt" 不含）
BuildLangMenu(type) {
    global
    langMenu := Menu()
    for id, info in gLangs {
        if type = "tgt" and id = "auto"
            continue
        langMenu.Add(info[1], MakeLangHandler(id))
    }
    current := type = "src" ? gSourceLang : gTargetLang
    langMenu.Check(LangDisplay(current))
    return langMenu
}
; 闭包捕获修正：通过函数参数传值，避免循环变量共享
MakeLangHandler(id) {
    if id = "auto"
        return (*) => SetSourceLang("auto")
    return (*) => SetLangFromMenu(id)
}
; 语言菜单点击分发（源/目标共用，由打开的子菜单决定）
SetLangFromMenu(id) {
    global
    if gLastLangMenu = "src"
        SetSourceLang(id)
    else
        SetTargetLang(id)
}
SetSourceLang(id) {
    global
    gSourceLang := id
    SaveConfig()
    UpdateMenuLabels()
    ToolTip("源语言已设为：" . LangDisplay(id), 0, 0)
    SetTimer(() => ToolTip(), -2000)
}
SetTargetLang(id) {
    global
    gTargetLang := id
    SaveConfig()
    UpdateMenuLabels()
    ToolTip("目标语言已设为：" . LangDisplay(id), 0, 0)
    SetTimer(() => ToolTip(), -2000)
}
; 菜单标题统一更新（勾选跟着重建）
UpdateMenuLabels() {
    global
    gMenuHotkeyLbl := "当前热键：" . FormatHotkey(gHotkey)
    gMenuProviderLbl := "翻译提供商：" . ProviderName(gProvider)
    gSrcMenuLbl := "源语言：" . LangDisplay(gSourceLang)
    gTgtMenuLbl := "目标语言：" . LangDisplay(gTargetLang)
    A_TrayMenu.Delete("")
    A_TrayMenu.Add(gMenuHotkeyLbl, (*) => 0)
    A_TrayMenu.Add(gMenuProviderLbl, ToggleProvider)
    A_TrayMenu.Add("配置百度翻译密钥…", ConfigBaidu)
    A_TrayMenu.Add(gSrcMenuLbl, BuildLangMenu("src"))
    A_TrayMenu.Add(gTgtMenuLbl, BuildLangMenu("tgt"))
    A_TrayMenu.Add()
    A_TrayMenu.Add("更改翻译热键…", ChangeHotkey)
    A_TrayMenu.Default := "更改翻译热键…"
}

;=============================================================
; URL 编码（UTF-8 字节级，符合 RFC 3986）
;=============================================================
UrlEncode(str) {
    byteCount := StrPut(str, "UTF-8")
    buf := Buffer(byteCount)
    StrPut(str, buf, "UTF-8")
    enc := ""
    loop byteCount - 1 {
        b := NumGet(buf, A_Index - 1, "UChar")
        c := Chr(b)
        if RegExMatch(c, "^[A-Za-z0-9\-_.~]$")
            enc .= c
        else
            enc .= Format("%{:02X}", b)
    }
    return enc
}

;=============================================================
; 解码 JSON 中的 \uXXXX 为中文
;=============================================================
DecodeUnicode(str) {
    out := ""
    i := 1
    n := StrLen(str)
    while i <= n {
        if SubStr(str, i, 1) = "\" and SubStr(str, i + 1, 1) = "u" {
            hex := SubStr(str, i + 2, 4)
            if RegExMatch(hex, "^[0-9A-Fa-f]{4}$") {
                out .= Chr(Integer("0x" . hex))
                i += 6
                continue
            }
        }
        out .= SubStr(str, i, 1)
        i += 1
    }
    return out
}

;=============================================================
; MD5（百度接口需要）：纯 AHK 实现，无外部进程
;=============================================================
Md5Hex(str) {
    byteLen := StrPut(str, "UTF-8") - 1
    bytes := []
    if byteLen > 0 {
        buf := Buffer(byteLen)
        StrPut(str, buf, byteLen, "UTF-8")
        loop byteLen
            bytes.Push(NumGet(buf, A_Index - 1, "UChar"))
    }
    bytes.Push(0x80)
    while Mod(bytes.Length, 64) != 56
        bytes.Push(0x00)
    bitLen := byteLen * 8
    loop 8 {
        bytes.Push(bitLen & 0xFF)
        bitLen := bitLen >> 8
    }
    K := [0xd76aa478,0xe8c7b756,0x242070db,0xc1bdceee,0xf57c0faf,0x4787c62a,0xa8304613,0xfd469501,0x698098d8,0x8b44f7af,0xffff5bb1,0x895cd7be,0x6b901122,0xfd987193,0xa679438e,0x49b40821,0xf61e2562,0xc040b340,0x265e5a51,0xe9b6c7aa,0xd62f105d,0x02441453,0xd8a1e681,0xe7d3fbc8,0x21e1cde6,0xc33707d6,0xf4d50d87,0x455a14ed,0xa9e3e905,0xfcefa3f8,0x676f02d9,0x8d2a4c8a,0xfffa3942,0x8771f681,0x6d9d6122,0xfde5380c,0xa4beea44,0x4bdecfa9,0xf6bb4b60,0xbebfbc70,0x289b7ec6,0xeaa127fa,0xd4ef3085,0x04881d05,0xd9d4d039,0xe6db99e5,0x1fa27cf8,0xc4ac5665,0xf4292244,0x432aff97,0xab9423a7,0xfc93a039,0x655b59c3,0x8f0ccc92,0xffeff47d,0x85845dd1,0x6fa87e4f,0xfe2ce6e0,0xa3014314,0x4e0811a1,0xf7537e82,0xbd3af235,0x2ad7d2bb,0xeb86d391]
    s := [7,12,17,22,7,12,17,22,7,12,17,22,7,12,17,22,5,9,14,20,5,9,14,20,5,9,14,20,5,9,14,20,4,11,16,23,4,11,16,23,4,11,16,23,4,11,16,23,6,10,15,21,6,10,15,21,6,10,15,21,6,10,15,21]
    a0 := 0x67452301, b0 := 0xefcdab89, c0 := 0x98badcfe, d0 := 0x10325476
    MASK := 0xFFFFFFFF
    index := 1
    while index <= bytes.Length {
        M := []
        loop 16 {
            i := index + (A_Index - 1) * 4
            w := bytes[i] | (bytes[i+1] << 8) | (bytes[i+2] << 16) | (bytes[i+3] << 24)
            M.Push(w & MASK)
        }
        A := a0, B := b0, C := c0, D := d0
        loop 64 {
            i := A_Index - 1
            if i < 16 {
                F := (B & C) | ((~B) & D)
                g := i
            } else if i < 32 {
                F := (D & B) | ((~D) & C)
                g := Mod(5 * i + 1, 16)
            } else if i < 48 {
                F := B ^ C ^ D
                g := Mod(3 * i + 5, 16)
            } else {
                F := C ^ (B | (~D))
                g := Mod(7 * i, 16)
            }
            F := (F + A + K[i+1] + M[g+1]) & MASK
            A := D
            D := C
            C := B
            sh := s[i+1]
            B := (B + (((F << sh) | (F >> (32 - sh))) & MASK)) & MASK
        }
        a0 := (a0 + A) & MASK
        b0 := (b0 + B) & MASK
        c0 := (c0 + C) & MASK
        d0 := (d0 + D) & MASK
        index += 64
    }
    res := ""
    vals := [a0, b0, c0, d0]
    for idx, val in vals {
        v := val
        loop 4 {
            res .= Format("{:02x}", v & 0xFF)
            v := v >> 8
        }
    }
    return res
}

;=============================================================
; 结果弹窗：上方原文、下方译文（可选中复制）+ 复制按钮
;=============================================================
ShowResult(src, dst) {
    global
    try gResultGui.Destroy()
    g := Gui("+AlwaysOnTop +Resize", "翻译结果 - translator")
    gResultGui := g
    g.MarginX := 12, g.MarginY := 10
    g.SetFont("s10", "Microsoft YaHei UI")
    g.Add("Text", "w520", "原文（" . LangDisplay(gSourceLang) . "）：")
    srcBox := g.Add("Edit", "w520 h110 ReadOnly +Wrap", src)
    srcBox.SetFont("s9", "Consolas")
    g.Add("Text", "w520", "译文（" . LangDisplay(gTargetLang) . "）：")
    dstBox := g.Add("Edit", "w520 h130 ReadOnly +Wrap", dst)
    dstBox.SetFont("s10", "Microsoft YaHei UI")
    g.Add("Button", "w90 h30 xm+330", "复制译文").OnEvent("Click", (*) => A_Clipboard := dst)
    g.Add("Button", "w90 h30 xm+430 Default", "关闭").OnEvent("Click", (*) => g.Destroy())
    g.Show()
}

;=============================================================
; 错误提示
;=============================================================
ShowError(msg) {
    MsgBox(msg, "translator", "Icon!")
}

;=============================================================
; 把 AHK 热键格式转为人类可读文本（^!d → Ctrl+Alt+D）
;=============================================================
FormatHotkey(hk) {
    out := ""
    if InStr(hk, "^")
        out .= "Ctrl+"
    if InStr(hk, "!")
        out .= "Alt+"
    if InStr(hk, "+")
        out .= "Shift+"
    if InStr(hk, "#")
        out .= "Win+"
    key := RegExReplace(hk, "^[\^\!\+\#]+", "")
    if key = "XButton1"
        key := "鼠标侧键1"
    else if key = "XButton2"
        key := "鼠标侧键2"
    else if key = "Escape"
        key := "Esc"
    return out . key
}

;=============================================================
; 更改翻译热键：托盘菜单调用，立即生效并持久化
;=============================================================
ChangeHotkey(*) {
    global
    newKey := CaptureHotkey()
    if newKey = "" {
        ToolTip("已取消更改热键", 0, 0)
        SetTimer(() => ToolTip(), -2000)
        return
    }
    if newKey = gHotkey {
        ToolTip("热键未变化：" . FormatHotkey(gHotkey), 0, 0)
        SetTimer(() => ToolTip(), -3000)
        return
    }
    Hotkey(gHotkey, "Off")
    try {
        Hotkey(newKey, TranslateSelected, "On")
    } catch as err {
        Hotkey(gHotkey, TranslateSelected, "On")
        ShowError("热键无效或已被占用：`n" . err.Message)
        return
    }
    gHotkey := newKey
    SaveConfig()
    UpdateMenuLabels()
    ToolTip("翻译热键已更改为：" . FormatHotkey(gHotkey), 0, 0)
    SetTimer(() => ToolTip(), -3000)
}

;=============================================================
; 切换翻译提供商（托盘菜单项，点击切换）
;=============================================================
ToggleProvider(*) {
    global
    if gProvider = "baidu" {
        gProvider := "mymemory"
    } else {
        if gBaiduAppid = "" or gBaiduSecret = "" {
            ShowError("尚未配置百度翻译密钥。`n`n请先选择托盘菜单「配置百度翻译密钥…」完成配置。")
            return
        }
        gProvider := "baidu"
    }
    SaveConfig()
    UpdateMenuLabels()
    ToolTip("翻译提供商已切换为：" . ProviderName(gProvider), 0, 0)
    SetTimer(() => ToolTip(), -3000)
}

;=============================================================
; 配置百度翻译密钥（托盘菜单入口，含注册引导）
;=============================================================
ConfigBaidu(*) {
    global
    ib := InputBox("百度翻译开放平台（免费标准版）`n`n步骤：`n1. 打开 https://fanyi-api.baidu.com/choose `n2. 勾选「通用文本翻译」→ 下一步 → 开通`n   （如提示先完成开发者认证，按提示实名）`n3. 开通后回控制台 → 我的服务 → 通用文本翻译`n4. 在服务信息中找到 APP ID 和密钥`n`n现在请粘贴你的 APP ID：", "配置百度翻译", "", "w440 h300")
    if ib.Result != "OK"
        return
    appid := Trim(ib.Value)
    if appid = "" {
        ShowError("APP ID 不能为空。")
        return
    }
    ib2 := InputBox("请粘贴你的密钥（Secret Key）：", "配置百度翻译", "", "w440 h120")
    if ib2.Result != "OK"
        return
    secret := Trim(ib2.Value)
    if secret = "" {
        ShowError("密钥不能为空。")
        return
    }
    gBaiduAppid := appid
    gBaiduSecret := secret
    gProvider := "baidu"
    SaveConfig()
    UpdateMenuLabels()
    ToolTip("百度翻译已配置并启用（提供商：百度翻译）", 0, 0)
    SetTimer(() => ToolTip(), -3000)
}

;=============================================================
; 提供商名称
;=============================================================
ProviderName(p) {
    if p = "baidu"
        return "百度翻译"
    return "MyMemory"
}

;=============================================================
; 配置读写（config.conf：hotkey / provider / src_lang / tgt_lang / baidu_*）
;=============================================================
SaveConfig() {
    global
    content := "hotkey=" . gHotkey . "`nprovider=" . gProvider
        . "`nsrc_lang=" . gSourceLang . "`ntgt_lang=" . gTargetLang
        . "`nbaidu_appid=" . gBaiduAppid . "`nbaidu_secret=" . gBaiduSecret . "`n"
    FileOpen(gConfigFile, "w", "UTF-8").Write(content)
}
LoadConfig() {
    global
    if FileExist(gConfigFile) {
        for line in StrSplit(FileRead(gConfigFile), "`n") {
            line := Trim(line)
            if line = "" or SubStr(line, 1, 1) = ";"
                continue
            pos := InStr(line, "=")
            if pos = 0
                continue
            key := Trim(SubStr(line, 1, pos - 1))
            val := Trim(SubStr(line, pos + 1))
            if key = "hotkey" and val != "" {
                try {
                    Hotkey(val, TranslateSelected, "On")
                    Hotkey(val, "Off")
                    gHotkey := val
                }
            } else if key = "provider" and (val = "baidu" or val = "mymemory") {
                gProvider := val
            } else if key = "src_lang" and gLangs.Has(val) {
                gSourceLang := val
            } else if key = "tgt_lang" and gLangs.Has(val) and val != "auto" {
                gTargetLang := val
            } else if key = "baidu_appid" {
                gBaiduAppid := val
            } else if key = "baidu_secret" {
                gBaiduSecret := val
            }
        }
    } else if FileExist(hotkeyConf) {
        ; 兼容旧版 hotkey.conf（仅热键）
        saved := StrReplace(Trim(FileRead(hotkeyConf)), Chr(0xFEFF), "")
        if saved != "" {
            try {
                Hotkey(saved, TranslateSelected, "On")
                Hotkey(saved, "Off")
                gHotkey := saved
            }
        }
    }
}

;=============================================================
; 鼠标侧键回调：InputHook 只监听键盘，鼠标侧键用临时热键捕获
;=============================================================
MouseSideHook(keyName) {
    global
    gSideBtn := keyName
    gCaptureIh.Stop()
}

;=============================================================
; 热键捕获：弹窗等待用户按下组合键（或鼠标侧键）
;=============================================================
CaptureHotkey() {
    global
    g := Gui("+AlwaysOnTop", "更改翻译热键")
    g.SetFont("s11", "Microsoft YaHei UI")
    g.Add("Text", "w380 h110 Center", "请按下新的热键组合…`n`n例如：Ctrl+Alt+D、F8、鼠标侧键等`n（按 Esc 取消）")
    g.Show()

    gSideBtn := ""
    gCaptureIh := InputHook()
    gCaptureIh.VisibleNonText := false
    gCaptureIh.KeyOpt("{All}", "E")
    gCaptureIh.KeyOpt("{LCtrl}{RCtrl}{LAlt}{RAlt}{LShift}{RShift}{LWin}{RWin}", "-E")
    Hotkey("XButton1", MouseSideHook)
    Hotkey("XButton2", MouseSideHook)
    gCaptureIh.Start()
    gCaptureIh.Wait()
    Hotkey("XButton1", "Off")
    Hotkey("XButton2", "Off")

    g.Destroy()

    if gSideBtn != "" {
        mods := ""
        if GetKeyState("Ctrl")
            mods .= "^"
        if GetKeyState("Alt")
            mods .= "!"
        if GetKeyState("Shift")
            mods .= "+"
        if GetKeyState("Win")
            mods .= "#"
        deadline := A_TickCount + 3000
        while (GetKeyState("XButton1") or GetKeyState("XButton2")) and A_TickCount < deadline
            Sleep 50
        return mods . gSideBtn
    }

    if gCaptureIh.EndReason != "EndKey"
        return ""
    key := gCaptureIh.EndKey
    if key = "Escape" or key = ""
        return ""

    deadline := A_TickCount + 3000
    try {
        while (GetKeyState("Ctrl") or GetKeyState("Alt") or GetKeyState("Shift") or GetKeyState("Win") or GetKeyState(key)) and A_TickCount < deadline
            Sleep 50
    }

    mods := RegExReplace(gCaptureIh.EndMods, "[<>](.)(?:>\1)?", "$1")
    return mods . key
}
