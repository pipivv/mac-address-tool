param([string]$Executable='dist\MacAddressTool.exe')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$textFiles=@('README.md','LICENSE','THIRD_PARTY_NOTICES.md','.gitignore')
foreach($directory in @('src','scripts','docs','.github')){
 $folder=Join-Path $project $directory
 if(Test-Path -LiteralPath $folder){$textFiles+=Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object {$_.Extension -in @('.cs','.xaml','.manifest','.ps1','.md','.yml','.yaml','.txt')} | ForEach-Object {$_.FullName.Substring($project.Length+1)}}
}
$patterns=@{
 'personal-path'=('(?i)(?:[A-Z]:\\Users\\|/Us'+'ers/|/ho'+'me/)[^\s"<>]+')
 'private-workspace'=('(?i)Documents[\\/]ChatGPT|AppData[\\/]Local[\\/]Temp|codex-'+'clipboard-')
 'private-email'='(?i)\b[A-Z0-9._%+-]+@(?:qq\.com|vip\.qq\.com|gmail\.com|outlook\.com|hotmail\.com|163\.com|126\.com)\b'
 'access-token'='\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9]{24,})\b'
 'private-key'='-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
}
$macPattern='(?i)(?<![0-9A-F])(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}(?![0-9A-F])|(?<=["''])(?:[0-9A-F]{12})(?=["''])'
$safeAddresses=@('020000000001','000000000000','FFFFFFFFFFFF','010000000001')
$findings=New-Object 'System.Collections.Generic.List[string]'
function Check-Text([string]$name,[string]$text){
 foreach($entry in $patterns.GetEnumerator()){if([regex]::IsMatch($text,$entry.Value)){$findings.Add($name+': '+$entry.Key)}}
 foreach($match in [regex]::Matches($text,$macPattern)){
  $normal=($match.Value -replace '[:-]','').ToUpperInvariant()
  if($normal -notin $safeAddresses){$findings.Add($name+': unexpected fixed MAC address')}
 }
}
foreach($relative in ($textFiles | Sort-Object -Unique)){
 $path=Join-Path $project $relative
 if(Test-Path -LiteralPath $path){Check-Text $relative ([IO.File]::ReadAllText($path))}
}
$binary=Join-Path $project $Executable
if(-not(Test-Path -LiteralPath $binary)){throw 'Build the release EXE before running the privacy check.'}
$bytes=[IO.File]::ReadAllBytes($binary)
Check-Text 'EXE UTF-8 strings' ([Text.Encoding]::UTF8.GetString($bytes))
Check-Text 'EXE UTF-16 strings' ([Text.Encoding]::Unicode.GetString($bytes))
if($bytes.Length -gt 1){Check-Text 'EXE UTF-16 offset strings' ([Text.Encoding]::Unicode.GetString($bytes,1,$bytes.Length-1))}
foreach($path in @(Get-ChildItem -LiteralPath (Join-Path $project 'docs') -Filter '*.png' -File)){
 $image=[IO.File]::ReadAllBytes($path.FullName);$offset=8
 while($offset+12 -le $image.Length){
  $length=([uint32]$image[$offset] -shl 24) -bor ([uint32]$image[$offset+1] -shl 16) -bor ([uint32]$image[$offset+2] -shl 8) -bor [uint32]$image[$offset+3]
  $type=[Text.Encoding]::ASCII.GetString($image,$offset+4,4)
  if($type -in @('tEXt','iTXt','zTXt','eXIf')){$findings.Add('Demo image: unexpected metadata chunk')}
  $offset+=12+$length
 }
}
if($findings.Count){$findings | Sort-Object -Unique | Write-Output;throw 'Privacy check failed. No matched secret or address values are printed.'}
[pscustomobject]@{Result='PASS';TextFiles=($textFiles | Sort-Object -Unique).Count;ExecutableBytes=$bytes.Length;SHA256=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash;DebugSymbols=(Get-ChildItem -LiteralPath (Split-Path $binary -Parent) -Filter '*.pdb' -File).Count;PersonalDataPatternsFound=0}
