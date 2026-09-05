# Translate for Developers

[![CI](https://github.com/SimplestBOT/translate-for-developers/actions/workflows/ci.yml/badge.svg)](https://github.com/SimplestBOT/translate-for-developers/actions/workflows/ci.yml)

<p align="center"><img src="demo.gif" alt="划词 → 热键 → 译文 → 复制" width="590"></p>

[简体中文](README.md) · [English](README.en.md) · [日本語](README.ja.md) · **한국어**

영어 텍스트를 선택하고 단축키만 누르면 중국어 번역이 바로 팝업으로 나타납니다. 코드 주석, 영어 문서, 논문 초록을 읽을 때——**어떤 앱에서든 사용 가능**합니다.

MATLAB의 영어 주석이 이해되지 않나요? VS Code의 영어 에러 메시지에 막혔나요? PDF의 한 구절에서 고민 중인가요?
선택 → 단축키 → 번역이 바로 표시됩니다. **선택할 수 없는 텍스트——에러 스크린샷, 이미지, 동영상 자막——은 스크린샷 단축키로 영역을 박스하기만 하면 됩니다.**

## 이 도구를 만든 이유

기존 사전형 스크린 번역 앱들은 UI 자동화로 하이라이트된 선택 영역을 읽는 방식이라 **MATLAB(Java 컨트롤) 같은 프로그램에서는 텍스트를 가져올 수 없습니다**.
이 도구는 **클립보드 방식**을 채택했습니다: 텍스트를 선택하면 자동으로 `Ctrl+C`를 전송 → 클립보드를 읽음 → 번역 → 팝업 표시.
MATLAB, VS Code, 브라우저, PDF 리더 등 **복사를 지원하는 모든 앱**에서 동작합니다.

## 기능

- 어떤 앱에서든 텍스트 선택 → 단축키로 번역 (기본값 `Ctrl+Alt+T`, 언제든 변경 가능)
- **스크린샷 번역 (v1.6)**: 에러 스크린샷·이미지·동영상 자막처럼 선택할 수 없는 텍스트도 번역. 스크린샷 단축키 (기본값 `Ctrl+Alt+Z`, 트레이 메뉴에도 있음) → 영역 드래그 → Windows 내장 OCR (무료·오프라인·의존성 없음) 인식 → 자동 번역
- **입력 번역 (v1.7)**: `Ctrl+Alt+I`로 여러 줄 입력창을 열고 `Enter`로 번역——에러 조회·주석 작성·변수명 고민 시 직접 입력
- **완전히 새로운 다크 UI(v1.1)**: 시스템 내장 WebView2로 렌더링 —— 글래스 카드, 등장 애니메이션, 스캔 빔 로딩 바, 스켈레톤 화면, 키캡 스프링 효과; 번역은 **비동기** 처리되어 창이 즉시 열리고 애니메이션이 끊기지 않습니다
- **원본 언어 자동 감지**(수동 지정도 가능), **대상 언어 30+ 종**(중국어·영어·일본어·한국어·프랑스어·독일어·스페인어·러시아어…)
- 클립보드 자동 백업/복원 —— 복사해둔 내용을 **절대 훼손하지 않습니다**
- 번역 서비스 선택 가능: **MyMemory**(무료, 가입 불필요) / **바이두 번역** / **DeepL** / **AI LLM**(OpenAI 호환: OpenAI/DeepSeek/Kimi/지푸/Ollama/사용자 정의)
- 긴 텍스트 자동 분할로 바이두의 요청당 6000바이트 제한 우회
- LLM 번역은 코드/경로/URL/식별자를 플레이스홀더로 보호(복원 실패 시 조용히 반환하지 않음)
- 세심한 인터랙션: 제목표시줄 드래그 이동, `Esc`로 닫기, `Enter`로 복사, 복사 버튼이 초록색 체크 표시로 전환, 우하단 토스트 알림(투박한 메시지 박스 대신)
- 트레이 메뉴: **단축키 변경(키보드로 직접 누르기만 하면 됨)**, **번역 서비스 전환**, 언어 선택, 종료
- 설치 불필요한 포터블 방식: C# 호스트 + WebView2 렌더링(시스템 제공 런타임)

## 사용 방법

1. `start-translator.bat` 실행(또는 `src/bridge/translator-ui.exe` 직접 실행)—— 트레이에 "T" 아이콘이 나타납니다
2. 아무 앱에서나 **영어 텍스트 선택** → **`Ctrl+Alt+T`** 누름 → 번역 창이 팝업으로 열립니다
3. 창 안에서: 제목표시줄을 드래그해 이동; `Esc`로 닫기; `Enter` 또는 "복사" 버튼으로 번역 복사
4. 트레이 아이콘(T) → 오른쪽 클릭 메뉴: 단축키 변경 / 서비스 전환 / 언어 선택 / 스크린샷 번역 / 입력 번역 / 종료
5. **스크린샷 번역**: `Ctrl+Alt+Z` 누름 (또는 트레이 메뉴) → 영역을 드래그 → 놓으면 자동 OCR·번역; `Esc`·오른쪽 클릭으로 취소
6. **입력 번역**: `Ctrl+Alt+I` 누름 (또는 트레이 메뉴) → 텍스트 입력 → `Enter`로 번역 (`Shift+Enter` 줄바꿈)

## 설정

설정 파일 `config.conf`(`bridge/` 옆의 `scripts/` 폴더에 생성, 첫 실행 시 자동 생성):

```ini
hotkey=^!d              ; 번역 단축키 (트레이 메뉴에서 변경 가능)
shot_hotkey=^!z         ; 스크린샷 번역 단축키 (이 파일 수정 후 호스트 재시작 시 적용)
input_hotkey=^!i        ; 입력 번역 단축키 (이 파일 수정 후 호스트 재시작 시 적용)
src_lang=auto           ; 원본 언어: auto=자동 감지, 또는 zh-CN/en/ja/ko/…
tgt_lang=zh-CN          ; 대상 언어 (기본값: 중국어 간체)
provider=mymemory       ; mymemory / baidu / deepl / llm
baidu_appid=            ; 바이두 번역 APP ID (바이두 사용 시 입력)
baidu_secret=           ; 바이두 번역 시크릿 키 (바이두 사용 시 입력)
deepl_key=              ; DeepL API Key (DeepL 사용 시 입력)
deepl_endpoint=         ; 선택(Pro 엔드포인트); 비우면 무료 엔드포인트
llm_preset=             ; AI LLM 프리셋: openai/deepseek/kimi/zhipu/ollama/custom
llm_base_url=           ; OpenAI 호환 Base URL (예: https://api.deepseek.com/v1)
llm_api_key=            ; API Key (로컬 Ollama는 비워도 됨)
llm_model=              ; 모델 이름 (예: gpt-4o-mini / deepseek-chat)
llm_prompt=             ; 선택적 번역 프롬프트; 비우면 내장 기본값
```

> 트레이 메뉴에서 단축키 변경·서비스 전환 시 이 파일에 자동 저장됩니다. 수동 편집은 불필요합니다. 스크린샷 번역 단축키(`shot_hotkey`)는 당분간 파일 편집만 지원: 수정 후 호스트를 재시작하세요.

> **키 보안**: 바이두 APP ID / 시크릿 키는 Windows **DPAPI**로 암호화되어 저장됩니다(`dpapi:` 접두사 암호문, 이 PC의 현재 Windows 계정에서만 복호화 가능). `config.conf`를 다른 PC로 복사해도 키를 읽을 수 없습니다. 구버전 평문 파일은 첫 실행 시 자동 마이그레이션됩니다. 진단 로그에 키 내용은 기록되지 않으며, `config.conf`는 `.gitignore`에서 제외되어 있습니다(수동 커밋 금지).

## 지원 언어

원본 언어는 기본값으로 **자동 감지**되며(수동 지정 가능), 대상 언어는 다음 30+ 개 중 선택할 수 있습니다:

| 언어 | 코드 | 언어 | 코드 |
|---|---|---|---|
| 중국어 간체 | zh-CN | 영어 | en |
| 중국어 번체 | zh-TW | 일본어 | ja |
| 한국어 | ko | 프랑스어 | fr |
| 독일어 | de | 스페인어 | es |
| 포르투갈어 | pt | 러시아어 | ru |
| 이탈리아어 | it | 아랍어 | ar |
| 힌디어 | hi | 태국어 | th |
| 베트남어 | vi | 인도네시아어 | id |
| 터키어 | tr | 네덜란드어 | nl |
| 폴란드어 | pl | 우크라이나어 | uk |
| 그리스어 | el | 체코어 | cs |
| 스웨덴어 | sv | 헝가리어 | hu |
| 루마니아어 | ro | 덴마크어 | da |
| 핀란드어 | fi | 노르웨이어 | no |
| 말레이어 | ms | 필리핀어 | fil |
| 벵골어 | bn | 우르두어 | ur |
| 페르시아어 | fa | 히브리어 | he |

> 트레이 메뉴 → "원본 언어" / "대상 언어" 하위 메뉴로 전환하며, 선택은 자동 저장됩니다.

## 시스템 요구 사항

- Windows 10 1903+(최신 업데이트 적용) 또는 Windows 11 (.NET Framework 4.8 런타임은 OS에 내장)
- WebView2 런타임(**Win10/11에 기본 탑재** — 대부분의 PC는 별도 설치 불필요)

## 번역 서비스

| 서비스 | 비용 | 가입 | 비고 |
|---|---|---|---|
| MyMemory | 무료 | 불필요 | 하루 약 5만 자, 응답 약 1초 |
| 바이두 번역 | 무료 | 필요 | 더 안정적인 품질, 긴 텍스트 분할 지원 |
| DeepL | 월 50만 자 무료 | 필요 | 고품질, 30+ 개 언어, Pro 엔드포인트 지원 |
| AI LLM | 서비스별 | 서비스별 | OpenAI 호환 API; DeepSeek/Kimi/지푸/Ollama 프리셋 내장; **개발자 콘텐츠 보호**(코드/경로/URL 미번역) |

**바이두 무료 버전 개통 방법**: fanyi-api.baidu.com → 로그인 → 콘솔 → 앱 생성(일반 텍스트 번역/스탠다드)
→ APP ID와 시크릿 키 복사 → 트레이 메뉴 "서비스 전환" → 바이두 → 인증 정보 입력.

## 소스에서 빌드

[.NET SDK](https://dotnet.microsoft.com)(최근 버전 아무거나, 대상은 net48)가 필요합니다:

```
dotnet build src/csharp/TranslatorHost/TranslatorHost.csproj -c Release
```

빌드 결과는 `src/csharp/TranslatorHost/bin/Release/net48/win-x64/`에 생성됩니다. 해당 디렉터리 내용을
`bridge/`(`start-translator.bat`의 경로와 일치)에 복사하면 실행할 수 있습니다. 프런트엔드 페이지
(`src/webui/`, Vite + React 19 + TS)는 페이지 단위로 빌드되며, 결과물 `webui/dist/<page>.html`을 호스트가 자동으로 로드합니다.

### 디버그 스위치

```
translator-ui.exe --selftest            ; 헤드리스 셀프 테스트 (WebView2/설정 읽기·쓰기)
translator-ui.exe --open result,text    ; 디버그 창 열기 (60초 안전 타임아웃 후 자동 종료)
TFD_HEADLESS=1                          ; 트레이·단축키 없음 (샌드박스 e2e)
TFD_PIPE_NAME=<name>                    ; 인스턴스 격리 키 (병렬 테스트용)
TFD_TEST_REUSE=1                        ; 결과 창 재사용 플로우 자동 구동
```

## 아키텍처 (v1.7, 마이그레이션 완료)

- **C# 호스트**(`src/csharp/TranslatorHost`, net48 + WinForms + WebView2): 유일한 호스트——
  트레이, 전역 단축키(선택 + 스크린샷 번역), 선택 캡처(터미널 보호·UIA 직접 읽기 헬퍼 자식 프로세스),
  스크린샷 번역(오버레이 영역 선택 → `Windows.Media.Ocr`), 창 라이프사이클, WebView2, DWM 둥근 모서리, 네이티브 드래그.
  WinForms는 Windows 통합만 담당하며, 비즈니스 로직은 Form에 넣지 않습니다.
- **핵심 라이브러리**(`src/csharp/TranslatorCore`): 번역/프로바이더/설정/클립보드/HTTP/JSON,
  전부 async + CancellationToken.
- **UI 페이지**(`src/webui/`, React 19 + TypeScript): settings/result/capture/config/input 5페이지, 페이지 단위 싱글 파일 빌드;
  호스트와는 JSON 메시지 프로토콜로 통신(계약은 `docs/protocol.md`).
- AHK 버전과 마이그레이션 기간의 Named Pipe 브리지는 각각 v1.4/v1.5에서 폐기·삭제되었습니다(백업은 저장소에 미포함).

## 디렉터리 구조

```
translate-for-developers/
├── README.md
├── LICENSE
├── .gitignore
├── start-translator.bat        # 실행 진입점 (C# 호스트를 띄움)
├── docs/                       # architecture / protocol / known-issues
└── src/
    ├── csharp/                 # C# 호스트 + 핵심 라이브러리 + 셀프 테스트
    │   ├── TranslatorHost/     # WinForms/WebView2/트레이/단축키/선택 캡처
    │   ├── TranslatorCore/     # 번역/프로바이더/설정/클립보드 (클래스 라이브러리)
    │   ├── TranslatorCore.Tests/  # 내장 어설션 셀프 테스트 (dotnet run)
    │   └── build-bridge.ps1    # 빌드 + bridge/ 배포
    ├── webui/                  # React 19 + TS 프런트엔드 (페이지 단위 싱글 파일)
    │   ├── src/                # settings/result/capture/config + 브리지 프로토콜 계층
    │   └── dist/               # 빌드 결과물 <page>.html (자기완결형)
    ├── icon.ico
    └── WebView2Loader.dll
```

## 자주 묻는 질문

- **단축키가 반응하지 않음**: 트레이에 "T" 아이콘이 있는지 확인; 영어가 포함된 텍스트가 선택되어 있는지 확인
- **"선택된 텍스트를 감지하지 못했습니다" 표시**: 텍스트를 먼저 선택한 후 단축키를 누르세요 (일부 앱은 창을 한 번 클릭해 포커스를 맞춰야 함)
- **"네트워크 요청 실패" 표시**: 네트워크 확인; MyMemory는 가끔 시간 초과 —— 창의 "다시 시도" 클릭
- **번역 서비스를 바꾸고 싶음**: 트레이 메뉴 → 번역 서비스 전환
- **단축키가 다른 소프트웨어와 충돌**: 트레이 메뉴 → 단축키 변경 → 원하는 조합키를 직접 누르기
- **스크린샷 번역에서 "OCR 언어 팩 미설치" 안내**: Windows 설정 → 시간 및 언어 → 언어 및 지역 → 언어 추가 → "광학 문자 인식" 옵션 기능 체크 (Win10/11은 보통 중·영어 내장)
- **스크린샷 번역 인식률이 낮음**: OCR 언어는 "원본 언어" 설정을 따름 (auto = 시스템 언어). 영어 전용 내용은 원본 언어를 영어로. 너무 작은 글자(약 12px 미만)는 인식률이 떨어짐
- **번역 창 이동 방법**: 상단 제목표시줄(로고가 있는 줄)을 잡고 드래그
- **첫 실행 시 "WebView2를 찾을 수 없음"**: [마이크로소프트 공식 사이트](https://developer.microsoft.com/microsoft-edge/webview2/)에서 Evergreen Runtime을 한 번 설치 (일반적인 Win10/11에서는 불필요)
- **시작 시 자동 실행(선택)**: translator.exe 바로 가기를 `shell:startup`에 넣기

## 라이선스

[MIT](LICENSE)
