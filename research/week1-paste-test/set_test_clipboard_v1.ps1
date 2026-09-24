<#
SnapContext Week 1 PoC - 클립보드 이미지+텍스트 동시 세팅 테스트용 스크립트.
더미 데이터만 사용. 실제 개인정보/민감 캡처 없음.
목적: 미래 WPF 앱이 Clipboard.SetDataObject로 만들 클립보드 상태를 재현해,
ChatGPT/Claude/Gemini 채팅창에서 "이미지+텍스트 동시 붙여넣기"가 실제로 어떻게 처리되는지 확인.
#>

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# 1. 더미 테스트 이미지 생성 (실제 캡처가 아닌 합성 이미지)
$bitmap = New-Object System.Drawing.Bitmap(600, 350)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::White)

$titleFont = New-Object System.Drawing.Font("Segoe UI", 22, [System.Drawing.FontStyle]::Bold)
$bodyFont = New-Object System.Drawing.Font("Segoe UI", 14)
$blackBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Black)
$grayBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::DimGray)
$redPen = New-Object System.Drawing.Pen([System.Drawing.Color]::Crimson, 4)

$graphics.DrawRectangle($redPen, 8, 8, 584, 334)
$graphics.DrawString("SnapContext - TEST PATTERN", $titleFont, $blackBrush, 30, 40)
$graphics.DrawString("이 이미지는 더미 테스트용입니다.", $bodyFont, $grayBrush, 30, 100)
$graphics.DrawString("실제 개인정보나 민감 캡처가 아닙니다.", $bodyFont, $grayBrush, 30, 130)
$graphics.DrawString("Week 1 PoC: 이미지+텍스트 동시 붙여넣기 검증용", $bodyFont, $grayBrush, 30, 160)

$graphics.Dispose()

# 2. 더미 설명 텍스트 (실제 캡션 시나리오를 모사)
$captionText = "[테스트] 로그인 화면 에러 메시지 예시 - 2026-09-20"

# 3. 하나의 DataObject에 Bitmap + UnicodeText를 동시에 담아 클립보드에 설정
#    (미래 WPF 앱의 Clipboard.SetDataObject 호출과 동일한 방식)
$dataObject = New-Object System.Windows.Forms.DataObject
$dataObject.SetData([System.Windows.Forms.DataFormats]::Bitmap, $true, $bitmap)
$dataObject.SetData([System.Windows.Forms.DataFormats]::UnicodeText, $true, $captionText)

[System.Windows.Forms.Clipboard]::SetDataObject($dataObject, $true)

# 4. 참고용으로 로컬에도 이미지 저장 (스크린샷으로 확인 가능하게)
$outPath = Join-Path $PSScriptRoot "test_pattern.png"
$bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Host "===================================================="
Write-Host "클립보드 설정 완료:"
Write-Host " - 이미지: 600x350 더미 테스트 패턴 (Bitmap 포맷)"
Write-Host " - 텍스트: `"$captionText`" (UnicodeText 포맷)"
Write-Host " - 참고 이미지 저장 위치: $outPath"
Write-Host "===================================================="
Write-Host "이제 ChatGPT / Claude / Gemini 채팅 입력창에서 Ctrl+V로 붙여넣어보세요."

