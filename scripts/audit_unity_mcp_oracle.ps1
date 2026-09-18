$ErrorActionPreference = 'Stop'
$auditRoot = Join-Path (Get-Location) 'output/unity-mcp-audit-2026-09-08'
$oracleOut = Join-Path $auditRoot 'oracle'
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.3.16f1/Editor/Data'
$runtimeDir = Join-Path $unityData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$compilerDir = Join-Path $unityData 'DotNetSdkRoslyn'
$source = Join-Path (Get-Location) 'Library/PackageCache/com.community.unity-mcp@85f6c175c082/Tools/verify/roslyn-oracle~/Program.cs'
New-Item -ItemType Directory -Path $oracleOut -Force | Out-Null
$globalUsings = 'global using System; global using System.IO; global using System.Linq; global using System.Collections.Generic;'
Set-Content -LiteralPath (Join-Path $oracleOut 'GlobalUsings.cs') -Value $globalUsings
$lines = @('-nologo','-target:exe','-nostdlib+','-langversion:latest',('-out:"' + (Join-Path $oracleOut 'oracle.dll') + '"'))
$lines += Get-ChildItem -LiteralPath $runtimeDir -Filter '*.dll' | ForEach-Object { try { [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null; '-r:"' + $_.FullName + '"' } catch { } }
$lines += '-r:"' + (Join-Path $compilerDir 'Microsoft.CodeAnalysis.dll') + '"'
$lines += '-r:"' + (Join-Path $compilerDir 'Microsoft.CodeAnalysis.CSharp.dll') + '"'
$lines += '"' + $source + '"'
$lines += '"' + (Join-Path $oracleOut 'GlobalUsings.cs') + '"'
$rsp = Join-Path $oracleOut 'build.rsp'
Set-Content -LiteralPath $rsp -Value $lines
& (Join-Path $unityData 'NetCoreRuntime/dotnet.exe') (Join-Path $compilerDir 'csc.dll') ('@' + $rsp)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath (Join-Path $compilerDir 'Microsoft.CodeAnalysis.dll') -Destination $oracleOut
Copy-Item -LiteralPath (Join-Path $compilerDir 'Microsoft.CodeAnalysis.CSharp.dll') -Destination $oracleOut
Copy-Item -LiteralPath (Join-Path $compilerDir 'csc.runtimeconfig.json') -Destination (Join-Path $oracleOut 'oracle.runtimeconfig.json')
& (Join-Path $unityData 'NetCoreRuntime/dotnet.exe') (Join-Path $oracleOut 'oracle.dll') (Get-Location).Path (Join-Path $auditRoot 'roslyn-ground-truth.json')
exit $LASTEXITCODE
