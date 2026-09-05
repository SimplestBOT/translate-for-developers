# Translate for Developers

[![CI](https://github.com/SimplestBOT/translate-for-developers/actions/workflows/ci.yml/badge.svg)](https://github.com/SimplestBOT/translate-for-developers/actions/workflows/ci.yml)

<p align="center"><img src="demo.gif" alt="划词 → 热键 → 译文 → 复制" width="590"></p>

[简体中文](README.md) · [English](README.en.md) · **日本語** · [한국어](README.ko.md)

英語のテキストを選択してホットキーを押すだけで、中国語訳がすぐポップアップ表示されます。コードのコメント、英語のドキュメント、論文の要約を読むときに——**あらゆるアプリで使えます**。

MATLAB の英語コメントが読めない？VS Code の英語エラーメッセージで詰まった？PDF の一節で困った？
選択 → ホットキー → 翻訳がすぐ表示。**選択できないテキスト——エラーのスクリーンショット、画像、動画の字幕——は、スクリーンショットホットキーで範囲を囲むだけ。**

## このツールを作った理由

有名な辞書系のマウスオーバー翻訳は、UI 自動化でハイライトされた選択範囲を読み取る仕組みのため、**MATLAB（Java コントロール）などのアプリでは単語を取得できません**。
このツールは**クリップボード方式**を採用しました：テキストを選択すると自動で `Ctrl+C` を送信 → クリップボードを読み取り → 翻訳 → ポップアップ表示。
MATLAB、VS Code、ブラウザ、PDF リーダーなど、**コピーに対応しているすべてのアプリ**で動作します。

## 機能

- どのアプリでもテキストを選択 → ホットキーで翻訳（デフォルト `Ctrl+Alt+T`、いつでも変更可能）
- **スクリーンショット翻訳（v1.6）**：エラーのスクショ・画像・動画字幕など、選択できない文字も翻訳。スクリーンショットホットキー（デフォルト `Ctrl+Alt+Z`、トレイメニューからも）→ 範囲をドラッグ → Windows 内蔵 OCR（無料・オフライン・依存ゼロ）で認識 → 自動翻訳
- **入力翻訳（v1.7）**：`Ctrl+Alt+I` で複数行入力ボックスを開き、`Enter` で翻訳——エラー調査・コメント執筆・変数名検討の手入力シーンに
- **新しいダーク UI（v1.1）**：システム標準の WebView2 で描画——ガラス風カード、入場アニメーション、スキャン光ローディングバー、スケルトンスクリーン、キーキャップのバネアニメ；翻訳は**非同期**で、ウィンドウは即開き、アニメはカクつきません
- **翻訳元言語の自動検出**（手動指定も可能）、**翻訳先言語は 30+ 種類**（中国語・英語・日本語・韓国語・フランス語・ドイツ語・スペイン語・ロシア語…）
- クリップボードは自動でバックアップ/復元——コピーしていた内容を**壊しません**
- 翻訳サービスを選択可能：**MyMemory**（無料・登録不要）／**百度翻訳**／**DeepL**／**AI LLM**（OpenAI 互換：OpenAI/DeepSeek/Kimi/智譜/Ollama/カスタム）
- 長文は自動分割し、百度の1リクエスト6000バイト制限を回避
- LLM 翻訳はコード/パス/URL/識別子をプレースホルダで保護（復元失敗時は黙って返さない）
- 操作のこだわり：タイトルバーのドラッグ移動、`Esc` で閉じる、`Enter` でコピー、コピーボタンが緑のチェックマークに変化、右下のトースト通知（野雑なメッセージボックスの代わり）
- トレイメニュー：**ホットキー変更（キーボードで直接押すだけ）**、**翻訳サービスの切り替え**、言語選択、終了
- インストール不要のポータブル版：C# ホスト + WebView2 描画（システム標準ランタイム）

## 使い方

1. `start-translator.bat` を実行（または `src/bridge/translator-ui.exe` を直接実行）——トレイに「T」アイコンが表示されます
2. 任意のアプリで**英語を選択** → **`Ctrl+Alt+T`** を押す → 翻訳ウィンドウがポップアップ
3. ウィンドウ内：タイトルバーをドラッグして移動；`Esc` で閉じる；`Enter` または「コピー」で訳文をコピー
4. トレイアイコン（T）→ 右クリックメニュー：ホットキー変更／サービス切り替え／言語選択／スクリーンショット翻訳／入力翻訳／終了
5. **スクリーンショット翻訳**：`Ctrl+Alt+Z` を押す（またはトレイメニュー）→ 範囲をドラッグ → 離すと自動で OCR・翻訳；`Esc`・右クリックでキャンセル
6. **入力翻訳**：`Ctrl+Alt+I` を押す（またはトレイメニュー）→ テキストを入力 → `Enter` で翻訳（`Shift+Enter` で改行）

## 設定

設定ファイル `config.conf`（`bridge/` の隣の `scripts/` フォルダ、初回起動時に自動生成）：

```ini
hotkey=^!d              ; 翻訳ホットキー（トレイメニューから変更可能）
shot_hotkey=^!z         ; スクリーンショット翻訳ホットキー（このファイルを編集後、ホスト再起動で反映）
input_hotkey=^!i        ; 入力翻訳ホットキー（このファイルを編集後、ホスト再起動で反映）
src_lang=auto           ; 翻訳元言語：auto=自動検出、または zh-CN/en/ja/ko/…
tgt_lang=zh-CN          ; 翻訳先言語（デフォルト：簡体字中国語）
provider=mymemory       ; mymemory / baidu / deepl / llm
baidu_appid=            ; 百度翻訳の APP ID（百度を使う場合に入力）
baidu_secret=           ; 百度翻訳のシークレット（百度を使う場合に入力）
deepl_key=              ; DeepL API Key（DeepL を使う場合に入力）
deepl_endpoint=         ; 任意（Pro 端点）；空欄で無料端点
llm_preset=             ; AI LLM プリセット: openai/deepseek/kimi/zhipu/ollama/custom
llm_base_url=           ; OpenAI 互換 Base URL（例: https://api.deepseek.com/v1）
llm_api_key=            ; API Key（ローカル Ollama は空欄可）
llm_model=              ; モデル名（例: gpt-4o-mini / deepseek-chat）
llm_prompt=             ; 任意の翻訳プロンプト；空欄で内蔵デフォルト
```

> トレイメニューからのホットキー変更・サービス切り替えはこのファイルに自動保存されます。手動編集は不要です。スクリーンショット翻訳ホットキー（`shot_hotkey`）は当面ファイル編集のみ：変更後ホストを再起動してください。

> **キーの保護**: 百度の APP ID / シークレットは Windows **DPAPI** で暗号化して保存されます（`dpapi:` 接頭辞の暗号文。この PC の現在の Windows アカウントでのみ復号可能）。`config.conf` を別の PC へコピーしてもキーは読み出せません。旧バージョンの平文ファイルは初回起動時に自動移行されます。診断ログにキー内容は記録されず、`config.conf` は `.gitignore` で除外済みです（手動コミット禁止）。

## 対応言語

翻訳元言語はデフォルトで**自動検出**（手動指定も可能）；翻訳先は以下の 30+ 言語から選べます：

| 言語 | コード | 言語 | コード |
|---|---|---|---|
| 簡体字中国語 | zh-CN | 英語 | en |
| 繁体字中国語 | zh-TW | 日本語 | ja |
| 韓国語 | ko | フランス語 | fr |
| ドイツ語 | de | スペイン語 | es |
| ポルトガル語 | pt | ロシア語 | ru |
| イタリア語 | it | アラビア語 | ar |
| ヒンディー語 | hi | タイ語 | th |
| ベトナム語 | vi | インドネシア語 | id |
| トルコ語 | tr | オランダ語 | nl |
| ポーランド語 | pl | ウクライナ語 | uk |
| ギリシャ語 | el | チェコ語 | cs |
| スウェーデン語 | sv | ハンガリー語 | hu |
| ルーマニア語 | ro | デンマーク語 | da |
| フィンランド語 | fi | ノルウェー語 | no |
| マレー語 | ms | フィリピン語 | fil |
| ベンガル語 | bn | ウルドゥー語 | ur |
| ペルシャ語 | fa | ヘブライ語 | he |

> トレイメニュー →「翻訳元言語」「翻訳先言語」サブメニューで切り替え、選択は自動保存されます。

## システム要件

- Windows 10 1903+（最新の更新済み）または Windows 11（.NET Framework 4.8 ランタイムは OS に同梱）
- WebView2 ランタイム（**Win10/11 には標準搭載**——ほとんどの PC ではインストール不要）

## 翻訳サービス

| サービス | 費用 | 登録 | 備考 |
|---|---|---|---|
| MyMemory | 無料 | 不要 | 1日約5万文字、応答約1秒 |
| 百度翻訳 | 無料 | 必要 | 品質がより安定、長文分割対応 |
| DeepL | 月50万文字の無料枠 | 必要 | 高品質、30+ 言語、Pro 端点対応 |
| AI LLM | 各プロバイダ | 各自 | OpenAI 互換 API；DeepSeek/Kimi/智譜/Ollama プリセット内蔵；**開発者コンテンツ保護**（コード/パス/URL は翻訳しない） |

**百度無料版の開通方法**：fanyi-api.baidu.com → ログイン → コンソール → アプリ作成（汎用テキスト翻訳／標準版）
→ APP ID とシークレットをコピー → トレイメニュー「サービス切り替え」→ 百度 → 認証情報を入力。

## ソースからビルド

[.NET SDK](https://dotnet.microsoft.com)（最近の任意のバージョン、ターゲットは net48）が必要です：

```
dotnet build src/csharp/TranslatorHost/TranslatorHost.csproj -c Release
```

出力は `src/csharp/TranslatorHost/bin/Release/net48/win-x64/` にあります。そのディレクトリの中身を
`bridge/`（`start-translator.bat` のパスと一致）にコピーすれば実行できます。フロントエンドの各ページ
（`src/webui/`、Vite + React 19 + TS）はページ単位でビルドし、成果物 `webui/dist/<page>.html` をホストが自動読み込みします。

### デバッグ用スイッチ

```
translator-ui.exe --selftest            ; ヘッドレス自己テスト（WebView2／設定の読み書き）
translator-ui.exe --open result,text    ; デバッグ用ウィンドウ（60秒の安全タイムアウトで自動終了）
TFD_HEADLESS=1                          ; トレイ・ホットキーなし（サンドボックス e2e）
TFD_PIPE_NAME=<name>                    ; インスタンス分離キー（並列テスト用）
TFD_TEST_REUSE=1                        ; 結果ウィンドウ再利用フローを自動駆動
```

## アーキテクチャ（v1.7、移行完了）

- **C# ホスト**（`src/csharp/TranslatorHost`、net48 + WinForms + WebView2）：唯一のホスト——
  トレイ、グローバルホットキー（選択 + スクリーンショット翻訳）、選択キャプチャ（ターミナル保護・UIA 直読み
  ヘルパー子プロセス）、スクリーンショット翻訳（オーバーレイ範囲選択 → `Windows.Media.Ocr`）、
  ウィンドウライフサイクル、WebView2、DWM 角丸、ネイティブドラッグ。
  WinForms は Windows 統合のみ担当し、ビジネスロジックは Form に入れません。
- **コアライブラリ**（`src/csharp/TranslatorCore`）：翻訳／プロバイダ／設定／クリップボード／HTTP／JSON、
  すべて async + CancellationToken。
- **UI ページ**（`src/webui/`、React 19 + TypeScript）：settings/result/capture/config/input の5ページ、ページ単位のシングルファイルビルド；
  ホストとは JSON メッセージプロトコルで通信（契約は `docs/protocol.md`）。
- AHK 版と移行期の Named Pipe ブリッジは、それぞれ v1.4／v1.5 で廃止・削除しました（バックアップはリポジトリに含みません）。

## ディレクトリ構成

```
translate-for-developers/
├── README.md
├── LICENSE
├── .gitignore
├── start-translator.bat        # 起動入口（C# ホストを起動）
├── docs/                       # architecture / protocol / known-issues
└── src/
    ├── csharp/                 # C# ホスト + コアライブラリ + 自己テスト
    │   ├── TranslatorHost/     # WinForms/WebView2/トレイ/ホットキー/選択キャプチャ
    │   ├── TranslatorCore/     # 翻訳/プロバイダ/設定/クリップボード（クラスライブラリ）
    │   ├── TranslatorCore.Tests/  # 内蔵アサーション自己テスト（dotnet run）
    │   └── build-bridge.ps1    # ビルド + bridge/ へ配置
    ├── webui/                  # React 19 + TS フロントエンド（ページ単位シングルファイル）
    │   ├── src/                # settings/result/capture/config + ブリッジプロトコル層
    │   └── dist/               # ビルド成果物 <page>.html（自己完結）
    ├── icon.ico
    └── WebView2Loader.dll
```

## よくある質問

- **ホットキーが反応しない**：トレイに「T」アイコンがあるか確認；英語を含むテキストが選択されているか確認
- **「選択されたテキストが検出されません」と表示される**：先にテキストを選択してからホットキーを押す（一部のアプリでは先にウィンドウをクリックしてフォーカスを合わせる必要があります）
- **「ネットワークリクエスト失敗」と表示される**：ネットワークを確認；MyMemory はたまにタイムアウト——ウィンドウ内の「再試行」をクリック
- **翻訳サービスを変えたい**：トレイメニュー → 翻訳サービスの切り替え
- **ホットキーが他のソフトと競合する**：トレイメニュー → ホットキー変更 → お好みの組み合わせを直接押す
- **スクリーンショット翻訳で「OCR 言語パックが未インストール」**：Windows 設定 → 時刻と言語 → 言語と地域 → 言語の追加 → 「光学文字認識」オプション機能にチェック（Win10/11 は通常中英内蔵）
- **スクリーンショット翻訳の認識精度が低い**：OCR 言語は「翻訳元言語」設定に追従（auto = システム言語）。英語のみの内容なら翻訳元を英語に。小さすぎる文字（約 12px 未満）は認識率が低下
- **翻訳ウィンドウの移動方法**：上部のタイトルバー（ロゴの行）を掴んでドラッグ
- **初回起動で「WebView2 が見つかりません」**：[Microsoft のサイト](https://developer.microsoft.com/microsoft-edge/webview2/)から Evergreen Runtime を一度インストール（通常の Win10/11 では不要）
- **スタートアップ登録（任意）**：translator.exe のショートカットを `shell:startup` に入れる

## ライセンス

[MIT](LICENSE)
