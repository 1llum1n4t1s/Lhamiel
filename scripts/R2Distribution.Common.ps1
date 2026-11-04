#requires -Version 7.2
# 公開関数の互換入口。署名・転送・検証の正本は同梱 R2Transfer.Core.cs。
$coreSource = Join-Path $PSScriptRoot 'R2Transfer.Core.cs'
$coreHash = (Get-FileHash -LiteralPath $coreSource -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
$coreType = 'Kagayoi.R2.Transfer' -as [type]
if ($null -ne $coreType) {
    $sourceHashProperty = $coreType.GetProperty('SourceHash', [Reflection.BindingFlags]'Public,Static')
    if ($null -eq $sourceHashProperty -or $sourceHashProperty.GetValue($null) -cne $coreHash) {
        throw [InvalidOperationException]::new('R2 transfer stopped: phase=input code=core-version-conflict')
    }
} else {
    Add-Type -Path $coreSource -ErrorAction Stop
    [Kagayoi.R2.Transfer]::RegisterSourceHash($coreHash)
}
function Stop-R2Distribution([string]$Phase, [string]$Code, [int]$Status = 0) { throw [Kagayoi.R2.Transfer]::Failure($Phase, $Code, $Status) }
function Get-R2DistributionHash([IO.Stream]$Stream) { return [Kagayoi.R2.Transfer]::Hash($Stream) }
function Get-R2DistributionHmac([byte[]]$Key, [string]$Text) { return ,([Kagayoi.R2.Transfer]::Hmac($Key, $Text)) }
function Get-R2DistributionAuthorization {
    param([string]$Method, [string]$CanonicalPath, [Collections.IDictionary]$Headers,
        [string]$PayloadHash, [string]$Timestamp, [string]$AccessKeyId, [string]$SecretAccessKey, [string]$Region = 'auto')
    return [Kagayoi.R2.Transfer]::Authorization($Method, $CanonicalPath, $Headers, $PayloadHash, $Timestamp, $AccessKeyId, $SecretAccessKey, $Region)
}
function New-R2DistributionClient { return [Kagayoi.R2.Transfer]::NewClient() }
function Send-R2DistributionRequest {
    param([Net.Http.HttpClient]$Client, [Net.Http.HttpRequestMessage]$Request, [string]$Phase,
        [Threading.CancellationToken]$CancellationToken = [Threading.CancellationToken]::None)
    return [Kagayoi.R2.Transfer]::Send($Client, $Request, $Phase, $CancellationToken)
}
function Get-R2DistributionCredentials {
    param([Net.Http.HttpClient]$Client, [string]$AccountId, [string]$ApiToken, [string]$AccessKeyId, [string]$SecretAccessKey)
    return [Kagayoi.R2.Transfer]::Credentials($Client, $AccountId, $ApiToken, $AccessKeyId, $SecretAccessKey)
}
function New-R2DistributionRequest {
    param([string]$Method, [uri]$Uri, [string]$CanonicalPath, [string]$PayloadHash,
        [hashtable]$Credentials, [hashtable]$ExtraHeaders = @{})
    return [Kagayoi.R2.Transfer]::Request($Method, $Uri, $CanonicalPath, $PayloadHash, $Credentials, $ExtraHeaders)
}
function Assert-R2DistributionRemote {
    param([Net.Http.HttpClient]$Client, [uri]$Uri, [string]$CanonicalPath, [hashtable]$Credentials,
        [long]$Size, [string]$Hash, [string]$ExpectedETag)
    return [Kagayoi.R2.Transfer]::VerifyRemote($Client, $Uri, $CanonicalPath, $Credentials, $Size, $Hash, $ExpectedETag)
}
function Publish-R2Object {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$AccountId, [Parameter(Mandatory)][string]$Bucket,
        [Parameter(Mandatory)][string]$FilePath, [string]$Key,
        [string]$ApiToken = $env:CLOUDFLARE_API_TOKEN, [string]$AccessKeyId = $env:AWS_ACCESS_KEY_ID,
        [string]$SecretAccessKey = $env:AWS_SECRET_ACCESS_KEY, [string]$CacheControl,
        [string]$ContentType = 'application/octet-stream', [switch]$Immutable, [string]$ExpectedETag)
    try {
        $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($FilePath)
        $result = [Kagayoi.R2.Transfer]::PublishObject($AccountId, $Bucket, $resolved, $Key, $ApiToken, $AccessKeyId,
            $SecretAccessKey, $CacheControl, $ContentType, $Immutable.IsPresent, $ExpectedETag,
            $PSBoundParameters.ContainsKey('ExpectedETag'), $PSBoundParameters.ContainsKey('AccessKeyId'),
            $PSBoundParameters.ContainsKey('SecretAccessKey'), [Func[Net.Http.HttpClient]]{ New-R2DistributionClient })
        return [pscustomobject]@{ Key = $result.Key; ETag = $result.ETag; SHA256 = $result.SHA256; Size = $result.Size; Changed = $result.Changed }
    } catch { throw [Kagayoi.R2.Transfer]::SafeFailure($_.Exception) }
}
