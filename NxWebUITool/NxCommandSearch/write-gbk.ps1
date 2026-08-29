$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$enc = [System.Text.Encoding]::GetEncoding(936)
$utf8 = New-Object System.Text.UTF8Encoding $false
$pairs = @(
  @{ src = Join-Path $root "plugin\startup\NxWebUI.men"; dst = Join-Path $root "deploy\startup\NxWebUI.men" },
  @{ src = Join-Path $root "plugin\startup\NxWebUI.btn"; dst = Join-Path $root "deploy\startup\NxWebUI.btn" },
  @{ src = Join-Path $root "plugin\application\profiles\All\NxWebUI.rtb"; dst = Join-Path $root "deploy\application\profiles\All\NxWebUI.rtb" }
)
foreach ($p in $pairs) {
  if (-not (Test-Path -LiteralPath $p.src)) { Write-Host "skip missing $($p.src)"; continue }
  $dir = Split-Path $p.dst -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
  $text = [IO.File]::ReadAllText($p.src, $utf8)
  [IO.File]::WriteAllText($p.dst, $text, $enc)
  Write-Host "gbk $($p.dst)"
}
