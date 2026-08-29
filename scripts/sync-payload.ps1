param(
  [Parameter(Mandatory = $true)][string]$Source,
  [Parameter(Mandatory = $true)][string]$Dest
)

if (-not (Test-Path -LiteralPath (Join-Path $Source 'startup\NxWebUI.men'))) {
  Write-Host "skip payload: missing $Source"
  exit 0
}

New-Item -ItemType Directory -Force -Path $Dest | Out-Null
foreach ($folder in @('startup', 'application')) {
  $from = Join-Path $Source $folder
  $to = Join-Path $Dest $folder
  if (-not (Test-Path -LiteralPath $from)) { continue }
  robocopy $from $to /E /NFL /NDL /NJH /NJS /nc /ns /np `
    /XF *.pdb radial-slots.json custom_dirs.dat | Out-Null
  $code = $LASTEXITCODE
  if ($code -ge 8) { throw "robocopy failed ($code) $from -> $to" }
}
Write-Host "payload synced $Source -> $Dest"
exit 0
