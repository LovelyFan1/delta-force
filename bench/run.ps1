<#
  bench/run.ps1 —— 靶场入口：哈希校验 + 基础 PE 信息 + 字符串快照
  只读样本，不改原文件。产物写 -Out 目录。
#>
param(
  [Parameter(Mandatory=$true)][string]$Target,
  [string]$Out = "out"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$t = Get-Item -LiteralPath $Target
$sha = (Get-FileHash -LiteralPath $t.FullName -Algorithm SHA256).Hash

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot "targets.json") -Raw | ConvertFrom-Json
$known = $manifest.targets | Where-Object { $_.sha256 -eq $sha }
if (-not $known) { Write-Warning "哈希不在 targets.json 清单内：$sha" } else { Write-Host "命中样本：$($known.id)" }

# PE 头
$fs = [IO.File]::OpenRead($t.FullName)
$br = New-Object IO.BinaryReader($fs)
$null = $fs.Seek(0x3C, 'Begin'); $peOff = $br.ReadInt32()
$null = $fs.Seek($peOff, 'Begin'); $sig = $br.ReadUInt32(); $machine = $br.ReadUInt16(); $sections = $br.ReadUInt16()
$null = $fs.Seek($peOff + 24, 'Begin'); $magic = $br.ReadUInt16()
$null = $fs.Seek($peOff + 24 + 68, 'Begin'); $subsys = $br.ReadUInt16()
$fs.Close()

$report = [ordered]@{
  file        = $t.Name
  size        = $t.Length
  sha256      = $sha
  peSignature = ('0x{0:X}' -f $sig)
  machine     = ('0x{0:X4}' -f $machine)
  bitness     = if ($magic -eq 0x20b) { "PE32+ (x64)" } else { "PE32 (x86)" }
  subsystem   = switch ($subsys) { 2 {"GUI"} 3 {"Console"} default {"$subsys"} }
  sections    = $sections
}
$report | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $Out "pe.json") -Encoding UTF8
$report.GetEnumerator() | ForEach-Object { "{0,-12} {1}" -f $_.Key, $_.Value }

# 字符串快照（只取前 8 MB，够看导入/参数）
$bytes = [IO.File]::ReadAllBytes($t.FullName)
$take = [Math]::Min(8MB, $bytes.Length)
$enc = [Text.Encoding]::GetEncoding(28591)
$text = $enc.GetString($bytes, 0, $take)
[regex]::Matches($text, '[ -~]{6,80}') | ForEach-Object { $_.Value } |
  Select-Object -Unique | Set-Content -LiteralPath (Join-Path $Out "strings.txt") -Encoding UTF8

Write-Host "产物：$Out\pe.json, $Out\strings.txt"
