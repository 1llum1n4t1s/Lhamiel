# v1.0.181+ password protection i18n を 15 ロケールに英訳ベースで一括注入
# - 各ロケールの末尾 </ResourceDictionary> 直前に英訳ブロックを挿入
# - 既に注入済 (Text.Settings.Compression.PasswordHeader を含む) ファイルはスキップ
# - 翻訳改善は後追いで各言語ごとに上書き可

$ErrorActionPreference = 'Stop'

$localesDir = "$PSScriptRoot\..\src\Lhamiel\Resources\Locales"
$targets = @(
    'zh_CN', 'zh_TW', 'ko_KR', 'de_DE', 'fr_FR', 'es_ES',
    'pt_BR', 'ru_RU', 'it_IT', 'uk_UA', 'id_ID',
    'ta_IN', 'sa_IN', 'la_VA', 'fil_PH'
)

$block = @'

  <!-- Password protection (v1.0.181+) — TODO: localize from en_US baseline -->
  <x:String x:Key="Text.Settings.Compression.PasswordHeader" xml:space="preserve">Password Protection</x:String>
  <x:String x:Key="Text.Settings.Compression.EnablePassword" xml:space="preserve">Protect with password</x:String>
  <x:String x:Key="Text.Settings.Compression.EnablePasswordDescription" xml:space="preserve">Encrypts with AES-256 (WinZip AE-2) for ZIP, AES-256 for 7z.</x:String>
  <x:String x:Key="Text.Settings.Compression.TarNoEncryptionNote" xml:space="preserve">TAR does not support password protection. Choose ZIP or 7z.</x:String>
  <x:String x:Key="Text.Settings.Compression.ZipAesExplorerNote" xml:space="preserve">Note: AES-256 encrypted ZIPs cannot be opened by the built-in Windows Explorer. Recipients need 7-Zip, WinRAR, or another compatible tool.</x:String>
  <x:String x:Key="Text.Settings.Compression.EncryptFileNames" xml:space="preserve">Encrypt file names too</x:String>
  <x:String x:Key="Text.Settings.Compression.EncryptFileNamesDescription" xml:space="preserve">Encrypts the file-name listing (header) inside the archive. Without the password, even the contents cannot be browsed.</x:String>
  <x:String x:Key="Text.Settings.Compression.EncryptFileNamesZipUnsupported" xml:space="preserve">The ZIP format cannot encrypt file names (central directory). Switch to 7z to enable this option.</x:String>
  <x:String x:Key="Text.Settings.Compression.PasswordMode.GroupLabel" xml:space="preserve">How to enter the password</x:String>
  <x:String x:Key="Text.Settings.Compression.PasswordMode.PromptEachTime" xml:space="preserve">Prompt on every drop</x:String>
  <x:String x:Key="Text.Settings.Compression.PasswordMode.Remember" xml:space="preserve">Save and reuse (DPAPI encrypted)</x:String>
  <x:String x:Key="Text.Settings.Compression.SavedPasswordStatus.Set" xml:space="preserve">Password: set</x:String>
  <x:String x:Key="Text.Settings.Compression.SavedPasswordStatus.NotSet" xml:space="preserve">Password: not set (you will be asked on the next compression)</x:String>
  <x:String x:Key="Text.Settings.Compression.ChangeSavedPassword" xml:space="preserve">Change Password</x:String>
  <x:String x:Key="Text.Settings.Compression.ClearSavedPassword" xml:space="preserve">Clear Password</x:String>

  <x:String x:Key="Text.Password.SetTitle" xml:space="preserve">Set a Password</x:String>
  <x:String x:Key="Text.Password.SetMessage" xml:space="preserve">Set a password to encrypt the archive. Enter it twice for confirmation.</x:String>
  <x:String x:Key="Text.Password.ConfirmPlaceholder" xml:space="preserve">Password (confirm)</x:String>
  <x:String x:Key="Text.Password.MismatchWarning" xml:space="preserve">Passwords do not match. Please re-enter the confirmation.</x:String>
  <x:String x:Key="Text.Password.EmptyPasswordWarning" xml:space="preserve">Please enter a password.</x:String>
  <x:String x:Key="Text.Password.PasteHint" xml:space="preserve">You can paste from a password manager (Ctrl+V).</x:String>

  <x:String x:Key="Text.Confirm.WipeSavedPassword.Title" xml:space="preserve">Delete the saved password?</x:String>
  <x:String x:Key="Text.Confirm.WipeSavedPassword.Message" xml:space="preserve">Switching to "Prompt on every drop" will delete the currently saved password. Continue?</x:String>
  <x:String x:Key="Text.Confirm.ClearSavedPassword.Title" xml:space="preserve">Delete the saved password?</x:String>
  <x:String x:Key="Text.Confirm.ClearSavedPassword.Message" xml:space="preserve">The saved password will be deleted. You will be asked to enter it again on the next compression. Continue?</x:String>

  <x:String x:Key="Text.Notify.SavedPasswordDecryptFailed" xml:space="preserve">The saved password could not be restored (likely caused by copying settings from another PC or a Windows password reset). Please set the password again.</x:String>
  <x:String x:Key="Text.Notify.PartialSkipWithPassword" xml:space="preserve">{0} file(s) were skipped because they were inaccessible. A password-protected archive was created with the remaining files.</x:String>

  <x:String x:Key="Text.Error.AllSourcesInaccessible" xml:space="preserve">All source files were inaccessible, so compression was aborted. An empty archive was not created.</x:String>
  <x:String x:Key="Text.Error.PasswordNotSupportedByFormat" xml:space="preserve">The {0} format does not support password protection. Choose ZIP or 7z.</x:String>
'@

$marker = 'Text.Settings.Compression.PasswordHeader'

foreach ($locale in $targets) {
    $path = Join-Path $localesDir "$locale.axaml"
    if (-not (Test-Path $path)) {
        Write-Warning "skip (missing): $path"
        continue
    }
    $content = Get-Content -Path $path -Raw -Encoding UTF8
    if ($content -match [regex]::Escape($marker)) {
        Write-Host "skip (already injected): $locale"
        continue
    }
    $closeTag = '</ResourceDictionary>'
    $idx = $content.LastIndexOf($closeTag)
    if ($idx -lt 0) {
        Write-Warning "skip (no closing tag): $locale"
        continue
    }
    $newContent = $content.Substring(0, $idx) + $block + "`r`n" + $content.Substring($idx)
    # UTF-8 (BOM なし) で書き戻し: GetBytes は BOM を含まないので WriteAllBytes で安全
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($newContent)
    [System.IO.File]::WriteAllBytes($path, $bytes)
    Write-Host "injected: $locale"
}

Write-Host ""
Write-Host "Done. Run: dotnet build to verify."
