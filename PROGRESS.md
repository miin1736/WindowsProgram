# SnapContext 진행 로그

> 이 파일은 세션 간/기기 간 작업 연속성을 위한 살아있는 로그입니다. 안정적인 스펙·규칙은 [CLAUDE.md](CLAUDE.md)에 있고, 이 파일은 "지금까지 뭘 했고 다음에 뭘 할지"를 추적합니다. 새 기기에서 Claude Code를 열면 CLAUDE.md가 자동 로드되지만, 이 파일은 자동 로드되지 않으니 새 세션에서 반드시 먼저 읽고 이어서 진행하세요.

## 확정된 결정
- 배포 범위: 공개 배포(일반 대중/상업화) 확정 (2026-09-20). 상세는 [CLAUDE.md 12절](CLAUDE.md) 참고.

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

### 미해결 — 다음에 이어서 할 일
1. **대조군 테스트 필요**: 텍스트 없이 **이미지만 단독으로** 클립보드에 올렸을 때 ChatGPT가 이미지로 인식하는지 확인 (v3 스크립트 예정). 이걸로 "ChatGPT가 텍스트+이미지 동시 처리를 못 하는 것"인지 "테스트용 합성 이미지 포맷 자체를 인식 못 하는 것"인지 구분.
2. Claude(claude.ai), Gemini(gemini.google.com)에서도 동일한 v1/v2/v3 테스트 반복.
3. 결과 종합 후 Phase 1 기본 UX(순차 붙여넣기 vs 캡션 합성 중 무엇을 기본값으로 할지, CLAUDE.md 3절/9-2절/D-15) 확정.

### 환경 관련 주의사항 (다른 로컬에서 재현 시 필독)
- **Windows PowerShell 5.1은 BOM 없는 UTF-8 `.ps1` 파일의 한글을 깨뜨림.** 시스템 기본 코드페이지로 오인식해서 문자열이 깨진 채로 실행됨(콘솔 출력뿐 아니라 실제 문자열 데이터 자체가 손상됨). 한글이 포함된 스크립트는 반드시 UTF-8 **BOM 포함**으로 저장할 것. 확인/수정 방법:
  ```powershell
  $content = Get-Content -Path <파일경로> -Raw -Encoding UTF8
  Set-Content -Path <파일경로> -Value $content -Encoding UTF8   # Windows PowerShell 5.1에서 BOM 포함으로 저장됨
  ```
- 테스트 시 클립보드에 올린 더미 이미지/텍스트는 실제 개인정보가 아닌 합성 테스트 데이터임.
- 사이트에 붙여넣기 후 **전송(Send) 버튼은 누르지 않는 것**이 원칙(관찰만 목적). 실수로 전송해도 더미 데이터라 문제는 없음.
