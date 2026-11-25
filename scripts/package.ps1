$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'privacy-check.ps1')
$exe=Join-Path $project 'dist\MacAddressTool.exe'
$hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $project 'dist\SHA256SUMS.txt'),$hash+'  MacAddressTool.exe'+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
$files=@('README.md','LICENSE','THIRD_PARTY_NOTICES.md','.gitignore')
foreach($directory in @('src','scripts','data','docs')){
 $files+=Get-ChildItem -LiteralPath (Join-Path $project $directory) -Recurse -File | ForEach-Object {$_.FullName.Substring($project.Length+1)}
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=Join-Path $project 'dist\mac-address-tool-source.zip'
if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive -Force}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($relative in ($files | Sort-Object -Unique)){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $project $relative),($relative -replace '\\','/'),[IO.Compression.CompressionLevel]::Optimal) | Out-Null}}finally{$zip.Dispose()}
Get-Item -LiteralPath $archive,$exe | Select-Object Name,Length
