param([Parameter(Mandatory)][string]$AppPath,[Parameter(Mandatory)][string]$ArtifactDirectory)
$ErrorActionPreference='Stop'
$AppPath=(Resolve-Path -LiteralPath $AppPath).Path
$root=[IO.Path]::GetFullPath($ArtifactDirectory)
if(Test-Path -LiteralPath $root){throw 'Use an unused artifact directory'}
New-Item -ItemType Directory -Path $root | Out-Null
# 失敗モード: 実workerが他種出力保護をすり抜ける、待機解除後に出力できない、token残存、内容変化。
$processes=[Collections.Generic.List[Diagnostics.Process]]::new()
$markers=[Collections.Generic.List[string]]::new()
$tokens=[Collections.Generic.List[string]]::new()
$results=@()
function Start-Owned([string]$Exe,[string[]]$Arguments){
 $info=[Diagnostics.ProcessStartInfo]::new($Exe); $info.UseShellExecute=$false; $info.CreateNoWindow=$true
 foreach($arg in $Arguments){$info.ArgumentList.Add($arg)}
 $child=[Diagnostics.Process]::Start($info); $processes.Add($child); return $child
}
function Wait-File([string]$Path){
 $deadline=(Get-Date).AddSeconds(12)
 while(!(Test-Path -LiteralPath $Path)){if((Get-Date) -gt $deadline){throw 'Marker timeout'};Start-Sleep -Milliseconds 40}
}
$gateDll=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'bin/Release/net10.0/OutputGateE2E.dll'))
$settings=@{CompressionFormat='ZIP';CompressionOutputToSameDirectory=$false;ExtractionOutputToSameDirectory=$false;CreateArchiveNameFolder=$false;OpenCompressionOutputFolder=$false;OpenExtractionOutputFolder=$false;Check4UpdatesOnStartup=$false;IsPasswordProtectionEnabled=$false;ShowOverwriteConfirm=$false}
try{
 foreach($mode in @('compression','extraction')){
  $case=Join-Path $root $mode; $output=Join-Path $case 'output'; $data=Join-Path $case 'source'
  New-Item -ItemType Directory -Path $output,$data | Out-Null
  $payload=Join-Path $data 'payload.txt'; [IO.File]::WriteAllText($payload,'RERE_NATIVE_GATE_CANARY')
  $source=$payload; $expected=Join-Path $output 'payload.zip'; $heldPath=$output; $heldMode='extraction'
  if($mode -eq 'extraction'){
   $source=Join-Path $data 'input.zip'; $zip=[IO.Compression.ZipFile]::Open($source,[IO.Compression.ZipArchiveMode]::Create)
   try{$entry=$zip.CreateEntry('payload.txt'); $writer=[IO.StreamWriter]::new($entry.Open()); try{$writer.Write('RERE_NATIVE_GATE_CANARY')}finally{$writer.Dispose()}}finally{$zip.Dispose()}
   $expected=Join-Path $output 'payload.txt'; $heldPath=Join-Path $output 'busy.zip'; $heldMode='fixed'
  }
  $marker=Join-Path $case 'holder'; $markers.Add($marker)
  $holder=Start-Owned 'dotnet' @($gateDll,'worker',$heldMode,$heldPath,$marker)
  Wait-File ($marker+'.acquired')
  $settings.CompressionOutputDirectory=$output; $settings.ExtractionOutputDirectory=$output
  $token=[guid]::NewGuid().ToString('N'); $tokenPath=Join-Path $env:TEMP ('Lhamiel-compression-request-'+$token+'.json'); $tokens.Add($tokenPath)
  $request=@{SourcePaths=@($source);CompressionFormat='ZIP';Settings=$settings;EncryptFileNames=$false;ProcessAsBatch=$false;IsExtraction=($mode -eq 'extraction')}
  $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($request|ConvertTo-Json -Depth 15))
  $stream=[IO.FileStream]::new($tokenPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
  $started=Get-Date; $workerFlag=if($mode -eq 'extraction'){'--extraction-worker'}else{'--compression-worker'}
  $worker=Start-Owned $AppPath @($workerFlag,'--compression-request',$token)
  $deadline=(Get-Date).AddSeconds(10)
  while(Test-Path -LiteralPath $tokenPath){if($worker.HasExited -or (Get-Date) -gt $deadline){throw 'Worker did not accept token'};Start-Sleep -Milliseconds 40}
  Start-Sleep -Seconds 3
  $blocked=(!$worker.HasExited -and !(Test-Path -LiteralPath $expected))
  if(!$blocked){throw 'Actual worker bypassed hierarchy gate'}
  [IO.File]::WriteAllText($marker+'.release','release')
  if(!$holder.WaitForExit(10000) -or !$worker.WaitForExit(20000)){throw 'Worker did not finish after release'}
  if($holder.ExitCode -ne 0 -or $worker.ExitCode -ne 0 -or !(Test-Path -LiteralPath $expected)){throw 'Worker failed'}
  $hash=(Get-FileHash -LiteralPath $expected).Hash
  $text=if($mode -eq 'extraction'){[IO.File]::ReadAllText($expected)}else{
   $archive=[IO.Compression.ZipFile]::OpenRead($expected); try{$reader=[IO.StreamReader]::new($archive.GetEntry('payload.txt').Open());try{$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$archive.Dispose()}
  }
  if($text -ne 'RERE_NATIVE_GATE_CANARY'){throw 'Content mismatch'}
  $results+=@{mode=$mode;holderPid=$holder.Id;workerPid=$worker.Id;blockedWhileOtherOperationHeld=$blocked;workerExitCode=$worker.ExitCode;tokenConsumed=(!(Test-Path -LiteralPath $tokenPath));outputSha256=$hash;contentPassed=$true;elapsedSeconds=((Get-Date)-$started).TotalSeconds}
 }
 [ordered]@{passed=$true;nativeSha256=(Get-FileHash -LiteralPath $AppPath).Hash;cases=$results;userSettingsChanged=$false} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $root 'result.json')
 Get-Content (Join-Path $root 'result.json')
}finally{
 foreach($marker in $markers){if(!(Test-Path ($marker+'.release'))){[IO.File]::WriteAllText($marker+'.release','release on exit')}}
 foreach($child in $processes){if(!$child.HasExited -and !$child.WaitForExit(1000)){$child.Kill($true);$child.WaitForExit()};$child.Dispose()}
 # 未消費tokenは正確な作成パスを台帳へ残し、親が共通ごみ箱helperで清掃する。
 @($tokens | Where-Object {Test-Path -LiteralPath $_}) | ConvertTo-Json | Set-Content (Join-Path $root 'remaining-tokens.json')
}
