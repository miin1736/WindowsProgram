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

### 다음에 이어서 할 일
1. Week 1 로드맵의 나머지 축: 전역 핫키(`RegisterHotKey`) + GDI BitBlt 영역 캡처 PoC 코딩 착수.
2. 캡션 합성 기능(이미지 하단에 텍스트 번인) 구현 시 폰트/레이아웃/가독성 세부 사항 결정 필요.
3. 코드사이닝 인증서 비용/신청 조건 재확인(CLAUDE.md 11절 미해결 항목).

### 환경 관련 주의사항 (다른 로컬에서 재현 시 필독)
- **Windows PowerShell 5.1은 BOM 없는 UTF-8 `.ps1` 파일의 한글을 깨뜨림.** 시스템 기본 코드페이지로 오인식해서 문자열이 깨진 채로 실행됨(콘솔 출력뿐 아니라 실제 문자열 데이터 자체가 손상됨). 한글이 포함된 스크립트는 반드시 UTF-8 **BOM 포함**으로 저장할 것. 확인/수정 방법:
  ```powershell
  $content = Get-Content -Path <파일경로> -Raw -Encoding UTF8
  Set-Content -Path <파일경로> -Value $content -Encoding UTF8   # Windows PowerShell 5.1에서 BOM 포함으로 저장됨
  ```
- 테스트 시 클립보드에 올린 더미 이미지/텍스트는 실제 개인정보가 아닌 합성 테스트 데이터임.
- 사이트에 붙여넣기 후 **전송(Send) 버튼은 누르지 않는 것**이 원칙(관찰만 목적). 실수로 전송해도 더미 데이터라 문제는 없음.
