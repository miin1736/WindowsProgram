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
- `HotkeyManager.cs`: `RegisterHotKey` P/Invoke, 숨김 메시지 윈도우의 `WM_HOTKEY` 수신. 등록 실패는 반환값으로 감지.
- `ScreenCapture.cs`: GDI BitBlt 캡처(규칙 C-14) + `BitmapSource` 변환.
- `RegionSelectionWindow.xaml(.cs)`: 영역 선택 오버레이. DIP → 물리 픽셀 변환 포함.
- `App.xaml.cs`: 전체 흐름 연결. `StartupUri` 없이 `OnExplicitShutdown`으로 창 없이 상주.

### 빌드/실행 (다른 로컬에서 재현 시)
```powershell
winget install Microsoft.DotNet.SDK.8
dotnet build src/SnapContext/SnapContext.csproj
src/SnapContext/bin/Debug/net8.0-windows/SnapContext.exe
```
트레이 아이콘이 아직 없어서 종료는 작업 관리자(또는 `Stop-Process -Name SnapContext`)로 해야 함.

### 알려진 한계 (PoC 단계라 의도적으로 미구현)
- 핫키는 Ctrl+Alt+S 고정. 등록 실패 시 MessageBox만 표시. **규칙 C-12상 재설정 UI/대체 키 제안 없이는 출시 불가.**
- 완료 알림은 임시 placeholder. 설명 입력, 숫자키 재사용, 태그 프리셋은 Week 2.
- 캡션 합성, 파일 저장/파일명 생성, 클립보드 히스토리·동기화 제외 플래그(규칙 B-10) 미구현. **B-10은 출시 전 필수.**
- DPI는 System DPI Aware 기준. 모니터별 배율이 다른 멀티 모니터는 오차 가능성이 있어 PMv2 전환 시 재검증 필요.
- 이번 테스트는 사용자 PC 1대 기준이며, 이 PC의 화면 배율(100%/125%/150% 등)은 기록하지 않았음.
- 클립보드에 쓰는 이미지 포맷이 ChatGPT/Claude/Gemini에서 이미지로 붙는지는 아직 미검증(이미지 단독 붙여넣기 대조군은 생략했음). Week 2 진행 중 함께 확인할 것.

### 다음에 이어서 할 일
1. Week 2: "입력 없이도 즉시 완료" 플로우 유지 + 비모달 설명 토스트(자동 소멸, Esc, 숫자키 1~5 재사용, 태그 프리셋).
2. 저장/파일명 시퀀스 확정(CLAUDE.md 11절): 타임스탬프 임시 파일명으로 즉시 저장 후 설명 입력 시 rename.
3. 캡션 합성(이미지 하단 텍스트 번인)의 폰트/레이아웃/가독성 세부 결정.
4. 클립보드 히스토리/클라우드 동기화 제외 플래그 구현(규칙 B-10).
5. 코드사이닝 인증서 비용/신청 조건 재확인(CLAUDE.md 11절 미해결 항목).

### 환경 관련 주의사항 (다른 로컬에서 재현 시 필독)
- **Windows PowerShell 5.1은 BOM 없는 UTF-8 `.ps1` 파일의 한글을 깨뜨림.** 시스템 기본 코드페이지로 오인식해서 문자열이 깨진 채로 실행됨(콘솔 출력뿐 아니라 실제 문자열 데이터 자체가 손상됨). 한글이 포함된 스크립트는 반드시 UTF-8 **BOM 포함**으로 저장할 것. 확인/수정 방법:
  ```powershell
  $content = Get-Content -Path <파일경로> -Raw -Encoding UTF8
  Set-Content -Path <파일경로> -Value $content -Encoding UTF8   # Windows PowerShell 5.1에서 BOM 포함으로 저장됨
  ```
- 테스트 시 클립보드에 올린 더미 이미지/텍스트는 실제 개인정보가 아닌 합성 테스트 데이터임.
- 사이트에 붙여넣기 후 **전송(Send) 버튼은 누르지 않는 것**이 원칙(관찰만 목적). 실수로 전송해도 더미 데이터라 문제는 없음.
