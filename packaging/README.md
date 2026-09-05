# packaging/ — 分发渠道与提交流程

本目录存放 scoop / winget 的 manifest 模板。Release workflow（tag → 自动构建 →
便携 zip 挂 GitHub Release）产出 asset 后，按下列步骤发布到包管理器。
`SHA256_PLACEHOLDER_FILL_ON_RELEASE` 在每次发版后都要更新（workflow 的
Release 页面 step summary 会打印 zip 的 sha256）。

## 自动化边界（哪些自动、哪些人工）

| 环节 | 自动化 |
|---|---|
| 构建 + Core 测试 + WebUI 五页 + selftest | ✅ CI（push/PR 自动） |
| tag `v*` → 构建 + 测试 + 组包 + 挂 Release asset | ✅ release.yml 全自动 |
| zip SHA256 | ✅ 自动打印在 Release step summary |
| scoop bucket manifest 更新 | ⚙️ 半自动（见下，一条命令） |
| scoop 官方 main bucket PR | 👤 人工（可选） |
| winget-pkgs PR | 👤 人工（首次提交 + 每版本更新） |

## scoop：自建 bucket（推荐，零审核全自动）

1.（仅首次）建 bucket 仓库：
   ```
   gh repo create SimplestBOT/scoop-bucket --public
   ```
   把本目录的 `translate-for-developers.json` 提交为 bucket 根下的
   `bucket/translate-for-developers.json`（scoop bucket 约定结构）。
2.（每次发版后）Release 页面拿到 zip sha256，替换 json 的 `version`/`hash`
   后推送到 bucket 仓库：
   ```
   gh workflow ...   # 或本地：改 hash → git commit → git push
   ```
   用户安装：
   ```
   scoop bucket add tfd https://github.com/SimplestBOT/scoop-bucket
   scoop install tfd/translate-for-developers
   ```
3. `checkver: github` + `autoupdate` 已配置：`scoop update *` 会自动发现新
   Release 并按 `v$version` URL 模板升级；只需维护 json 的 `hash` 字段。

## winget：microsoft/winget-pkgs PR（人工）

1. fork https://github.com/microsoft/winget-pkgs
2. 新包首次提交 = 在 `manifests/s/SimplestBOT/translate-for-developers/<版本>/`
   下放本目录三个 yaml（defaultLocale / locale.zh-CN / installer），
   `InstallerSha256` 填 Release zip 的 sha256。
3. 提 PR（标题 `New package: SimplestBOT.translate-for-developers version 1.7.0`），
   过 CI（winget-validate）后由微软审核合入。之后每个新版本再提一个目录 PR。
4. 验证 manifest（本地装 winget-cli 后）：
   ```
   winget validate --manifest .\packaging\winget\
   ```
5. 合入后用户安装：`winget install SimplestBOT.translate-for-developers`

## 本地复现 CI 的组包（不依赖 GitHub）

```
dotnet build src/csharp/TranslatorHost/TranslatorHost.csproj -c Release
dotnet build src/csharp/TranslatorUia/TranslatorUia.csproj -c Release
cd src/webui && 对每页 TFD_PAGE=<p> npm run build
./package-release.ps1 -Version v1.7.0
```
