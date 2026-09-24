<#
SnapContext Week 1 PoC v2 - 클립보드 이미지+텍스트 동시 세팅 테스트 (개선판).
v1은 DataObject.SetData(Bitmap, ...)를 써서 브라우저가 이미지를 인식 못 했을 가능성이 있어,
DataObject 전용 메서드인 SetImage()/SetText()를 사용해 더 표준적인 클립보드 포맷(CF_DIB 등 포함)으로 재구성.
더미 데이터만 사용. 실제 개인정보/민감 캡처 없음.
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
$graphics.DrawString("SnapContext - TEST PATTERN v2", $titleFont, $blackBrush, 30, 40)
$graphics.DrawString("이 이미지는 더미 테스트용입니다.", $bodyFont, $grayBrush, 30, 100)
$graphics.DrawString("SetImage()/SetText() 방식으로 재테스트 중입니다.", $bodyFont, $grayBrush, 30, 130)
$graphics.DrawString("Week 1 PoC v2: 이미지+텍스트 동시 붙여넣기 검증용", $bodyFont, $grayBrush, 30, 160)

$graphics.Dispose()

# 2. 더미 설명 텍스트
$captionText = "[테스트 v2] 로그인 화면 에러 메시지 예시 - 2026-09-20"

# 3. DataObject 전용 메서드(SetImage/SetText) 사용 -> CF_BITMAP + CF_DIB + 텍스트 포맷을 함께 등록
$dataObject = New-Object System.Windows.Forms.DataObject
$dataObject.SetImage($bitmap)
$dataObject.SetText($captionText)

[System.Windows.Forms.Clipboard]::SetDataObject($dataObject, $true)

# 4. 참고용 이미지 저장
$outPath = Join-Path $PSScriptRoot "test_pattern_v2.png"
$bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)

Write-Host "===================================================="
Write-Host "클립보드 설정 완료 (v2 - SetImage/SetText 방식):"
Write-Host " - 이미지: 600x350 더미 테스트 패턴"
Write-Host " - 텍스트: `"$captionText`""
Write-Host " - 참고 이미지 저장 위치: $outPath"
Write-Host "===================================================="
Write-Host "ChatGPT 채팅 입력창을 클릭하고 Ctrl+V 만 눌러보세요 (전송은 누르지 마세요)."

