<#
.SYNOPSIS
  Sends a file to the dry-run endpoint and writes the report and the chunks to a text file, so they can be read.
.EXAMPLE
  .\scripts\dry-run.ps1 -File D:\RAG\DotNet\content\v3\whats-new-v3.md -Version v3 -Out .\dryrun-output\whats-new-v3.txt
.NOTES
  The app must be running (dotnet run --project src\RagSystem). Nothing is stored.
#>
param(
    [Parameter(Mandatory)] [string] $File,
    [Parameter(Mandatory)] [ValidateSet('v2', 'v3')] [string] $Version,
    [Parameter(Mandatory)] [string] $Out,
    [string] $BaseUrl = 'http://localhost:5289'
)

$ErrorActionPreference = 'Stop'
$json = curl.exe -s -S -X POST "$BaseUrl/api/documents?version=$Version&dryRun=true" -F "file=@$File"
if ($LASTEXITCODE -ne 0) { throw "curl failed with exit code $LASTEXITCODE" }
$report = $json | ConvertFrom-Json
if ($null -eq $report.chunks) { throw "The endpoint did not return a report: $json" }

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("File:    $($report.fileName)")
$lines.Add("Version: $($report.version)")
$lines.Add("Title:   $($report.title)")
$lines.Add("Valid:   $($report.valid)")
$lines.Add("Chunks:  $($report.chunks.Count)  (tokens: " + (($report.chunks | Measure-Object estTokens -Sum).Sum) + ")")
foreach ($e in $report.errors) { $lines.Add("ERROR   $($e.code): $($e.message) (line $($e.line))") }
foreach ($w in $report.warnings) { $lines.Add("WARNING $($w.code): $($w.message) (line $($w.line))") }
foreach ($c in $report.chunks) {
    $lines.Add('')
    $lines.Add("=================== chunk $($c.ordinal)  ($($c.estTokens) tokens) ===================")
    $lines.Add($c.text)
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
[System.IO.File]::WriteAllLines($Out, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $Out ($($report.chunks.Count) chunks)"
