param()

$patterns = 'TODO','FIXME','FUTURE IMPLEMENTATION','FUTURE','NEXT STEPS','OPTIONAL ENHANCEMENTS'
$files = git ls-files | Where-Object { $_ -match '\.(cs|ts|tsx|md|yml|yaml|json)$' }
$hits = @()
foreach ($f in $files) {
  $content = Get-Content -Raw $f
  foreach ($p in $patterns) {
    if ($content -match [regex]::Escape($p)) { $hits += $f }
  }
}
if ($hits.Count -gt 0) {
  Write-Error "Placeholder markers found:`n$($hits | Sort-Object -Unique -join "`n")"
  exit 1
} else {
  Write-Host "No placeholders found."
}
