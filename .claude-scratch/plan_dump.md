

## ui_layout

圧縮設定タブ (MainWindow.axaml) の現行 LevelHeader (line 287-328) と ExcludedPatterns (line 330) の間に新ブロックを 1 つ挿入。チェックボックス + 折り畳みサブパネル方式 (直交配置)、format 切替時の警告 TextBlock を含む。

```xml
<!-- 既存 LevelHeader (line 328) の </primitives:HeaderedContentControl> 直後に挿入 -->
<primitives:HeaderedContentControl
    Header="{DynamicResource Text.Settings.Compression.PasswordHeader}">
    <StackPanel Spacing="8">

        <!-- 1) 親チェックボックス (TAR では IsEnabled=false で説明テキストを別表示) -->
        <CheckBox
            x:Name="EnablePasswordCheckBox"
            Content="{DynamicResource Text.Settings.Compression.EnablePassword}"
            IsChecked="{Binding IsPasswordProtectionEnabled}"
            IsEnabled="{Binding IsZipOrSevenZipFormat}" />
        <TextBlock
            Margin="28,-4,0,0"
            FontSize="12"
            Foreground="{DynamicResource Brush.FG2}"
            Text="{DynamicResource Text.Settings.Compression.EnablePasswordDescription}"
            TextWrapping="Wrap" />

        <!-- TAR 警告 (format = TAR のときだけ表示) -->
        <TextBlock
            Margin="28,-2,0,0"
            FontSize="12"
            Foreground="{DynamicResource Brush.WarningFG}"
            IsVisible="{Binding IsTarFormat}"
            Text="{DynamicResource Text.Settings.Compression.TarNoEncryptionNote}"
            TextWrapping="Wrap" />

        <!-- ZIP の Explorer 非互換注意 (format = ZIP かつ ON のときだけ表示) -->
        <TextBlock
            Margin="28,-2,0,0"
            FontSize="12"
            Foreground="{DynamicResource Brush.WarningFG}"
            IsVisible="{Binding ShowZipExplorerWarning}"
            Text="{DynamicResource Text.Settings.Compression.ZipAesExplorerNote}"
            TextWrapping="Wrap" />

        <!-- 2) サブパネル: ON のときのみ可視 -->
        <StackPanel
            Margin="24,4,0,0"
            IsVisible="{Binding IsPasswordSubPanelVisible}"
            Spacing="6">

            <!-- 2-a) ファイル名暗号化 (7z 専用、ZIP では disabled + 説明) -->
            <CheckBox
                x:Name="EncryptFileNamesCheckBox"
                Content="{DynamicResource Text.Settings.Compression.EncryptFileNames}"
                IsChecked="{Binding EncryptFileNames}"
                IsEnabled="{Binding IsSevenZipFormat}" />
            <TextBlock
                Margin="28,-4,0,0"
                FontSize="12"
                Foreground="{DynamicResource Brush.FG2}"
                Text="{DynamicResource Text.Settings.Compression.EncryptFileNamesDescription}"
                TextWrapping="Wrap" />
            <TextBlock
                Margin="28,-2,0,0"
                FontSize="12"
                Foreground="{DynamicResource Brush.WarningFG}"
                IsVisible="{Binding IsZipFormatAndPasswordOn}"
                Text="{DynamicResource Text.Settings.Compression.EncryptFileNamesZipUnsupported}"
                TextWrapping="Wrap" />

            <Separator Margin="0,4,0,4" />

            <!-- 2-b) パスワード入力モード (Tag + IsCheckedChanged の既存 DirMode 規約に揃える) -->
            <TextBlock
                Margin="0,0,0,2"
                FontWeight="SemiBold"
                Text="{DynamicResource Text.Settings.Compression.PasswordMode.GroupLabel}" />
            <RadioButton
                x:Name="PromptEachTimeRadio"
                Content="{DynamicResource Text.Settings.Compression.PasswordMode.PromptEachTime}"
                GroupName="PasswordMode"
                IsCheckedChanged="PasswordModeRadio_Changed"
                Tag="PromptEachTime" />
            <RadioButton
                x:Name="RememberPasswordRadio"
                Content="{DynamicResource Text.Settings.Compression.PasswordMode.Remember}"
                GroupName="PasswordMode"
                IsCheckedChanged="PasswordModeRadio_Changed"
                Tag="Remember" />

            <!-- 2-c) 保存済みパスワード状態 + 変更/削除ボタン -->
            <StackPanel
                Margin="24,4,0,0"
                IsVisible="{Binding IsRememberModeActive}"
                Spacing="4">
                <TextBlock
                    FontSize="12"
                    Foreground="{DynamicResource Brush.FG2}"
                    Text="{Binding SavedPasswordStatusText}" />
                <StackPanel Orientation="Horizontal" Spacing="6">
                    <Button
                        Command="{Binding ChangeSavedPasswordCommand}"
                        Content="{DynamicResource Text.Settings.Compression.ChangeSavedPassword}" />
                    <Button
                        Command="{Binding ClearSavedPasswordCommand}"
                        Content="{DynamicResource Text.Settings.Compression.ClearSavedPassword}"
                        IsEnabled="{Binding HasSavedPassword}" />
                </StackPanel>
            </StackPanel>
        </StackPanel>
    </StackPanel>
</primitives:HeaderedContentControl>
```

VM 側 derived properties (compiled binding 必須なので public):
- `IsZipOrSevenZipFormat` (format != TAR)
- `IsSevenZipFormat` (format == "7z")
- `IsZipFormat` (format == "ZIP")
- `IsTarFormat` (format == "TAR")
- `IsPasswordSubPanelVisible` = `IsPasswordProtectionEnabled && IsZipOrSevenZipFormat`
- `IsZipFormatAndPasswordOn` = `IsZipFormat && IsPasswordProtectionEnabled`
- `ShowZipExplorerWarning` = `IsZipFormat && IsPasswordProtectionEnabled`
- `IsRememberModeActive` = `IsPasswordProtectionEnabled && PasswordMode == "Remember"`
- `HasSavedPassword` (SettingsManager 経由)
- `SavedPasswordStatusText` (「設定済」「未設定 (次回圧縮時に設定)」)

`SelectedCompressionFormat`, `IsPasswordProtectionEnabled`, `PasswordMode` の宣言に `[NotifyPropertyChangedFor(...)]` を多数追加して上記 derived bool を全部リフレッシュする。

PasswordDialog (View/PasswordDialog.axaml) は Mode enum と Confirm TextBox を追加する Option A 方式。Title/Message は DynamicResource を 2 系統用意し、Mode に応じて IsVisible で出し分け (locale 動的切替に追従させるため、getter 方式は採用しない)。

## settings_schema

Settings.cs (RespectNestedGitignore の直後、line 251 付近) に以下 4 フィールドを追加。

```csharp
// ----- パスワード保護 (v1.0.181 で追加) -----

/// <summary>
/// 圧縮アーカイブをパスワードで保護するかどうか。
/// ON のとき ZIP=AES-256 (WinZip AE-2)、7z=AES-256 で暗号化。TAR は非対応。
/// パスワードそのものは <see cref="EncryptedCompressionPassword"/> (DPAPI 暗号化バイト列) のみ永続化。
/// </summary>
public bool IsPasswordProtectionEnabled { get; set; }

/// <summary>
/// 7z 形式でアーカイブ内ファイル名 (ヘッダ) も暗号化するか (-mhe=on 相当)。
/// ZIP では仕様上不可能なので無視される (ArchiveCompressor で format ガードあり)。
/// 既定値は <see cref="IsPasswordProtectionEnabled"/> が ON になった瞬間 true (UI 側で同期)。
/// 設定永続値としてはユーザーが明示的に変更した最終値を保持する。
/// </summary>
public bool EncryptFileNames { get; set; } = true;

/// <summary>
/// パスワード入力モード: "PromptEachTime" (ドロップごとに確認) または "Remember" (DPAPI で保存)。
/// SanitizeAfterLoad で allow-list 検証し、不正値は "PromptEachTime" に矯正。
/// </summary>
public string PasswordMode { get; set; } = "PromptEachTime";

/// <summary>
/// 圧縮パスワードを DPAPI (CurrentUser scope) で暗号化したバイト列。
/// PasswordMode == "Remember" のときだけ書き込まれ、"PromptEachTime" 切替で null 化される。
/// System.Text.Json は byte[] を Base64 文字列としてシリアライズする (AOT 安全)。
/// 別ユーザー / 別 PC / Windows パスワードリセット後は復号失敗 → 上位レイヤーが UI 警告して再設定要求。
/// 長さは 1KB を上限とし SanitizeAfterLoad でクランプする。
/// </summary>
public byte[]? EncryptedCompressionPassword { get; set; }
```

**プレーンテキストアクセサは Settings に置かない** (critique blocker #3 「Settings 寿命 = アプリ寿命」回避)。代わりに `Util/CompressionPasswordSession.cs` (新規) を作る:

```csharp
/// <summary>
/// 圧縮実行スコープでのみ平文パスワードを扱う一時保持クラス。
/// Settings には DPAPI 暗号化バイト列だけが残り、平文 string は短寿命のローカル変数に閉じる。
/// </summary>
internal static class CompressionPasswordSession
{
    /// <summary>DPAPI ciphertext を復号して平文を返す。失敗時は null。呼出側が責任を持って参照を捨てる。</summary>
    internal static string? TryUnprotect(byte[]? ciphertext);

    /// <summary>平文をエンコード → DPAPI で暗号化したバイト列を返す。input の char[] を best-effort で zero-fill する経路は後段。</summary>
    internal static byte[]? Protect(string? plaintext);
}
```

[JsonIgnore] な平文アクセサは作らない (critique security #6 「getter 副作用」回避)。すべての利用箇所で `CompressionPasswordSession.TryUnprotect(settings.EncryptedCompressionPassword)` を明示呼出する。

## persistence

**DPAPI 呼び出し形状** (CompressionPasswordSession.cs):

```csharp
internal static byte[]? Protect(string? plaintext)
{
    if (string.IsNullOrEmpty(plaintext)) return null;
    if (plaintext.Length > 1024) throw new ArgumentException("password too long (max 1024 chars)", nameof(plaintext));

    byte[] plain = Encoding.UTF8.GetBytes(plaintext);
    try
    {
        return ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(plain);  // best-effort、string 自体の zero 化は不能
    }
}

internal static string? TryUnprotect(byte[]? ciphertext)
{
    if (ciphertext is null || ciphertext.Length == 0) return null;
    byte[]? plain = null;
    try
    {
        plain = ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
    catch (CryptographicException ex)
    {
        Logger.Log($"DPAPI 復号失敗 (別ユーザー/PC コピー or マスタキー破損): {ex.Message}", LogLevel.Warning);
        return null;  // ★Settings 側の自動 wipe はしない。呼出側 (ArchiveProcessor) が UI 警告 + 再プロンプトに分岐。
    }
    finally
    {
        if (plain is not null) CryptographicOperations.ZeroMemory(plain);
    }
}
```

**Mode 切替時の wipe ロジック** (MainWindowViewModel.PasswordModeRadio_Changed 経由):

```csharp
private void OnPasswordModeChangingToPromptEachTime()
{
    // critique security #3 対応: AutoSave 経路ではなく MutateAndSave で 1 トランザクション化。
    // モードと EncryptedCompressionPassword の更新を 1 lock 内で完了させ、
    // 「mode = PromptEachTime + ciphertext 残存」の中間状態を絶対作らない。
    var hadSaved = SettingsManager.Instance.Current.EncryptedCompressionPassword is { Length: > 0 };

    if (hadSaved)
    {
        // 破壊操作 → ConfirmDialog で確認
        var ok = await ConfirmDialogImpl.ShowAsync(
            App.Text("Confirm.WipeSavedPassword.Title"),
            App.Text("Confirm.WipeSavedPassword.Message"),
            parentWindow);
        if (!ok)
        {
            // RadioButton を元に戻す (一時ガードフラグで反応抑制ループ防止)
            _suppressPasswordModeWipe = true;
            try { RememberPasswordRadio.IsChecked = true; } finally { _suppressPasswordModeWipe = false; }
            return;
        }
    }

    SettingsManager.Instance.MutateAndSave(s =>
    {
        s.PasswordMode = "PromptEachTime";
        s.EncryptedCompressionPassword = null;
    });
    // Save() 完了後に UI 状態確定 (Save 失敗時は throw → catch でユーザー通知 → UI ロールバック)
}
```

**3-stage Load fallback の挙動** (Settings.Load):

- **Stage 1 (JsonSerializer)**: `byte[]?` は Base64 文字列として自動復元。`WhenWritingNull` 既定挙動により未保存時は JSON 上に項目が存在しない (= null で復元)。
- **Stage 2 (TryRecoverFromJsonDocument)**: 必ず追加する救出ハンドラ:
  ```csharp
  if (TryGetBool(root, nameof(IsPasswordProtectionEnabled), out var ipe)) { s.IsPasswordProtectionEnabled = ipe; recoveredCount++; }
  if (TryGetBool(root, nameof(EncryptFileNames), out var efn)) { s.EncryptFileNames = efn; recoveredCount++; }
  if (TryGetString(root, nameof(PasswordMode), out var pm)) { s.PasswordMode = pm; recoveredCount++; }
  if (root.TryGetProperty(nameof(EncryptedCompressionPassword), out var ecpEl) && ecpEl.ValueKind == JsonValueKind.String)
  {
      try { s.EncryptedCompressionPassword = ecpEl.GetBytesFromBase64(); recoveredCount++; }
      catch (FormatException) { /* Base64 破損 → null のまま */ }
      catch (InvalidOperationException) { /* ValueKind 不一致 */ }
  }
  ```
  これにより無関係プロパティの型不整合で stage-2 に落ちても暗号化パスワードが温存される (critique security #1 blocker 対応)。
- **Stage 3 (corrupt → .bak)**: 既存挙動を維持。追加で **`.corrupt_*.bak` の中身は zero-overwrite してから rename** する (best-effort 削除耐性向上、critique security #10 対応)。サポート ZIP の収集経路に `*.corrupt_*.bak` 除外ロジックを追加。
- **SanitizeAfterLoad**:
  - `PasswordMode` が `"PromptEachTime"`/`"Remember"` 以外なら `"PromptEachTime"` に矯正。
  - `EncryptedCompressionPassword?.Length > 4096` なら `null` 化 + Logger.Warning (異常巨大化防御)。
  - **整合性 degrade**: `IsPasswordProtectionEnabled && PasswordMode == "Remember" && EncryptedCompressionPassword is null/empty` なら `PasswordMode = "PromptEachTime"` に倒す + Logger.Warning (critique UX #15)。
- **ResetToDefaults**: 4 件全部リセット (`IsPasswordProtectionEnabled=false; EncryptFileNames=true; PasswordMode="PromptEachTime"; EncryptedCompressionPassword=null;`)。

**DPAPI 失敗時の UX フロー** (Settings 自動 wipe はしない):

1. 圧縮実行直前に `CompressionPasswordSession.TryUnprotect(...)` が `null` を返す。
2. ArchiveProcessor は「保存パスワード復号失敗」と判定 → 通常の `PromptForPasswordAsync(mode: CompressNew)` にフォールバック。
3. UI に MessageService 経由で 1 度だけ案内: 「保存していたパスワードを復元できませんでした (別 PC からの設定コピー / Windows パスワードリセット等)。再設定してください」。
4. ユーザーがダイアログで新パスワードを入力すると、`MutateAndSave` で新 ciphertext を上書き保存。
5. **Settings.json のサイレント wipe は禁止**。ユーザーが明示的に「保存済みパスワードを削除」ボタンを押すか、PromptEachTime に切替えたときだけ wipe。

**追加防御**:
- `DiagnosticsCollector._sensitiveKeys` に `"EncryptedCompressionPassword"` を明示追加 (regex に頼らない、critique security #2)。
- `Settings.Snapshot` 直上のコメントブロックに「`EncryptedCompressionPassword` は wholesale-replace 限定、`Array.Clear` でその場破壊するな」と追記。
- `_suppressPasswordModeWipe` ガードを ViewModel に追加 (No 選択で RadioButton を元に戻すときのループ防止)。

## compression_flow

```mermaid
flowchart TD
  A[Drop event on MainWindow] --> B{MainWindowViewModel<br/>ProcessDroppedPathsAsync}
  B --> B0{_isAwaitingPasswordInput?<br/>(Interlocked)}
  B0 -- yes --> Z1[早期 return + DragOver で feedback<br/>(critique UX #4)]
  B0 -- no --> B1[settings = SettingsManager.Snapshot<br/>(byte[] ciphertext のみ)]
  B1 --> C{圧縮 or 展開?}
  C -- 展開 --> X[既存 ArchiveExtractor 経路<br/>(IPasswordDialogService 経由に refactor)]
  C -- 圧縮 --> D{IsPasswordProtectionEnabled<br/>&& format != TAR?}
  D -- no --> H[password = null]
  D -- yes --> E{PasswordMode}

  E -- PromptEachTime --> F1[PasswordDialog Mode=CompressNew<br/>確認 2 回入力 + 空文字拒否]
  F1 -- Cancel --> Z2[全体キャンセル<br/>NativeArchiveGate 取得前]
  F1 -- OK --> H1[password = plaintext local]

  E -- Remember --> G1[CompressionPasswordSession.TryUnprotect<br/>(settings.EncryptedCompressionPassword)]
  G1 -- 成功 --> H2[password = plaintext local]
  G1 -- null=未保存 --> F2[初回保存フロー: PasswordDialog Mode=CompressNew<br/>OK → MutateAndSave で ciphertext を即時保存]
  F2 -- Cancel --> Z2
  F2 -- OK --> H2
  G1 -- 復号失敗 --> W1[MessageService.NotifyAsync<br/>「再設定してください」<br/>+ PasswordDialog Mode=CompressNew]
  W1 -- OK --> H2
  W1 -- Cancel --> Z2

  H --> I[ArchiveProcessor.CompressItemsAsync<br/>(password: string?, encryptFileNames: bool)]
  H1 --> I
  H2 --> I
  I --> J[ScanSourceFiles<br/>(NativeArchiveGate 取得前、純 .NET I/O)]
  J --> K[ArchiveCompressor.CompressFilesAsync<br/>password を明示引数で渡す<br/>★Settings には平文を載せない]

  K --> L[★NativeArchiveGate.EnterAsync<br/>(critique integrity #4: dialog は絶対この外側)]
  L --> M[Task.Run スコープ]
  M --> N[CreateArchiveWriter<br/>format=ZIP → Password+EncryptionMethod=Aes256<br/>format=7z  → Password (+ CustomParameters he=on if encryptFileNames)<br/>format=TAR → Validate throw → ArgumentException 上位で UI 警告]

  N --> N1{addedCount tracker init}
  N1 --> O[writer.Add ループ<br/>addedCount++ on 成功<br/>AccessException → skip + warn]
  O --> P{addedCount == 0<br/>&& inaccessibleSkipped > 0?}
  P -- yes --> Q1[★throw InvalidOperationException<br/>「全ファイルアクセス不能、空アーカイブ生成阻止」<br/>(critique integrity #2 blocker 対応)]
  P -- no --> Q2[outputCreated = true]
  Q2 --> R[writer.Save → 7z.dll で AES-256 暗号化]
  R --> S[KeepAliveCallbacks → Dispose]
  S --> T[NativeArchiveGate 解放]
  T --> U[password local = null<br/>(GC まで残るが参照解除)]

  U --> V{inaccessibleSkipped > 0<br/>&& password != null?}
  V -- yes --> V1[MessageService.NotifyAsync<br/>「N 件スキップ、生成された暗号化アーカイブには M 件のみ」]
  V -- no --> Y
  V1 --> Y[OpenCompressionOutputFolder]

  Z1 --> Y
  Z2 --> Y
  Q1 --> Y
```

**CreateArchiveWriter の改修コア (critique integrity #1 blocker 対応)**:

```csharp
private static ArchiveWriter CreateArchiveWriter(
    Format format, Settings settings, string? password, bool encryptFileNames, int maxThreads = -1)
{
    var threadCount = Math.Clamp(maxThreads > 0 ? maxThreads : Environment.ProcessorCount, 1, Environment.ProcessorCount);
    var hasPassword = !string.IsNullOrEmpty(password);

    if (format == Format.SevenZip)
    {
        var custom = new Dictionary<string, string>();
        if (hasPassword && encryptFileNames) custom["he"] = "on";  // ★ヘッダ暗号化はここでしか入らない
        var options = new CompressionOption
        {
            CompressionLevel = (CompressionLevel)settings.SevenZipCompressionLevel,
            CompressionMethod = CompressionMethod.Lzma2,
            ThreadCount = threadCount,
            Password = hasPassword ? password! : string.Empty,
            CustomParameters = custom,
        };
        return new ArchiveWriter(format, options);
    }
    if (format == Format.Zip)
    {
        var options = new CompressionOption
        {
            CompressionLevel = (CompressionLevel)settings.ZipCompressionLevel,
            CompressionMethod = CompressionMethod.Deflate,
            ThreadCount = threadCount,
            CodePage = CodePage.Utf8,
            Password = hasPassword ? password! : string.Empty,
            // ★ZIP では Password 設定時は必ず Aes256 を同時設定。ZipCrypto fallback を物理的に防ぐ。
            EncryptionMethod = hasPassword ? EncryptionMethod.Aes256 : EncryptionMethod.Default,
        };
        // 二重防御 assert
        if (hasPassword && options.EncryptionMethod != EncryptionMethod.Aes256)
            throw new InvalidOperationException("ZIP encryption must be AES-256");
        return new ArchiveWriter(format, options);
    }
    if (hasPassword) throw new InvalidOperationException("TAR does not support encryption");  // UI でガード済だが念押し
    return new ArchiveWriter(format);
}
```

**Drop 中の二重ドロップガード**: MainWindowViewModel に `private int _isAwaitingPasswordInput;` を追加し、`Interlocked.CompareExchange(ref _isAwaitingPasswordInput, 1, 0)` で取得失敗時は早期 return。finally で 0 に戻す。

**Mode 変更中 batch 一時停止**: Mode が PromptEachTime に切り替わるとき、`_compressionBatchCts.Cancel()` を発火して in-flight バッチを止め、ユーザーに「進行中のバッチをキャンセルしました」を通知。新規ドロップは新モードで処理。

## i18n_keys

- Text.Settings.Compression.PasswordHeader
- Text.Settings.Compression.EnablePassword
- Text.Settings.Compression.EnablePasswordDescription
- Text.Settings.Compression.TarNoEncryptionNote
- Text.Settings.Compression.ZipAesExplorerNote
- Text.Settings.Compression.EncryptFileNames
- Text.Settings.Compression.EncryptFileNamesDescription
- Text.Settings.Compression.EncryptFileNamesZipUnsupported
- Text.Settings.Compression.PasswordMode.GroupLabel
- Text.Settings.Compression.PasswordMode.PromptEachTime
- Text.Settings.Compression.PasswordMode.Remember
- Text.Settings.Compression.SavedPasswordStatus.Set
- Text.Settings.Compression.SavedPasswordStatus.NotSet
- Text.Settings.Compression.ChangeSavedPassword
- Text.Settings.Compression.ClearSavedPassword
- Text.Password.SetTitle
- Text.Password.SetMessage
- Text.Password.ConfirmPlaceholder
- Text.Password.MismatchWarning
- Text.Password.EmptyPasswordWarning
- Text.Password.PasteHint
- Text.Confirm.WipeSavedPassword.Title
- Text.Confirm.WipeSavedPassword.Message
- Text.Confirm.ClearSavedPassword.Title
- Text.Confirm.ClearSavedPassword.Message
- Text.Notify.SavedPasswordDecryptFailed
- Text.Notify.PartialSkipWithPassword
- Text.Error.AllSourcesInaccessible
- Text.Error.PasswordRequiredForEncryption


## files_to_modify

- {"path": "src/Lhamiel/Util/Settings.cs", "change_summary": "4 プロパティ追加 (IsPasswordProtectionEnabled, EncryptFileNames, PasswordMode, EncryptedCompressionPassword)、ResetToDefaults に 4 件追加、TryRecoverFromJsonDocument に 4 件の救出ハンドラ追加、SanitizeAfterLoad で PasswordMode allow-list / ciphertext 長制限 / 整合性 degrade を実装、Snapshot コメントブロックに byte[] wholesale-replace 規約を追記。"}
- {"path": "src/Lhamiel/Util/CompressionPasswordSession.cs", "change_summary": "新規ファイル。DPAPI Protect/TryUnprotect 静的メソッドを提供。CryptographicOperations.ZeroMemory で平文 byte[] を best-effort zero-fill。例外時は Settings を自動変更せず null を返す (上位レイヤーが UI 警告と再プロンプトを担当)。"}
- {"path": "src/Lhamiel/Lhamiel.csproj", "change_summary": "PackageReference <System.Security.Cryptography.ProtectedData Version=10.0.*> を追加。"}
- {"path": "src/Lhamiel/Util/ServiceContracts.cs", "change_summary": "IPasswordDialogService インターフェース追加 (PromptForPasswordAsync(archiveName, mode, isRetry, parentWindow, ct))。DefaultPasswordDialogService を PasswordDialog.ShowFromBackgroundAsync に委譲する実装。"}
- {"path": "src/Lhamiel/Util/ArchiveProcessor.cs", "change_summary": "PasswordDialogImpl static プロパティ追加。CompressItemsAsync 系経路でパスワード解決 (NativeArchiveGate 取得前) を実装: PromptEachTime / Remember 分岐、DPAPI 復号失敗時の再プロンプトフォールバック、Mode 変更時の batch キャンセル、二重ドロップガード。password を ArchiveCompressor に明示引数で渡す。ArchiveExtractor の直接呼出も PasswordDialogImpl 経由に統一。空アーカイブ検出時の throw を完了通知前で処理。"}
- {"path": "src/Lhamiel/Util/ArchiveCompressor.cs", "change_summary": "CreateArchiveWriter のシグネチャに password / encryptFileNames を追加。ZIP は Password 設定時に EncryptionMethod=Aes256 を強制 (二重防御 assert 付き)。7z は CustomParameters[\"he\"]=\"on\" を encryptFileNames=true で注入。CompressFilesAsync は password を引数で受け取り、CreateArchiveWriter に渡す。writer.Add 成功カウンタを追加し、addedCount==0 で throw する事故防止。完了時 inaccessibleSkipped>0 && password!=null なら警告通知を上位に伝達。"}
- {"path": "src/Lhamiel/Util/ArchiveExtractor.cs", "change_summary": "1080 行の PasswordDialog.ShowFromBackgroundAsync 直接呼出を ArchiveProcessor.PasswordDialogImpl.PromptForPasswordAsync(mode: Extract, ...) に置換。"}
- {"path": "src/Lhamiel/Util/ArchiveErrorHandler.cs", "change_summary": "EncryptedOrWrongPassword が圧縮側でも分類されるよう error mapping を流用 (新規追加なし、再利用確認のみ)。再試行ループ判定で password 起因かどうかを呼出側に返す isPasswordRelated 補助メソッドを追加。"}
- {"path": "src/Lhamiel/Util/DiagnosticsCollector.cs", "change_summary": "_sensitiveKeys に 'EncryptedCompressionPassword' を明示追加。サポート ZIP 収集経路から *.corrupt_*.bak ファイルを除外。MaskLogPaths に password 値リダクション層を追加 (CompressionPasswordSession が登録するセッショントークン置換)。dumps/ ディレクトリは Settings.IsPasswordProtectionEnabled+PasswordMode=Remember のとき含めない、または同意プロンプト経由に変更。"}
- {"path": "src/Lhamiel/Util/SuperLightLogger.cs", "change_summary": "Logger.RegisterRedactionToken(string sentinel) を追加。CompressionPasswordSession が in-use なときセンチネルを登録し、Log() で文字列置換 (best-effort accidental logging guard)。"}
- {"path": "src/Lhamiel/ViewModels/MainWindowViewModel.cs", "change_summary": "[ObservableProperty] で 3 プロパティ (IsPasswordProtectionEnabled, EncryptFileNames, PasswordMode) を追加 + 多数の derived public bool (IsZipFormat / IsSevenZipFormat / IsTarFormat / IsZipOrSevenZipFormat / IsPasswordSubPanelVisible / ShowZipExplorerWarning / IsZipFormatAndPasswordOn / IsRememberModeActive / HasSavedPassword / SavedPasswordStatusText)。SelectedCompressionFormat 等に [NotifyPropertyChangedFor] 多数追加。ApplySettingsToManager / LoadFromSettings に 3 件のミラーリング追加。partial void OnXxxChanged で AutoSave。ChangeSavedPasswordCommand / ClearSavedPasswordCommand を RelayCommand で追加 (PasswordDialog 起動と MutateAndSave 呼出)。Interlocked _isAwaitingPasswordInput ガード。Mode 変更時 batch CTS キャンセル。"}
- {"path": "src/Lhamiel/View/MainWindow.axaml", "change_summary": "LevelHeader と ExcludedPatterns の間に新 HeaderedContentControl を 1 ブロック挿入 (上記 ui_layout 通り)。"}
- {"path": "src/Lhamiel/View/MainWindow.axaml.cs", "change_summary": "PasswordModeRadio_Changed ハンドラを追加 (Tag = 'PromptEachTime'/'Remember' で VM.PasswordMode を設定、PromptEachTime に切替時は ConfirmDialog → MutateAndSave wipe 経路を呼出)。Opened ハンドラで Settings.PasswordMode から RadioButton.IsChecked を初期同期。_suppressPasswordModeWipe ガードフラグ。"}
- {"path": "src/Lhamiel/View/PasswordDialog.axaml", "change_summary": "ConfirmBox TextBox と MismatchWarning / EmptyWarning TextBlock を追加。Title/Message は Mode 別に 2 系統の TextBlock + DynamicResource で IsVisible 出し分け (locale 動的切替に追従)。Tab 順序 (TabIndex) 明示。"}
- {"path": "src/Lhamiel/View/PasswordDialog.axaml.cs", "change_summary": "public enum PasswordDialogMode { Extract, CompressNew } を追加。新コンストラクタ (archiveName, isRetry, mode)、既存 2-arg ctor は委譲。OkButton_Click で CompressNew モード時に (1) 空文字拒否、(2) mismatch チェック (confirm のみクリア、primary 保持)、(3) 全 OK で Close(true)。PasswordBox_KeyDown は CompressNew モードで Enter→ConfirmBox にフォーカス移動、ConfirmBox_KeyDown で Enter→Submit。IME 合成中の Enter 無視。ShowFromBackgroundAsync に optional mode パラメータ追加 (デフォルト Extract、既存呼出と source-compatible)。ClearPassword は Confirm 側も含めて zero 化。"}
- {"path": "src/Lhamiel/Resources/Locales/ja_JP.axaml", "change_summary": "29 件の新規 i18n キー追加。"}
- {"path": "src/Lhamiel/Resources/Locales/en_US.axaml", "change_summary": "29 件の新規 i18n キー追加。"}
- {"path": "src/Lhamiel/Resources/Locales/*.axaml (残り 15 ロケール)", "change_summary": "29 件の新規 i18n キー追加 (zh_CN/zh_TW/ko_KR/de_DE/fr_FR/es_ES/pt_BR/ru_RU/it_IT/nl_NL/pl_PL/tr_TR/ar_SA/hi_IN/vi_VN)。"}
- {"path": "CLAUDE.md", "change_summary": "圧縮機能セクションに『パスワード保護』節を追加 (ZIP=AES-256 WinZip AE-2 / 7z=AES-256 +he=on、Settings 永続フィールド、DPAPI scope の制約 = settings.json は machine+user に bound、サポート ZIP 除外フィールド、CompressionPasswordSession 経由のみ平文を扱う規約、Settings.Load の新フィールド追加チェックリスト 6 点)。"}
- {"path": "docs/SETTINGS_SCHEMA.md", "change_summary": "新 4 フィールドのスキーマ説明を追加。"}
- {"path": ".github/workflows/dotnet-build.yml", "change_summary": "CI grep ステップ追加: Process.Start.*password / Logger.Log.*password 系の事故的パスワード混入をビルド時に検出して fail させる。"}


## tests_to_add

- ArchiveCompressorTests: ZIP+Password で生成したアーカイブが AES-256 (WinZip AE-2) で暗号化されている (ArchiveReader で Method メタを検査、ZipCrypto 文字列が含まれていたら fail)
- ArchiveCompressorTests: 7z+Password+EncryptFileNames=true で生成したアーカイブはパスワード無しで Items 列挙が EncryptionException を投げる (he=on の実効検証)
- ArchiveCompressorTests: 7z+Password+EncryptFileNames=false ではファイル名が見えるが本文は暗号化されている
- ArchiveCompressorTests: TAR+Password を要求すると ArgumentException / InvalidOperationException で fail-fast
- ArchiveCompressorAdversarialTests: 全ソースファイルが AccessException でスキップされたケース、空の password-protected アーカイブを生成せず InvalidOperationException を throw する
- ArchiveCompressorAdversarialTests: password=null/empty/whitespace で IsPasswordProtectionEnabled=true なら ArgumentException で fail-fast (UI ガードのバックアップ)
- PasswordDialogTests: CompressNew モードで空入力 Enter は Close せず EmptyWarning が visible になる
- PasswordDialogTests: CompressNew モードで mismatch 入力時、primary は保持され confirm だけクリアされ MismatchWarning が visible
- PasswordDialogTests: Extract モードでは ConfirmBox / MismatchWarning が非表示で従来挙動と完全互換
- PasswordDialogTests: Enter キーで PasswordBox→ConfirmBox にフォーカス移動 (CompressNew モード)
- PasswordDialogAdversarialTests: ShowFromBackgroundAsync を cancelled CT で呼ぶと dialog インスタンス生成前に即 null を返す
- SettingsTests: TryRecoverFromJsonDocument が IsPasswordProtectionEnabled / EncryptFileNames / PasswordMode / EncryptedCompressionPassword を全部復元する (1 つだけ corrupt な他プロパティを混ぜたケース)
- SettingsTests: ResetToDefaults が 4 件全部を初期値に戻す
- SettingsTests: SanitizeAfterLoad が PasswordMode='Invalid' を 'PromptEachTime' に矯正
- SettingsTests: SanitizeAfterLoad が EncryptedCompressionPassword.Length>4096 を null 化
- SettingsTests: SanitizeAfterLoad が IsPasswordProtectionEnabled+Remember+ciphertext null の不整合状態を PromptEachTime に degrade
- SettingsAdversarialTests: byte[] 値が完全壊れた Base64 を含む settings.json は stage-2 で他プロパティを復元しつつ EncryptedCompressionPassword だけ null になる
- CompressionPasswordSessionTests: Protect→TryUnprotect の往復で元の文字列が完全一致する (ASCII / 日本語 NFC / 1024 char 上限)
- CompressionPasswordSessionTests: 1025 char 入力は ArgumentException
- CompressionPasswordSessionTests: 別ユーザーで暗号化された Base64 を Unprotect すると null + Logger.Warning が記録され、Settings の自動 wipe は起きない
- DiagnosticsCollectorTests: サポート ZIP の settings.json に EncryptedCompressionPassword が含まれない (***マスク済み)
- DiagnosticsCollectorTests: サポート ZIP に *.corrupt_*.bak が含まれない
- DiagnosticsCollectorAdversarialTests: 平文パスワード文字列が Logger に流れた場合、Logger.RegisterRedactionToken による redaction が適用されサポート ZIP に sentinel が現れない
- ArchiveProcessorTests (Collection=ArchiveProcessor): PromptEachTime モードで Drop ごとに PasswordDialogImpl が 1 回呼ばれる
- ArchiveProcessorTests (Collection=ArchiveProcessor): Remember モードで保存済み ciphertext から復号成功すれば dialog は呼ばれない
- ArchiveProcessorTests (Collection=ArchiveProcessor): Remember モードで TryUnprotect が null を返したら CompressNew モードで再プロンプトされ、OK 後 MutateAndSave で新 ciphertext が保存される
- ArchiveProcessorAdversarialTests: 二重ドロップガードが 2 回目の同時 Drop を抑制する (Interlocked)
- ArchiveProcessorAdversarialTests: バッチ実行中に PasswordMode を PromptEachTime に切替 → 進行中バッチがキャンセルされる
- ArchiveProcessorAdversarialTests: ZIP+password 圧縮で writer.Save が EncryptionException 等を投げた場合、TryDeletePartialOutput が走り部分ファイルが残らない
- MainWindowViewModelTests: SelectedCompressionFormat=TAR で IsPasswordProtectionEnabled が true でも IsPasswordSubPanelVisible が false になり、ShowZipExplorerWarning も false
- MainWindowViewModelTests: SelectedCompressionFormat=ZIP で EncryptFileNames=true は IsEnabled=false (実圧縮では 7z 専用フラグ無視) + 警告 TextBlock 表示条件が満たされる
- LocaleParityTests: 17 ロケール全てに 29 件の新規キーが存在する (DynamicResource 解決時のキー文字列リテラル露出防止)
- PasswordModeWipeTests: Remember → PromptEachTime 切替時、ConfirmDialog で Yes → MutateAndSave で mode + ciphertext が 1 トランザクションで更新される (中間状態 = mode 変更済み + ciphertext 残存 は発生しない)
- PasswordModeWipeTests: Remember → PromptEachTime 切替時、ConfirmDialog で No → RadioButton が Remember に戻り Settings は変更されない
- ResetToDefaultsTests: 'Reset to defaults' 操作後、EncryptedCompressionPassword が null になっていることを確認


## risks

- **平文パスワードの GC 寿命**: .NET の string は immutable で zero-fill 不能。CompressionPasswordSession 経由で短寿命ローカルに閉じ、Settings には byte[] ciphertext のみ持つ設計で best-effort 最小化するが、Native AOT 環境下の GC 挙動 + KeepAliveCallbacks による writer 延命の組合せで、圧縮中に MiniDump を採取するとスタック上の plaintext 参照が含まれる可能性は残る。CrashHandler は MiniDumpNormal なので data segment は除外されるが、call stack 内の参照は捕捉される。サポート ZIP に dumps/ を含める運用は Remember モード時に同意プロンプトを挟むことで緩和するが、ゼロにはできない。
- **ZIP AES-256 (WinZip AE-2) のエクスプローラー非互換**: Windows 標準のビルトイン ZIP ハンドラは AES 暗号 ZIP を展開できない (Win11 23H2 以前)。受信者が 7-Zip / WinRAR を持っていないと「アーカイブが壊れている」と誤認する。UI で警告 TextBlock を出すが、業務メール添付など『受信者の環境がコントロールできない』ケースは事故の温床。リリースノートと README にも明記する。
- **DPAPI CurrentUser scope の壊れやすさ**: settings.json を OneDrive/Syncthing/git 等で同期するパワーユーザーは別 PC で復号失敗 → 「保存していたパスワードが消えた」と認識する。CLAUDE.md で『settings.json は machine+user bound』を強く明記するが、暗黙的に既知のユーザー層を切り捨てる仕様判断であり、CHANGELOG とリリースノートで事前周知が必要。同様に Windows パスワードリセット (admin reset / domain reset) でも復号不能になる。
- **設計仕様 4『PromptEachTime 切替で wipe』は破壊操作だが Undo を提供しない**: critique UX #2 で指摘の通り、切替 1 クリックで二度と戻らない値を消す。ConfirmDialog で 1 段挟むだけにとどめ、セッション中の Undo (短時間メモリ保持) は実装しない (機能複雑度 + メモリ寿命延長のトレードオフで採用しない)。リスクは『誤クリック → Yes → 復旧不能』。代替案は『Cancel ボタンを ConfirmDialog で IsDefault にして誤 Enter を防ぐ』程度。
- **1llum1n4t1s.Sevenzip ライブラリの将来挙動依存**: ライブラリは CompressionOption.Password の writer 内部での lifetime / Dispose 時のクリア有無を保証しない。現状解析した範囲では reasonable だが、minor バージョン更新で内部挙動が変わると plaintext のメモリ寿命が伸びる可能性。NuGet 更新時の回帰テスト (AES-256 生成検証 + he=on 検証) を CI で必ず回す体制が必要。


## open_questions

- ZIP 形式選択 + パスワード ON のとき、Windows 標準エクスプローラーで展開できない旨を『常時表示の TextBlock』だけで済ますか、それとも『初回 ON 時に ConfirmDialog で 1 度だけ強制確認』も入れるか? (critique UX #5)
- PromptEachTime → Remember に切替えた直後の挙動: (a) ConfirmDialog で『今すぐパスワードを設定しますか?』と聞き Yes で PasswordDialog 起動、(b) 次回ドロップ時に初回保存フローを発火、のどちらを採用するか? デフォルトは (b) を推奨 (critique UX #3)。
- Remember モード中にバッチ圧縮 (50 アーカイブ等) 進行中、ユーザーが PromptEachTime に切替えた場合: (a) 進行中バッチをキャンセル、(b) 進行中バッチは既に取得した平文で完走、のどちらか? セキュリティ重視なら (a)、UX 重視なら (b)。デフォルトは (a) を推奨 (critique security #15)。
- EncryptFileNames のデフォルト値: 設計仕様 2 では『パスワード保護 ON にした瞬間 ON』だが、永続値としてはユーザーが OFF に変更した最終状態を保持するか、毎回 IsPasswordProtectionEnabled の OFF→ON 遷移で強制 ON にリセットするか? デフォルトは『最終状態を保持』を推奨 (ユーザー意図尊重)。
- サポート ZIP に dumps/ ディレクトリを含めるか否か: Remember モード有効時のみ『機密性の高い情報が含まれる可能性がある』同意プロンプトを挟むのか、Remember モード時は無条件で dumps を除外するか? デフォルトは『同意プロンプト』を推奨 (診断価値の保全)。
- GitHub Releases への踏み台 publish (Velopack 旧クライアント救済) はこの変更で必要か? settings.json の schema 拡張は『新フィールドの追加』のみで後方互換なので、旧バージョンが新 settings.json を読んでも未知フィールドは無視される (JsonSerializer 既定挙動)。逆 (新バージョンが旧 settings.json を読む) は 3-stage fallback で吸収できるので踏み台は不要、という前提でよいか?
- v1.0.181 リリースで圧縮設定タブを開いたことがないユーザーへの『新機能あるよ』通知: (a) リリースノートと README のみ、(b) 初回起動時に 1 度だけ簡易通知バナーを表示する仕組みを追加、のどちらか? デフォルトは (a) を推奨 (実装コスト最小)。
