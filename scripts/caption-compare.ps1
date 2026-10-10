<#
캡션 합성 vs 글자 입력 비교 시험용 클립보드 도우미(research/caption-compare-test 이미지 사용).
번호를 입력하면 해당 이미지/글자를 클립보드에 올린다. 더미 데이터만 사용하며 외부로 전송하지 않는다.
#>
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$dir = Join-Path $PSScriptRoot "..\research\caption-compare-test"
$items = @(
  @{ Key = "1"; Kind = "image"; File = "s1_설명합성.png"; Label = "[시나리오1] 방법 A - 설명이 합성된 이미지 (붙여넣기 1번이면 끝)" },
  @{ Key = "2"; Kind = "image"; File = "s1_원본.png";     Label = "[시나리오1] 방법 B - 1단계: 원본 이미지" },
  @{ Key = "3"; Kind = "text";  File = "s1_설명글자.txt";  Label = "[시나리오1] 방법 B - 2단계: 설명 글자" },
  @{ Key = "4"; Kind = "image"; File = "s2_설명합성.png"; Label = "[시나리오2] 방법 A - 설명이 합성된 이미지 (붙여넣기 1번이면 끝)" },
  @{ Key = "5"; Kind = "image"; File = "s2_원본.png";     Label = "[시나리오2] 방법 B - 1단계: 원본 이미지" },
  @{ Key = "6"; Kind = "text";  File = "s2_설명글자.txt";  Label = "[시나리오2] 방법 B - 2단계: 설명 글자" },
  @{ Key = "7"; Kind = "image"; File = "s3_설명합성.png"; Label = "[시나리오3] 방법 A - 설명이 합성된 이미지 (붙여넣기 1번이면 끝)" },
  @{ Key = "8"; Kind = "image"; File = "s3_원본.png";     Label = "[시나리오3] 방법 B - 1단계: 원본 이미지" },
  @{ Key = "9"; Kind = "text";  File = "s3_설명글자.txt";  Label = "[시나리오3] 방법 B - 2단계: 설명 글자" }
)

while ($true) {
  Write-Host ""
  Write-Host "=== 번호를 입력하면 클립보드에 올라갑니다. 그다음 채팅창에서 Ctrl+V ==="
  foreach ($it in $items) { Write-Host ("  {0}) {1}" -f $it.Key, $it.Label) }
  Write-Host "  q) 끝내기"
  $choice = Read-Host "번호"
  if ($choice -eq "q") { break }
  $it = $items | Where-Object { $_.Key -eq $choice }
  if (-not $it) { Write-Host "없는 번호입니다."; continue }
  $path = Join-Path $dir $it.File
  if ($it.Kind -eq "image") {
    $src = [System.Drawing.Image]::FromFile($path)
    $bmp = New-Object System.Drawing.Bitmap($src)
    $src.Dispose()
    [System.Windows.Forms.Clipboard]::SetImage($bmp)
    Write-Host ("-> 이미지를 클립보드에 올렸습니다: {0}" -f $it.File)
  } else {
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    [System.Windows.Forms.Clipboard]::SetText($text)
    Write-Host ("-> 글자를 클립보드에 올렸습니다: {0}" -f $text)
  }
}
