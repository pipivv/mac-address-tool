param([string]$OutputName='MacAddressTool.exe')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
if(-not(Test-Path -LiteralPath $compiler)){throw '.NET Framework 4.x compiler is required.'}
$output=Join-Path $project 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $project
try {
 $sources=@(Get-ChildItem -LiteralPath '.\src' -Filter '*.cs' | ForEach-Object {'src\'+$_.Name})
 & $compiler /nologo /target:winexe /platform:x64 /optimize+ /debug- /codepage:65001 /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Xaml.dll "/r:$wpf\WindowsBase.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\PresentationFramework.dll" /win32manifest:src\app.manifest /win32icon:src\app.ico /resource:src\app.ico,app.ico /resource:data\vendors.tsv.gz,vendors.tsv.gz /resource:src\MainWindow.xaml,MainWindow.xaml "/out:dist\$OutputName" $sources
 if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
} finally {Pop-Location}
Get-Item -LiteralPath (Join-Path $output $OutputName) | Select-Object Name,Length
