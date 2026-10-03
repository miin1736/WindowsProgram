# SnapContext 진행 로그

> 이 파일은 세션 간/기기 간 작업 연속성을 위한 살아있는 로그입니다. 안정적인 스펙·규칙은 [CLAUDE.md](CLAUDE.md)에 있고, 이 파일은 "지금까지 뭘 했고 다음에 뭘 할지"를 추적합니다. 새 기기에서 Claude Code를 열면 CLAUDE.md가 자동 로드되지만, 이 파일은 자동 로드되지 않으니 새 세션에서 반드시 먼저 읽고 이어서 진행하세요.

## 확정된 결정
- 배포 범위: 공개 배포(일반 대중/상업화) 확정 (2026-09-20). 상세는 [CLAUDE.md 12절](CLAUDE.md) 참고.
- Phase 1 설명 전달 기본값: **캡션 합성**(이미지에 텍스트 번인) 확정, "동시 붙여넣기 자동 병합"은 채택 안 함 (2026-09-27). 상세는 [CLAUDE.md 12절](CLAUDE.md) 참고.

## 현재 단계: Week 1 PoC — LLM 붙여넣기 실동작 검증 (규칙 D-15)

CLAUDE.md 규칙 15("Week 1에 ChatGPT/Claude/Gemini 각각에서 이미지+텍스트 붙여넣기 실동작을 먼저 검증")를 진행 중.

### 테스트 방법
`research/week1-paste-test/`의 PowerShell 스크립트로, 향후 WPF 앱이 `Clipboard.SetDataObject`로 만들 클립보드 상태(이미지+텍스트 동시)를 재현한 뒤, 실제 웹사이트 채팅창에 Ctrl+V 한 번으로 무엇이 붙는지 관찰.

- `set_test_clipboard_v1.ps1`: `DataObject.SetData(DataFormats.Bitmap, ...)` + `SetData(DataFormats.UnicodeText, ...)` 방식
- `set_test_clipboard_v2.ps1`: `DataObject.SetImage(...)` + `SetText(...)` 전용 메서드 방식 (v1이 이미지 미인식 문제일 경우를 대비한 재시도)

### 결과 (2026-09-24 기준)

| 사이트 | 스크립트 | 결과 |
|---|---|---|
| ChatGPT | v1 (SetData 방식) | 텍스트만 붙음, 이미지 미첨부 |
| ChatGPT | v2 (SetImage/SetText 방식) | 텍스트만 붙음, 이미지 미첨부 (v1과 동일) |
| Claude | - | 미실시 |
| Gemini | - | 미실시 |

### 결론 (2026-09-27)
ChatGPT 실측 결과(2회, 서로 다른 클립보드 구성 방식) 모두 텍스트만 붙고 이미지가 탈락 → "이미지+텍스트 동시 붙여넣기"는 채택하지 않기로 결정. **Phase 1 기본값을 "캡션 합성"으로 확정**(CLAUDE.md 3절/규칙 D-15/12절 참고). 대조군 테스트(이미지 단독)와 Claude/Gemini 개별 검증은 이 결정에 필수 전제가 아니라고 판단해 진행하지 않음 — 캡션 합성은 클립보드에 이미지 포맷 하나만 올리므로 사이트별 paste 구현 차이와 무관하게 동작하기 때문.

## Week 1 PoC 코드: 전역 핫키 + 영역 캡처 + 클립보드 (2026-10-03 사용자 실기 테스트 통과)

`src/SnapContext/` (C# / .NET 8 / WPF). Ctrl+Alt+S → 가상 화면 전체 오버레이에서 드래그 선택 → GDI `CopyFromScreen` 캡처 → `Clipboard.SetImage` 즉시 반영 → 우측 하단에 포커스를 뺏지 않는 "캡처 완료" 알림이 2.5초 후 자동 소멸.

사용자 실기 테스트 결과(전부 정상): 핫키 동작, 오버레이/십자 커서, 드래그 박스, 완료 알림 자동 소멸, 붙여넣기 시 선택 영역과 캡처 결과 일치, Esc 취소.

### 구성
- `HotkeyManager.cs`: `RegisterHotKey` P/Invoke, 숨김 메시지 윈도우의 `WM_HOTKEY` 수신. 여러 핫키를 동시에 등록/해제하며, 등록 실패는 반환값(0)으로 감지.
- `ScreenCapture.cs`: GDI BitBlt 캡처(규칙 C-14) + `BitmapSource` 변환.
- `RegionSelectionWindow.xaml(.cs)`: 영역 선택 오버레이. DIP → 물리 픽셀 변환 포함.
- `App.xaml.cs`: 전체 흐름 연결. `StartupUri` 없이 `OnExplicitShutdown`으로 창 없이 상주. 캡처 중 재진입 방지.
- (Week 2) `CaptureToastWindow.xaml(.cs)`: 캡처 직후 우하단 비모달 토스트. 태그 칩, 최근 설명 칩, 직접 입력창.
- (Week 2) `CaptionRenderer.cs`: 이미지 하단에 설명을 캡션으로 번인한 새 이미지 생성(1차 안).
- (Week 2) `ClipboardHelper.cs`: 모든 클립보드 쓰기의 단일 경로. B-10 제외 플래그 적용 + 잠금 시 재시도.
- (Week 2) `RecentDescriptionStore.cs`: 최근 설명 5개를 `%APPDATA%\SnapContext\recent_descriptions.json`에 보관.

### 빌드/실행 (다른 로컬에서 재현 시)
```powershell
winget install Microsoft.DotNet.SDK.8
dotnet build src/SnapContext/SnapContext.csproj
src/SnapContext/bin/Debug/net8.0-windows/SnapContext.exe
```
트레이 아이콘이 아직 없어서 종료는 작업 관리자(또는 `Stop-Process -Name SnapContext`)로 해야 함.

## Week 2: 설명 토스트 (2026-10-03 구현, 자동 검증 통과 / 실기 테스트 대기)

캡처 직후 클립보드에 원본 이미지를 먼저 올리고(A-1), 우하단에 포커스를 뺏지 않는 토스트를 띄운다. 설명을 고르거나 입력하면 캡션을 번인한 이미지로 클립보드를 **교체**한다. 아무것도 안 하면 약 4초 뒤 자동으로 사라지고 원본 이미지가 그대로 남는다.

### 동작 요약
- 토스트는 `WS_EX_NOACTIVATE`로 떠서 표시 중/칩 클릭 시 다른 앱의 포커스를 뺏지 않음. **입력창을 직접 클릭했을 때만** 활성화되어 타이핑 가능.
- 태그 칩(버그 / UI 검토 / 에러 로그)과 최근 설명 칩(최대 5개)은 클릭 즉시 반영. 태그를 누를 때 입력창에 글이 있으면 `태그: 입력` 형태로 반영.
- 최근 설명은 Ctrl+Alt+1~5로도 반영(토스트가 떠 있는 동안에만 등록, 닫히면 해제).
- 마우스를 올려두거나 입력창에 포커스가 있으면 카운트다운 정지. 입력창에 글을 쓴 채 다른 곳을 클릭해 비활성화되면 그 글을 반영하고 닫음.
- 반영/실패 결과를 토스트에 잠깐 표시. 반영 실패 시 원본 이미지는 클립보드에 그대로 있음.
- 클립보드 쓰기는 전부 `ClipboardHelper`를 거치며 `CanIncludeInClipboardHistory`/`CanUploadToCloudClipboard`=0 플래그가 항상 붙음(규칙 B-10). 다른 프로세스가 클립보드를 잡고 있으면 6회 재시도.

### 자동 검증 결과 (임시 하니스, 38개 항목 전부 통과)
캡션 합성(폭 유지, 원본 픽셀 보존, 좁은 이미지, 4K 처리 18ms), 클립보드(이미지 + 두 제외 플래그가 DWORD 0으로 존재, 이미지 복원), 최근 설명 저장소(5개 유지, 중복 이동, 한글 저장, 손상 파일 복구), 토스트(포커스 미탈취, NOACTIVATE, 칩 클릭, 입력+Enter, 태그+입력, Esc, 약 4초 자동 소멸, 복사 실패/반영 실패 표시), Ctrl+Alt+2 전역 핫키 동작과 해제.

### 사용자 실기 테스트 결과 (2026-10-03)
- 한글 IME로 입력한 뒤 **Enter 한 번**으로 반영됨(두 번 필요하지 않음).
- 마우스를 토스트 위에 올려두면 사라지지 않음.
- Win+V(클립보드 기록을 켠 상태): Win+Shift+S로 복사한 이미지는 기록에 남고, **SnapContext 캡처는 남지 않음** → 규칙 B-10 동작 확인.
- 붙여넣기: 처음에는 "동작하지 않는다"고 보고되었으나 원인을 특정하지 못했고, 이후 사용자가 캡션 띠가 포함된 이미지가 정상적으로 붙는 것을 확인함. 어느 사이트/브라우저에서 확인했는지는 기록되지 않았음(ChatGPT/Claude/Gemini 각각의 확인은 아직 미완).

### 자동 붙여넣기 검증 (Chromium 계열 Edge, 2026-10-03)
Edge 창에 실제 Ctrl+V를 보내는 테스트 페이지로 확인(임시 스크립트, 저장소 미포함). 클립보드 쓰기 4가지 방식(Week 1 `Clipboard.SetImage`, 현재 `ClipboardHelper`, 플래그 없는 `SetDataObject`, 캡션 합성) **모두 `image/png` 파일 1개로 인식**됨. 실제 앱을 가상 핫키·마우스 드래그로 구동했을 때도 캡처만 한 경우 300x200, 캡션 반영(Ctrl+Alt+1) 시 300x245 이미지가 정상으로 붙음. 클립보드에 올라가는 형식은 `CF_BITMAP`/`CF_DIB`/`CF_DIBV5`와 제외 플래그 두 개.
- 이로써 1주차에 미뤄 둔 "이미지 단독 붙여넣기" 대조군이 Chromium에서 확인됨(다만 ChatGPT/Claude/Gemini 사이트 자체의 동작은 별개).
- 첫 자동 실행에서만 예상과 다른 크기(2729x313)의 이미지가 한 번 붙었고, 워밍업 후 재실행에서는 재현되지 않음. 첫 캡처 시 드래그 타이밍이 어긋났거나 사용자가 그 사이 따로 복사한 이미지였을 가능성이 있으나 확정하지 못함.

### 아직 사람이 확인해야 하는 것
- ChatGPT / Claude / Gemini 각각에서 캡션 포함 이미지가 첨부되는지.
- 이 PC의 화면 배율이 100%가 아닌 환경과 모니터 2대 환경에서 캡처 영역이 정확한지.

### 알려진 한계
- 핫키는 Ctrl+Alt+S 고정. 등록 실패 시 MessageBox만 표시. **규칙 C-12상 재설정 UI/대체 키 제안 없이는 출시 불가.**
- **Esc는 토스트가 활성화된 상태(입력창 클릭 후)에서만 닫힘.** 비활성 상태에서는 ✕ 버튼 또는 자동 소멸. 규칙 A-3의 "Esc 즉시 닫기"와 차이가 있어 CLAUDE.md 12절에 잠정 결정으로 기록함.
- 숫자키는 맨 숫자 대신 Ctrl+Alt+1~5 (CLAUDE.md 12절 참고).
- 캡션 디자인은 1차 안(어두운 띠 + 흰 글씨 + 파란 선, 글자 크기는 폭에 비례). GDI+가 한글을 글자 단위로 줄바꿈해 어절 중간에서 끊길 수 있음.
- 설명 최대 200자.
- 파일 저장/파일명 생성은 아직 없음(CLAUDE.md 11절 시퀀스 확정 후 구현). 캡처는 지금 클립보드에만 존재.
- 토스트는 주 모니터 우하단에만 뜸(캡처한 모니터와 다를 수 있음).
- DPI는 System DPI Aware 기준. 모니터별 배율이 다른 멀티 모니터는 오차 가능성이 있어 PMv2 전환 시 재검증 필요.
- 실기 테스트는 사용자 PC 1대 기준이며, 이 PC의 화면 배율은 기록하지 않았음.
- 설명 텍스트는 `%APPDATA%\SnapContext\recent_descriptions.json`에 평문으로 저장됨(개인정보가 들어갈 수 있으므로 설정 화면의 "기록 지우기"를 Week 4에 고려).

### 다음에 이어서 할 일
1. 캡션 디자인에 대한 사용자 피드백 확인, 토스트 Esc 동작(CLAUDE.md 12절 잠정 결정)에 대한 사용자 결정 받기.
2. 저장/파일명 시퀀스 확정(CLAUDE.md 11절): 타임스탬프 임시 파일명으로 즉시 저장 후 설명 입력 시 rename. 캡처를 디스크에 저장할지 자체가 사용자 결정 사항(개인정보).
3. 캡션 디자인 피드백 반영, "순차 붙여넣기"용 "설명 복사" 단축키/버튼(Week 3).
4. 핫키 재설정 UI와 설정 화면, 트레이 상주(Week 4).
5. 코드사이닝 인증서 비용/신청 조건 재확인(CLAUDE.md 11절 미해결 항목).

### 환경 관련 주의사항 (다른 로컬에서 재현 시 필독)
- **Windows PowerShell 5.1은 BOM 없는 UTF-8 `.ps1` 파일의 한글을 깨뜨림.** 시스템 기본 코드페이지로 오인식해서 문자열이 깨진 채로 실행됨(콘솔 출력뿐 아니라 실제 문자열 데이터 자체가 손상됨). 한글이 포함된 스크립트는 반드시 UTF-8 **BOM 포함**으로 저장할 것. 확인/수정 방법:
  ```powershell
  $content = Get-Content -Path <파일경로> -Raw -Encoding UTF8
  Set-Content -Path <파일경로> -Value $content -Encoding UTF8   # Windows PowerShell 5.1에서 BOM 포함으로 저장됨
  ```
- 테스트 시 클립보드에 올린 더미 이미지/텍스트는 실제 개인정보가 아닌 합성 테스트 데이터임.
- 사이트에 붙여넣기 후 **전송(Send) 버튼은 누르지 않는 것**이 원칙(관찰만 목적). 실수로 전송해도 더미 데이터라 문제는 없음.
