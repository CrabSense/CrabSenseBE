# Local-only static UI for CrabSense API (this machine).
# API: http://localhost:5080  |  UI: http://localhost:5500
$ErrorActionPreference = "Stop"
$port = 5500
$root = $PSScriptRoot

Write-Host "Serving $root at http://localhost:$port"
Write-Host "Login: owner / Owner@123  (API must be running on :5080)"
Write-Host "Ctrl+C to stop."

# Prefer real Python (avoid WindowsApps stub)
$pyCmd = $null
foreach ($c in @(
  "$env:LOCALAPPDATA\Python\bin\python.exe",
  "python",
  "py"
)) {
  if ($c -like '*\*' -and (Test-Path $c)) { $pyCmd = $c; break }
  $found = Get-Command $c -ErrorAction SilentlyContinue
  if ($found -and $found.Source -notmatch 'WindowsApps') { $pyCmd = $found.Source; break }
}

if ($pyCmd) {
  Write-Host "Using $pyCmd"
  Set-Location $root
  & $pyCmd -m http.server $port
  exit $LASTEXITCODE
}

# Fallback: tiny HttpListener
Add-Type -AssemblyName System.Net.HttpListener
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$port/")
$listener.Start()
Write-Host "HttpListener started (no Python)."

function Get-ContentType([string]$path) {
  switch -Regex ($path) {
    '\.html$' { return 'text/html; charset=utf-8' }
    '\.js$'   { return 'application/javascript; charset=utf-8' }
    '\.css$'  { return 'text/css; charset=utf-8' }
    default   { return 'application/octet-stream' }
  }
}

try {
  while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $reqPath = $ctx.Request.Url.LocalPath.TrimStart('/')
    if ([string]::IsNullOrWhiteSpace($reqPath)) { $reqPath = 'index.html' }
    $file = Join-Path $root $reqPath
    if (-not (Test-Path $file) -or (Get-Item $file).PSIsContainer) {
      $ctx.Response.StatusCode = 404
      $bytes = [Text.Encoding]::UTF8.GetBytes('404')
    } else {
      $bytes = [IO.File]::ReadAllBytes($file)
      $ctx.Response.ContentType = Get-ContentType $file
      $ctx.Response.StatusCode = 200
    }
    $ctx.Response.ContentLength64 = $bytes.Length
    $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $ctx.Response.Close()
  }
} finally {
  $listener.Stop()
}
