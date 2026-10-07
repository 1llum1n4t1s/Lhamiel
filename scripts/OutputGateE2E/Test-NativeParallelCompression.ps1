param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$ArtifactDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ArtifactDirectory); if(Test-Path $root){throw 'Unused root required'}
New-Item -ItemType Directory -Path $root | Out-Null
# 失敗モード: 異なる圧縮ファイルまで待機する、後発が先行完了を待つ、token残存、ZIP内容破損。
$firstSource=Join-Path $root 'first.bin'; $secondSource=Join-Path $root 'second.txt'
$buffer=New-Object byte[] (4MB); [Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
$stream=[IO.File]::Create($firstSource)
try{for($i=0;$i -lt 64;$i++){$stream.Write($buffer)}}finally{$stream.Dispose()}
[IO.File]::WriteAllText($secondSource,'parallel canary')
$settings=@{CompressionFormat='ZIP';CompressionOutputToSameDirectory=$true;OpenCompressionOutputFolder=$false;Check4UpdatesOnStartup=$false;IsPasswordProtectionEnabled=$false;CompressionLevel='Ultra'}
$owned=[Collections.Generic.List[Diagnostics.Process]]::new();$tokens=[Collections.Generic.List[string]]::new()
function Launch([string]$Source){
 $token=[guid]::NewGuid().ToString('N');$path=Join-Path $env:TEMP ('Lhamiel-compression-request-'+$token+'.json');$tokens.Add($path)
 $request=@{SourcePaths=@($Source);CompressionFormat='ZIP';Settings=$settings;EncryptFileNames=$false;ProcessAsBatch=$false;IsExtraction=$false}
 $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($request|ConvertTo-Json -Depth 15));$s=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
 try{$s.Write($bytes);$s.Flush($true)}finally{$s.Dispose()}
 $info=[Diagnostics.ProcessStartInfo]::new($AppPath);$info.UseShellExecute=$false;$info.CreateNoWindow=$true
 foreach($arg in @('--compression-worker','--compression-request',$token)){$info.ArgumentList.Add($arg)}
 $p=[Diagnostics.Process]::Start($info);$owned.Add($p);return $p
}
function Read-Shared([string]$Path){$s=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite);$r=[IO.StreamReader]::new($s);try{$r.ReadToEnd()}finally{$r.Dispose();$s.Dispose()}}
try{
 $first=Launch $firstSource;$started=Get-Date;$firstLog=Join-Path "$env:LOCALAPPDATA/Lhamiel" ("Lhamiel_compression_"+$first.Id+"_"+(Get-Date).ToString('yyyyMMdd')+".log")
 while(!(Test-Path $firstLog) -or !(Read-Shared $firstLog).Contains('ArchiveCompressor.CompressFilesAsyncを呼び出し')){if($first.HasExited -or ((Get-Date)-$started).TotalSeconds -gt 25){throw 'First did not begin compression'};Start-Sleep -Milliseconds 40}
 $second=Launch $secondSource
 if(!$second.WaitForExit(45000)){throw 'Second timed out'}
 $overlap=!$first.HasExited
 if(!$first.WaitForExit(60000) -or $first.ExitCode -ne 0 -or $second.ExitCode -ne 0){throw 'Workers failed'}
 $contents=foreach($pair in @(@{source=$firstSource;zip=(Join-Path $root 'first.zip');entry='first.bin'},@{source=$secondSource;zip=(Join-Path $root 'second.zip');entry='second.txt'})){
  $zip=[IO.Compression.ZipFile]::OpenRead($pair.zip);try{$s=$zip.GetEntry($pair.entry).Open();try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()}}finally{$zip.Dispose()}
  @{path=$pair.zip;sha256=$hash;expected=(Get-FileHash -LiteralPath $pair.source).Hash;passed=($hash -eq (Get-FileHash -LiteralPath $pair.source).Hash)}
 }
 $passed=$overlap -and @($contents|Where-Object {!$_.passed}).Count -eq 0 -and @($tokens|Where-Object {Test-Path $_}).Count -eq 0
 [ordered]@{passed=$passed;firstPid=$first.Id;secondPid=$second.Id;firstAliveWhenSecondCompleted=$overlap;sameParentDirectory=$true;allTokenConsumed=(@($tokens|Where-Object {Test-Path $_}).Count -eq 0);content=$contents;nativeSha256=(Get-FileHash -LiteralPath $AppPath).Hash;userSettingsChanged=$false} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $root 'result.json')
 Read-Shared $firstLog | Set-Content (Join-Path $root 'first-worker.log')
 $secondLog=Join-Path "$env:LOCALAPPDATA/Lhamiel" ("Lhamiel_compression_"+$second.Id+"_"+(Get-Date).ToString('yyyyMMdd')+".log");Read-Shared $secondLog | Set-Content (Join-Path $root 'second-worker.log')
 if(!$passed){throw 'Parallel compression evidence failed'};Get-Content (Join-Path $root 'result.json')
}finally{
 foreach($p in $owned){if(!$p.HasExited){$p.Kill($true);$p.WaitForExit()};$p.Dispose()}
 @($tokens|Where-Object {Test-Path $_})|ConvertTo-Json|Set-Content (Join-Path $root 'remaining-tokens.json')
}
