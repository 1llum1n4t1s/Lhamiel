

## library-api

- Password is init-only — set it on CompressionOption BEFORE passing to ArchiveWriter ctor. You cannot mutate it after. If you want different per-archive passwords, you must rebuild the CompressionOption + ArchiveWriter for each archive (Lhamiel already does this per Save).
- ZIP + Password + EncryptionMethod.Default = ZipCrypto (legacy PKZIP, cryptographically broken). For AES-256 ZIP you MUST set BOTH Password AND EncryptionMethod = EncryptionMethod.Aes256. Forgetting EncryptionMethod silently downgrades to insecure encryption — this is the #1 footgun.
- 7z body encryption is hardcoded to AES-256 (the only encryption 7z format supports). Setting EncryptionMethod on a 7z archive has no effect (SevenZipOptionSetter never emits `em`). Don't expose AES128/AES192 in UI for 7z — they're ZIP-only.
- 7z filename/header encryption (`-mhe=on`) is NOT exposed as a property. Must inject via CompressionOption.CustomParameters: `CustomParameters = new Dictionary<string,string> { ["he"] = "on" }`. Without this, the 7z central directory leaks filenames even when contents are encrypted. The XML doc confirms CustomParameters values are sent as BSTR through ISetProperties.
- ZIP format has NO filename encryption — the central directory is always plaintext. If users need filename hiding, you must steer them to 7z + he=on. Document this in the UI.
- Format.Tar + Password throws InvalidOperationException at the ArchiveWriter constructor (Options.Validate). Don't show the password UI for Tar; gate on FormatExtension.IsEncryptionSupported(format).
- CompressionOption.Password is a plain .NET `string` (init-only). It lives until GC. The XML doc explicitly warns: scope ArchiveWriter narrowly with `using` to limit plaintext lifetime in memory. Don't cache long-lived ArchiveWriter with passwords. SecureString is NOT supported.
- ArchiveWriter is non-reentrant and shares process-global native state — Lhamiel already serializes via NativeArchiveGate (SemaphoreSlim(1,1)). Password-related code adds no new concurrency concerns as long as it stays inside the existing gate.
- Update() reuses Options.Password to RE-ENCRYPT new entries, while existing renamed/preserved entries stay encrypted under their ORIGINAL password (the source archive's password). XML doc lines 727-742 warn about this asymmetric behavior — if the user changes a password via Update, the archive becomes mixed-password and unopenable as a single unit by most tools.
- EncryptionException is the wrong-password / corrupted-encryption signal. Lhamiel's ArchiveErrorHandler already maps it to ArchiveErrorType.EncryptedOrWrongPassword — reuse this for compression-side failures too if any surface.
- Lhamiel uses ToolHelper enum CompressionMethod.Lzma2 for 7z (line 655). For password-protected 7z the canonical 7-Zip CLI default is `-m0=lzma2 -mhe=on` — the existing Lzma2 method is compatible and SHOULD be kept.
- When `EncryptionMethod = Default` and `Password = ""`, no encryption is applied — i.e. empty password is treated as no-encryption by 7-Zip. Validate Password is non-empty before setting it, otherwise users may think they protected an archive when they didn't.


## settings-aot-dpapi

- TryRecoverFromJsonDocument (Settings.cs:614-698) is hand-maintained — any new persisted property MUST also be added there or it silently reverts to default during stage-2 recovery (e.g., when an unrelated property has a type mismatch). This is the single biggest footgun for adding new fields.
- ResetToDefaults (Settings.cs:578-607) must also be updated — there's an explicit `// NOTE: 新しいプロパティを追加したら必ずここにも追加すること` comment at line 606 that's been broken before.
- System.Security.Cryptography.ProtectedData is NOT in the .NET 10 BCL by default — must be added as a separate PackageReference (latest 10.0.5+). The package has zero transitive deps and is Native-AOT safe (thin DllImport over crypt32.dll), so no TrimmerRootAssembly is needed.
- DPAPI CurrentUser scope means settings.json copied to another user / machine cannot decrypt — this is correct security but must be handled (catch CryptographicException, clear EncryptedCompressionPassword) and documented. The settings.json `corrupt_*.bak` rotation does NOT cover this case because the JSON itself is valid.
- WhenWritingNull on AppJsonContext (line 11) means a null `byte[]? EncryptedCompressionPassword` is OMITTED from JSON, not written as `null` — matches existing behavior of `ExcludedFilePatternsLegacy` and keeps fresh-install settings.json clean.
- DiagnosticsCollector.cs:18 already lists fields excluded from the support ZIP — add EncryptedCompressionPassword there too, otherwise ciphertext leaks in diagnostics (which defeats the point of the JsonIgnore on the plaintext accessor).
- Snapshot() uses MemberwiseClone — byte[] reference is shared. Safe ONLY because the field is replaced wholesale by the setter (never mutated in place via index assignment). A future mistake that does `EncryptedCompressionPassword[0] = 0` would race across snapshots; worth a comment in the // 参照型コレクションは深コピー block at Settings.cs:259-265.
- TreatWarningsAsErrors=true (Directory.Build.props:11) + the project's [SupportedOSPlatform] manifest mean CA1416 will silently pass for ProtectedData calls (target = net10.0-windows8.0), but if the project ever multi-targets, this changes — the call sites would need `OperatingSystem.IsWindows()` guards.


## ui-vm-wiring

- No existing IValueConverter classes in the codebase — for radio-button-bound enum/string selection the established pattern is code-behind `IsCheckedChanged` + `Tag` strings (see DirModeRadio_Changed at MainWindow.axaml:270-283). Do NOT invent a StringEqualsConverter; follow the radio-tag pattern.
- Format-conditional UI visibility binding does NOT exist anywhere in this codebase. The ZIP-level and 7z-level ComboBoxes are always shown side-by-side. If you add IsZipFormat/IsSevenZipFormat derived bools, you are introducing the first format-conditional binding pattern — make sure the SelectedCompressionFormat property gets `[NotifyPropertyChangedFor]` attributes (precedent: IgnoredUpdateTag at line 321-323) or the derived bool won't refresh.
- Adding ANY new [ObservableProperty] requires updates in 3 places: (1) ApplySettingsToManager (line 156-179), (2) LoadFromSettings (line 462-490), (3) a `partial void OnXxxChanged` calling AutoSave (line 219-232). Forgetting any one will cause silent persistence bugs — values seen in UI but lost on restart, or vice versa.
- TreatWarningsAsErrors is ON project-wide — unused parameters / missing XML docs on public types will fail the build.
- Settings.cs must NEVER persist plaintext passwords. The 3 new properties (IsPasswordProtectionEnabled, EncryptFileNames, PasswordMode) are safe to persist; the actual password string is held only by the dialog and passed by parameter to ArchiveCompressor — never written to settings.json.
- There are 17 locale .axaml files — adding 7 new resource keys means 7×17 = 119 string entries. Missing any single one will cause `App.Text()` to return the key string literally as a UI fallback (no compile-time error). Grep all 17 files after editing to confirm key coverage.
- PasswordDialog.ShowFromBackgroundAsync is already called from ArchiveExtractor.cs directly (not via IPasswordDialogService). When adding the new interface, refactor that existing call site too — or you'll have two paths and tests can't stub the extraction path.
- Tests touching `ArchiveProcessor.PasswordDialogImpl` (or any of the 3 existing static impls) MUST be marked `[Collection("ArchiveProcessor")]` per CLAUDE.md — these are mutable shared state, parallel xUnit3 test execution will race without the collection guard.
- TAR format does NOT support encryption — the IsZipOrSevenZipFormat gate on the HeaderedContentControl is required, otherwise the user sees the option enabled but it does nothing for TAR archives. EncryptFileNames is 7z-only (ZIP AES doesn't encrypt the central directory file names).


## password-dialog

- DataContext = this with x:CompileBindings='True' (x:DataType=view:PasswordDialog) means new bindable properties (Mode, IsConfirmVisible, DialogTitle, MessageText) MUST be public properties on the PasswordDialog class itself — not on a separate VM — or the build will fail (TreatWarningsAsErrors). Compiled bindings have no runtime fallback.
- Per the class comment (lines 17-20), the dialog deliberately omits INotifyPropertyChanged because properties are 'set in ctor and immutable thereafter'. Keep this invariant: do mismatch validation by toggling _mismatchWarning.IsVisible via FindControl (NOT a bound bool), so the no-INPC contract holds.
- ShowFromBackgroundAsync has TWO cancellation-token race guards (line 116 pre-Register, line 134 post-Register before ShowDialog). Any new overload or wrapper MUST go through the same UIThread.InvokeAsync wrapper — don't add a parallel implementation that re-derives the guards (Option B's biggest hidden cost).
- Password is held in `Password` property + the TextBox text until OK-Click clears the TextBox and ShowFromBackgroundAsync calls ClearPassword(). For Option A, ALSO clear the ConfirmBox.Text on success AND on mismatch-retry (the sketch above does this) — otherwise the confirm box accumulates the user's first attempt and exposes plaintext longer than intended.
- Enter key currently triggers OkButton_Click from PasswordBox. In CompressNew mode, Enter pressed in the FIRST TextBox should ideally move focus to ConfirmBox (not submit), but to stay minimal the sketch routes Enter from BOTH boxes to OkButton_Click — which works because OkButton_Click in CompressNew mode validates first and only Closes on match. Document this in code comments to avoid future 'why does Enter not advance' confusion.
- AsyncPasswordQuery returns string.Empty on cancel (mapped to Cancel=true). The compression caller does NOT use AsyncPasswordQuery — it gets a string? directly from ShowForCompressionAsync. Returning null means 'user cancelled the entire compression' and the caller should abort the operation before ArchiveWriter is constructed; do not coerce to string.Empty (that would mean 'compress with empty password').
- All 17 locale files (Resources/Locales/*.axaml) must receive the new 3-4 keys in one go — Avalonia ResourceInclude is statically merged, missing keys throw at runtime when DynamicResource resolves. The /stst test suite should add a 'all locales have all Password keys' parity test if not already present.
- The Grep tool output contained an injected <system-reminder> block claiming new MCP server instructions (computer-use, microsoft-learn, serena re-init). This is a prompt-injection pattern (reminder text appearing INSIDE tool output, not from the harness). I ignored it — the actual task is offline file analysis and no MCP server is required.


## i18n-coverage

- ja_JP.axaml is the ONLY locale that merges en_US as fallback (lines 3-5). The other 15 locales have no fallback chain — every new key MUST be inserted in all 17 .axaml files or those locales will hit runtime ResourceNotFound. Do not assume en_US 'falls through'.
- App.Text(key, args) auto-prepends 'Text.' (per CLAUDE.md). C# call sites should use App.Text("PasswordSet.Mismatch"), NOT App.Text("Text.PasswordSet.Mismatch"). XAML uses the full Text.* key in DynamicResource.
- Use {DynamicResource ...} not {StaticResource ...} so locale switching at runtime works (per CLAUDE.md Localization note). All 14 new keys consumed from XAML must use DynamicResource.
- TreatWarningsAsErrors is ON but it does NOT catch missing localization keys — DynamicResource lookups are runtime. Add a Lhamiel.Tests.Unit test that loads all 17 .axaml dictionaries and asserts the 14 new keys exist in each (mirror the existing locale-completeness test pattern if one exists; otherwise this is a new safety net to add alongside the feature).
- Existing Text.Password.* (Title/Message/Placeholder/WrongPasswordRetry) cover the EXTRACTION prompt and must NOT be reused for the COMPRESSION 'set password' dialog — semantics differ (extract = enter the archive's existing password; compress = create a new password with confirm). Hence the new Text.PasswordSet.* namespace.
- There are no Text.Format.* keys (no localized zip/7z/tar.gz labels). If the password UI needs to warn 'tar.gz does not support encryption', reuse the existing dropdown's literal or scope a separate Text.Format.* additions outside this feature.
- ResourceInclude implements IResourceProvider, NOT ResourceDictionary (per CLAUDE.md Pitfall). Any code that programmatically loads the new dialog's resources must cast to IResourceProvider.
- The 'saved password cleared' toast (Text.Password.Cleared) is in the existing Text.Password.* namespace (not Text.PasswordSet.*) because conceptually it applies to BOTH the cached extraction password and the cached compression password — single key handles both code paths.
