

## security

- **[blocker]** stage-2 JsonDocument recovery silently drops EncryptedCompressionPassword when any unrelated property is malformed
   - detail: Settings.cs:614-698 TryRecoverFromJsonDocument is hand-maintained — it explicitly enumerates every persisted property via TryGetString/TryGetBool/TryGetInt/TryGetEnum. The Phase 1 design proposes appending a Base64 byte[] handler for EncryptedCompressionPassword (Settings.cs:631-area, optional). If the developer forgets that one line (consistent with the existing `// NOTE: 新しいプロパティを追加したら必ずここにも追加する
   - fix: Make this fail-loud, not fail-silent: (a) MANDATORILY add the Base64 handler in TryRecoverFromJsonDocument before merging the feature (don't mark it 'optional'); (b) add a unit test that round-trips a Settings with EncryptedCompressionPassword non-null through a SIMULATED stage-2 recovery (deliberately corrupt one OTHER property, force stage 2, assert the blob survives); (c) in stage 2, if root.Tr
- **[blocker]** DiagnosticsCollector support ZIP leaks the encrypted password ciphertext (regex masks only strings on properties named ~password)
   - detail: DiagnosticsCollector.cs:191-202 ShouldMask uses regex `(?i)(token|secret|password|key|credential|apikey|api_key)`. The proposed property name `EncryptedCompressionPassword` matches `password` substring → regex hit. **However**, the property is a `byte[]` which System.Text.Json serializes as a JSON STRING containing Base64. WriteElement (line 170-176) at JsonValueKind.String DOES then call ShouldMa
   - fix: (a) Explicitly add `EncryptedCompressionPassword` to `_sensitiveKeys` array in DiagnosticsCollector.cs:22, and update the comment block at lines 16-21 to call out compression-password fields. Do NOT rely on the regex alone. (b) Add a unit test `DiagnosticsCollector_MasksEncryptedCompressionPassword` asserting `***` appears in the masked JSON for that property. (c) For dumps: either EXCLUDE dumps f
- **[blocker]** Confirm-per-drop mode wipe is not atomic — wipe-then-crash before Save() flush leaves the encrypted password on disk
   - detail: The design says 'Confirm-per-drop mode clears any previously saved password'. The natural implementation path in MainWindowViewModel is: when PasswordMode toggles from 'Remember' → 'PromptEachTime', call something like `settings.EncryptedCompressionPassword = null` (in-memory) → AutoSave() debounce 300ms → ExecuteAutoSaveAsync → SettingsManager.Save → Settings.Save → WriteAtomically. There are THR
   - fix: (a) Use `SettingsManager.MutateAndSave` (SettingsManager.cs:74-83) — already designed for atomic 'mutate + persist in one lock' — for the mode-switch transition. Do NOT route mode-switch through the debounced AutoSave. Write a dedicated `MainWindowViewModel.OnPasswordModeChanged` handler that calls `SettingsManager.Instance.MutateAndSave(s => { s.PasswordMode = newMode; if (newMode == 'PromptEachT
- **[blocker]** ZIP+Password without EncryptionMethod=Aes256 silently produces broken ZipCrypto encryption
   - detail: Phase 1 library investigation (gotcha #2) explicitly flags this as 'the #1 footgun': if `CompressionOption.Password='x'` is set but `EncryptionMethod` is left at `Default`, ZipOptionSetter.AddEncryptionMethod does NOT emit `em`, and 7-Zip falls back to legacy PKZIP ZipCrypto which is cryptographically broken (known-plaintext attack, brute-forced in seconds with `bkcrack`). Reviewing the proposed A
   - fix: (a) Add an assert in CreateArchiveWriter for ZIP+password: `if (format == Format.Zip && !string.IsNullOrEmpty(password) && options.EncryptionMethod != EncryptionMethod.Aes256) throw new InvalidOperationException("ZIP encryption must be AES-256");` — fail fast in dev/test. (b) Add a unit test `CreateArchiveWriter_ZipWithPassword_UsesAes256` that constructs an encrypted ZIP, opens it with `bkcrack` 
- **[high]** Empty/whitespace password silently produces UNENCRYPTED archive (7-Zip treats Password="" as 'no encryption')
   - detail: Phase 1 library investigation (gotcha #12) confirms: when `EncryptionMethod = Default` AND `Password = ''`, 7-Zip applies NO encryption. The user enables 'Protect archive with password' in the UI, types nothing (or hits space-Enter accidentally), presses OK. If the design doesn't reject empty/whitespace early, the UI says 'password-protected' but the archive is a plain unencrypted ZIP/7z that 7-Zi
   - fix: (a) In PasswordDialog.OkButton_Click for CompressNew mode, reject empty/whitespace before validating mismatch: `if (string.IsNullOrWhiteSpace(_passwordBox.Text)) { show 'enter a password' warning; return; }`. (b) Defense in depth: in ArchiveCompressor.CompressAsync, if `IsPasswordProtectionEnabled` is true AND the password parameter is null/empty/whitespace, throw `ArgumentException` BEFORE creati
- **[high]** Plaintext password lifetime in GC heap is much longer than 'narrow scope' claims — survives in interned/cached string pool
   - detail: PasswordDialog.axaml.cs:38-49 documents the threat model honestly: 'SecureString is deprecated; we minimize lifetime by clearing the dialog reference'. But the Phase 1 design extends this lifetime SIGNIFICANTLY: (1) Settings.CompressionPassword getter returns `Encoding.UTF8.GetString(plain)` — a fresh string. The setter at proposed Settings.CompressionPassword takes a `string value` parameter, cal
   - fix: (a) Document the actual lifetime in code — replace the optimistic comment at PasswordDialog.axaml.cs:38-49 with the truth: 'Best-effort. Plaintext lives in GC heap until next GC. Avoid persisting Always-Save mode unless trust boundary allows.' (b) Reduce # of plaintext copies: cache the decrypted password ONCE per compression batch (not per archive) — pass it via parameter, not via Settings proper
- **[high]** DPAPI CurrentUser scope: settings.json copied to another machine/user means CryptographicException on Unprotect — but the proposed catch silently clears the blob, hiding the failure mode and triggering AutoSave loss
   - detail: Phase 1 settings-aot-dpapi sketch suggests: `catch (CryptographicException) { Debug.WriteLine(...); EncryptedCompressionPassword = null; return null; }`. Three problems: (1) The mutation inside a GETTER is unusual and dangerous — calling `var pw = settings.CompressionPassword` is a side-effecting operation that nullifies EncryptedCompressionPassword. If the user does this on a fresh-install where 
   - fix: (a) Move the CryptographicException handling OUT of the getter into the caller. The getter should THROW; the call site should explicitly decide whether to clear, prompt-retry, or surface to UI. The proposed `catch + clear + return null` pattern is the WRONG default. (b) On first CryptographicException, surface a one-time dialog: 'Your saved compression password could not be decrypted (this happens
- **[high]** Logger captures ex.Message in 7 places in ArchiveCompressor — if the 7z.dll surfaces password in an exception message (rare but observed in zlib/7z stacks), it ends up in Lhamiel_yyyyMMdd.log
   - detail: ArchiveCompressor.cs lines 185, 230, 259, 300, 514, 707, 947, 1025, 1030 all log `ex.Message`. ArchiveErrorHandler.cs:166, 187, 203 do the same. These are written by Logger.Log to `%LocalAppData%\Lhamiel\Lhamiel_yyyyMMdd.log` (unencrypted, plaintext file, world-readable by the user account). Survey of 7-Zip/SevenZipSharp exception flow: most exceptions are HRESULT-based and don't carry user input.
   - fix: (a) Add a Logger-level allowlist: refuse to log strings containing the active password. Implement via an `internal static string? _activePasswordRedactionToken` set when password is in use, cleared in finally. Logger.Log scans message for that string and replaces with `***`. Best-effort but catches accidental logging. (b) Code review gate: add a CI grep that fails the build if `Logger.Log` lines r
- **[high]** ResetToDefaults does not clear EncryptedCompressionPassword — user clicks 'Reset settings to default' and the saved password survives
   - detail: Phase 1 settings-aot-dpapi sketch explicitly notes 'add `EncryptedCompressionPassword = null;` to ResetToDefaults'. But the explicit `// NOTE: 新しいプロパティを追加したら必ずここにも追加すること` comment at Settings.cs:606 has been BROKEN BEFORE per the project's own track record (see history). If this is forgotten, clicking 'Reset to defaults' from the Settings UI restores every other field but leaves the encrypted passw
   - fix: (a) Mandatory addition of `EncryptedCompressionPassword = null; PasswordMode = "PromptEachTime"; IsPasswordProtectionEnabled = false; EncryptFileNames = false;` to ResetToDefaults — make it part of the same commit, gated by a test. (b) Add an explicit unit test `Settings_ResetToDefaults_ClearsAllSecrets` that asserts EncryptedCompressionPassword is null AFTER ResetToDefaults. (c) Consider refactor
- **[high]** Settings.json corruption fallback writes empty `{}` and discards the ciphertext — irrecoverable loss with no warning to user
   - detail: Settings.cs:343-345 stage-3 fallback: when stage 2 (JsonDocument recovery) also fails, the file is moved to .corrupt_*.bak (or deleted, or overwritten with `{}`). The .bak file CONTAINS the original ciphertext. Two problems: (1) The .bak file is left in `%LocalAppData%\Lhamiel\` indefinitely. If a future support engineer asks 'send me your AppData', the user zips the entire directory — both the ne
   - fix: (a) When stage 3 runs, surface a one-shot UI notification at next app launch: 'Settings file was unreadable and reset to defaults. If you had a saved compression password, please re-enter it from Settings.' Use a persisted `_pendingCorruptionWarning` flag in a separate small file. (b) DiagnosticsCollector should EXCLUDE `settings.json.corrupt_*.bak` files from the support ZIP (DiagnosticsCollector
- **[medium]** Settings.Snapshot MemberwiseClone shares byte[] reference — racing thread mutation of EncryptedCompressionPassword[0]=0 would corrupt all snapshots
   - detail: Settings.cs:266-272 Snapshot() uses MemberwiseClone which shares the `byte[]` reference. The Phase 1 author warns about it: 'safe ONLY because the field is replaced wholesale by the setter (never mutated in place)'. But the proposed CompressionPassword setter does `EncryptedCompressionPassword = ProtectedData.Protect(plain, ...)` — replaces, OK. HOWEVER: (a) Future dev who reads the code adds an '
   - fix: (a) Mark the byte[] as 'immutable contract — never mutated in place' with a `[Pure]`/`<remarks>` warning AND a unit test that asserts the byte[] returned from Snapshot has reference equality with the original (catches accidental copy). (b) Better: change the type to a wrapper struct `ImmutableArray<byte>` (System.Collections.Immutable) — compile-time prevents in-place mutation. (c) Add an explicit
- **[medium]** 7z header encryption (he=on) via CustomParameters — if forgotten when 'Encrypt file names' is ON, central directory leaks filenames
   - detail: Phase 1 library investigation explicitly notes: 7z header encryption is NOT a first-class CompressionOption property — it must be injected via `CustomParameters = new Dictionary<string,string> { ["he"] = "on" }`. The design constraint says 'Encrypt file names defaults to ON when password protection is ON' for 7z. If a developer wires only `CompressionOption.Password = pwd` without setting CustomPa
   - fix: (a) Mandatory: in ArchiveCompressor.CreateArchiveWriter, for `format == Format.SevenZip && isPasswordProtectionEnabled && encryptFileNames`, set `CompressionOption.CustomParameters = new Dictionary<string,string> { ["he"] = "on" }`. (b) Add a unit test that creates a password-protected 7z with EncryptFileNames=true, opens it with `7z l archive.7z` (no password) and asserts it FAILS with 'Cannot op
- **[medium]** PasswordDialog Enter-key shortcut auto-submits with whitespace match — confirm-twice bypass when user double-presses Enter
   - detail: PasswordDialog.axaml.cs:87-94 PasswordBox_KeyDown maps Enter → OkButton_Click. The Phase 1 Option A sketch routes Enter from BOTH _passwordBox AND _confirmBox to the same handler. Race scenario: user types password into _passwordBox, hits Tab (focus moves to _confirmBox), starts typing — IME composition begins (Japanese/Chinese input). User accidentally hits Enter to COMMIT the IME composition (wh
   - fix: (a) In CompressNew mode, change PasswordBox_KeyDown to MOVE FOCUS to _confirmBox instead of submitting. Only submit from _confirmBox.KeyDown→Enter. (b) Block Enter on both boxes during IME composition: `if (e.Source is TextBox tb && tb.IsImeComposing) return;` (Avalonia exposes IME state). (c) Combined with the empty-password fix above (severity high), require non-empty in OkButton_Click before an
- **[medium]** Extremely-long password handling: Settings ciphertext can balloon JSON file, no SanitizeAfterLoad clamp
   - detail: Phase 1 sketch suggests an optional 'SanitizeAfterLoad can clamp >4096 bytes'. Without it, an attacker who gets write access to settings.json could plant a 100MB Base64 byte[] in EncryptedCompressionPassword. Next Settings.Load reads the entire file via `File.ReadAllText` (Settings.cs:309) which is unbounded. With a 100MB string in memory, the subsequent JsonSerializer.Deserialize may OOM or hang.
   - fix: (a) Add a hard cap on password length at the UI layer: `_passwordBox.MaxLength = 1024` (1KB is more than any reasonable password). (b) In Settings.SanitizeAfterLoad, if `EncryptedCompressionPassword != null && EncryptedCompressionPassword.Length > 4096`, clear it AND log a Warning 'Encrypted password blob suspiciously large — discarded'. (c) In the CompressionPassword setter, validate `value.Lengt
- **[medium]** Update() with mode-switch from Remember→PromptEachTime mid-batch: cached plaintext password may be reused after wipe
   - detail: Plausible UX path: user starts batch compression of 50 files in 'Remember' mode → ArchiveProcessor reads the password ONCE → starts compressing → user opens Settings mid-batch and toggles to 'PromptEachTime' → MutateAndSave wipes EncryptedCompressionPassword. The in-flight batch still holds the plaintext password in a local variable (captured by the Task.Run lambdas). The remaining 30 archives enc
   - fix: (a) When PasswordMode toggles to PromptEachTime mid-batch, signal a CancellationToken to the in-flight batch so it pauses and re-prompts for the next archive. (b) Document the behavior either way — but defaulting to 'continue batch with cached password' is the wrong default. Cancel-batch-on-mode-change is the safer default for a security-sensitive operation. (c) Add an integration test that starts
- **[low]** WinZip vs PKWARE AES variants: 1llum1n4t1s.Sevenzip emits WinZip-style AES-256 for ZIP, incompatible with some PKWARE-only readers
   - detail: ZIP AES-256 has two on-disk encodings: WinZip AE-2 (the 7-Zip/native default, no CRC stored separately, magic 0x9901) and PKWARE Strong Encryption (older, magic 0x9900 with separate CRC). 7-Zip emits WinZip AE-2 by default. Most modern readers (7-Zip, WinRAR, modern PowerShell Expand-Archive on Windows 11 23H2+, macOS Archive Utility) handle WinZip AE-2 fine. Older readers: built-in Windows Explor
   - fix: (a) Update the Phase 1 i18n description string from 'When ON, ZIP uses AES-256' to 'When ON, ZIP uses AES-256 (WinZip AE-2). May not open on older Windows/Linux extractors — recipients can use 7-Zip.' in en_US, with corresponding ja_JP translation. (b) In CLAUDE.md document the AE-2 variant choice as a known limitation. (c) Consider exposing a 'compatibility mode' radio in the future (ZipCrypto vs
- **[low]** Password not passed via command-line / process args (verified) — but Velopack --update-check path inherits env, document
   - detail: Verified: no `Process.Start` with password in args anywhere in the codebase. `vpk pack` and `dotnet publish` are build-time only and never see runtime passwords. The Velopack `--update-check` CLI path (Program.cs --update-check from StartupRegistration HKCU\Run) launches Lhamiel.exe with no password args. `IpcService` does NOT currently transmit a password field. **However**: if a future feature a
   - fix: (a) Add a CLAUDE.md note: 'Compression password MUST NEVER be passed via Process.Start arguments, environment variables, or named-pipe IPC messages in plaintext. If a CLI compression mode is added, accept the password via STDIN read (console hidden input) or a per-process named ephemeral pipe with DACL restricting to the spawning user.' (b) Add a CI grep that fails on `Process.Start.*password` or 


## ux

- **[blocker]** ZIP + ファイル名暗号化チェック ON で「保護したつもり」事故が起きる（ZIP 仕様上不可能なのに UI 上は ON 状態で残る）
   - detail: Phase 1 ライブラリ調査の通り、ZIP フォーマットは中央ディレクトリのファイル名を暗号化する手段がない（ZipOptionSetter は `he` プロパティを emit しない／7-Zip ZIP ハンドラも非対応）。XAML 提案では EncryptFileNames CheckBox を `IsEnabled="{Binding IsSevenZipFormat}"` でグレーアウトはするが、IsChecked の値はそのまま保持される。よって『7z で ON → ZIP に切替 → グレーアウトされたが ✓ のまま → ユーザーは「ファイル名は隠れる」と認識したまま配布』が起きる。AutoSave で永続化もされるため再起動後も誤認は続く。GUI 上 ✓ なのに実アーカイブはファイル名丸見え、というのはセキュリティ機能の最悪パターン。
   - fix: (1) SelectedCompressionFormat の change handler で「ZIP / Tar に切り替わったら EncryptFileNames を強制 false にし、ユーザーに次回 7z 復帰時に再度 ON にする必要がある旨を一度だけ通知する（ConfirmDialog で『ZIP はファイル名を暗号化できません。設定を OFF にしました』）」。または (2) チェックボックスを残しつつ、ZIP 選択時はその直下に赤系の小さな TextBlock で『ZIP では実行されません』と動的説明を出す（IsSevenZipFormat=false のとき IsVisible=true）。グレーアウトだけは弱すぎる。
- **[blocker]** always-save → confirm-per-drop 切替で保存パスワードを警告なし即時破棄するのは破壊操作（取り消し不能）
   - detail: 設計仕様『confirm-per-drop モード切替時に保存パスワードを wipe』はセキュリティとして正しいが、UI イベント駆動で無警告にやると事故率が高い。RadioButton クリックは誤クリック頻発操作（Tab + Space で迷い操作も含む）。長文パスワードを保存していた場合、切替 1 クリックで二度と復元不能な値を消すのは『git push --force』クラスの破壊操作。CLAUDE.md の §破壊的・不可逆操作と同じ扱いをすべき。さらに『切り戻したら戻る』錯覚を与えるが実際は wipe 済みで戻らない（confirm-per-drop → always-save に戻すと空欄になる）。
   - fix: (1) RadioButton.IsCheckedChanged ハンドラで、保存パスワードが存在する場合のみ ConfirmDialog（既存）で『保存中のパスワードを削除します。続行しますか？』を表示し、Yes でのみ wipe + モード変更、No で RadioButton を元に戻す（_isLoading 同様のガードフラグで反応抑制ループを防ぐ）。保存パスワードが空ならノー警告で即切替（実害ゼロ）。(2) wipe 後に短時間（例: そのセッション中のみ）の Undo を提供すると更に親切（メモリ上に最後の暗号化バイト列を一時保持し、即座に切り戻したら復元）。コストが高ければ (1) のみで可。
- **[high]** confirm-per-drop → always-save 切替時、いつどこでパスワードを設定するか未定義
   - detail: 仕様には『always-save: パスワード保存』『confirm-per-drop: 都度確認』しか書かれておらず、『切替直後はまだパスワード未設定』という空状態の遷移先が空白。ユーザー期待値は (a)『切替直後に設定ダイアログが開く』か (b)『次回ドロップ時に確認2回入力 → そこから保存』のどちらか。何も実装しないと『always-save に切り替えたのに次のドロップで毎回ダイアログが出る → 動かない』と認識される。
   - fix: 切替時に EncryptedCompressionPassword が null/empty なら、ConfirmDialog で『パスワードを今すぐ設定しますか？（後で次回圧縮時にも設定できます）』を提示。Yes → PasswordDialog(CompressNew モード) を即時表示し、入力 → DPAPI で暗号化して Settings に保存。No → 次回ドロップ時に CompressNew モードで 2 回入力 → 保存。後者の場合は『パスワード未設定』ラベルを RadioButton 横に薄く出す（『パスワード未設定（次回圧縮時に設定）』）。動作が予測可能になる。
- **[high]** ドロップ中にダイアログが開いている間、2 個目のドロップが来た時の挙動が未定義（重複ダイアログ／ロスト／レース）
   - detail: ProcessDroppedPathsAsync は ViewModel の単一エントリで処理されているが、Drop イベントは UI スレッドで非同期に複数回入りうる。1 個目で PasswordDialog（CompressNew, ShowDialog<bool>）を await 中に 2 個目がドロップされると、(a) 別の Window で 2 個目のダイアログが開く（ShowDialog<bool> の owner は MainWindow なので 2 重 modal は Avalonia では undefined）、(b) 1 個目の処理がまだ ArchiveCompressor に達しておらず NativeArchiveGate も取られていないのでガードが効かない、(c) 2 個目の処理が即座に進んで先に NativeArchiveGate を取ってしまう可能性。UX 的
   - fix: MainWindowViewModel に `_isAwaitingPasswordInput`（int、Interlocked）を追加し、true の間 DropZone_Drop を早期 return（DragOver は AllowedEffects=None でフィードバック）。または ConfirmDialog で『現在パスワード入力待機中です。完了後に再度ドロップしてください』を出す（多くの場合は単純無視で十分）。OptionA で済む。実装は ViewModel.IsBusy 風のフラグで MainWindow.DropZone_DragOver / DropZone_Drop ガード。
- **[high]** ZIP のファイル名暗号化 OFF（仕様上強制 OFF）に関する説明がユーザーに伝わらない → 7-Zip でしか開けない archive を量産
   - detail: ZIP + AES-256 で作ったアーカイブは Explorer のビルトイン ZIP（PKZIP のみ対応）で開けない。これは naive ユーザー（メールで配布する家族・取引先など）にとっては「アーカイブが壊れている」事故。ファイル名暗号化のチェック以前に、AES-256 ZIP 自体が Windows 標準展開と非互換。ConfirmDialog で『ZIP の AES-256 暗号化は Windows 標準のエクスプローラーでは展開できません。受け取り側にも 7-Zip / WinZip / WinRAR が必要です。続行しますか？』を初回 ON 時に出すか、説明 TextBlock に明記すべき。
   - fix: Text.Settings.Compression.EnablePasswordDescription を ZIP/7z 別の説明に分岐。ZIP のとき: 『ON のとき ZIP は AES-256 で暗号化されます。受け取り側には 7-Zip / WinZip / WinRAR 等が必要です（Windows 標準のエクスプローラーでは展開できません）』。7z のとき: 『ON のとき AES-256 で暗号化されます』。SelectedCompressionFormat に [NotifyPropertyChangedFor(nameof(PasswordDescriptionText))] を追加し、derived property で出し分け。初回 ON 時に一度だけ ConfirmDialog で詳細を出すと事故率がさらに下がる。
- **[high]** 確認入力の Cancel で writer 状態が中途半端になる可能性（仕様分離が必要）
   - detail: ArchiveCompressor.CreateArchiveWriter は CompressionOption.Password を init-only で受け取る前提なので、パスワード取得が writer インスタンス生成より前に完了する必要がある。現状の ProcessDroppedPathsAsync フローを見るに、ArchiveProcessor → ArchiveCompressor.CompressAsync → CreateArchiveWriter → writer.Save が一直線。パスワード入力が ArchiveProcessor 入口（CreateArchiveWriter より前）で完了していれば temp files / writer は未生成なので Cancel = ノーリスク。逆に、もし誰かが将来 writer を先に作って Password を後付け
   - fix: ArchiveProcessor の圧縮分岐の冒頭（NativeArchiveGate 取得『前』）でパスワード取得を完了させる契約を明文化し、ArchiveCompressor.CompressAsync の引数に password を String? として渡す。Cancel 時はその時点で NotifyCanceled() を返してフロー全体を early return、ArchiveCompressor も呼ばない（writer 生成しない、temp files 作らない）。CLAUDE.md の §Compressor 説明に 1 行『パスワード取得は NativeArchiveGate 取得前に行う』を追記して将来のリファクタ事故を予防。
- **[high]** TAR 形式選択で password 全ブロックが IsEnabled=false になるだけでは『なぜ無効なのか』が伝わらない
   - detail: Phase 1 ui-vm-wiring の XAML 提案は `IsEnabled="{Binding IsZipOrSevenZipFormat}"` で TAR 時にブロック全体をグレーアウト。ただ TAR がなぜ暗号化非対応かはユーザーに伝わらず、『ZIP/7z に戻したら ✓ が消えていてもう一度設定し直し』にも気づきにくい。FormatExtension.IsEncryptionSupported(format) でガードする精神は良いが、UI は理由を出さないと『バグ？』と思われる。
   - fix: ヘッダー直下に IsVisible="{Binding !IsZipOrSevenZipFormat}" の小さな TextBlock を置き、『TAR 形式は暗号化に対応していません。ZIP / 7z を選択するとパスワード保護が利用できます』と表示。さらに format 切替で IsPasswordProtectionEnabled の値は保持する（TAR ↔ ZIP の往復で ✓ が消えないように）— format 変更時に強制 OFF にすると逆に苛立つ。
- **[high]** PasswordDialog 表示中の locale 切替で文言が更新されない（Title / DialogTitle / MessageText が DynamicResource 経由でない or non-INPC）
   - detail: App.axaml の Text.LocaleChanged 仕組みは DynamicResource 経由のみリアルタイム更新。Phase 1 password-dialog 提案の `DialogTitle => App.Text(...)` getter-only プロパティは INPC を持たないため、ロケール切替イベントを受けても再評価されない（Window.Title binding は更新されない）。同じく MessageText も。既存 PasswordDialog は『開いている間に locale 切替する想定なし』だが、設定ダイアログから locale を切り替えるユースケースが既に存在（ja → en で UI を確認するなど）。
   - fix: PasswordDialog のクラスコメントが言う non-INPC 不変前提を破るのは避け、代わりに XAML で `Title="{DynamicResource Text.Password.Title}"` のまま固定 DynamicResource を使い、Mode に応じた key 切替は『コンストラクタで Title プロパティを書き換える』ではなく『XAML の TextBlock を Mode ごとに用意して IsVisible で出し分け』にする。例: Set モード用 TextBlock と Extract モード用 TextBlock を 2 個並べ、IsVisible で切替。これなら DynamicResource が常に最新キーを引く。Window.Title だけは startup のみ確定で OK（ロケール切替時にウィンドウタイトルバーが古いままなのは許容
- **[high]** 確認 mismatch 時に第 1 入力もクリアすると操作回数 2 倍（ユーザー激おこ事案）
   - detail: Phase 1 password-dialog の OkButton_Click 提案は mismatch 時に『confirm のみクリア、primary は保持』としており方針正しい。問題は実装ミスりやすいこと（最終コードで両方クリアにしてしまうと、長い generated password を毎回両方ペースト直しになる）。さらに『どちらが違ってるか分からない』というユーザー認識: confirm 側だけクリアしてフォーカスを confirm に置く方針は良いが、ユーザーが『primary が typo だった』と気づいた場合に primary 修正後に confirm 再入力が必要 → 両方クリアの方が誤解なくなる、という意見もある。
   - fix: Phase 1 提案通り『confirm だけクリア + confirm にフォーカス』を採用。さらに mismatch warning に『パスワード（1 つめ）に誤入力があれば編集できます』と短い hint を追記すると分かりやすい。両方クリアは絶対回避（特にパスワードマネージャからの粘着ペースト運用ユーザーを敵に回す）。テストで mismatch 後の primary.Text が保持されることを検証。
- **[high]** 空パスワード入力で Enter を許すと『暗号化なし』アーカイブが無言で生成される
   - detail: Phase 1 library-api gotcha: 『EncryptionMethod = Default かつ Password = "" は無暗号化扱い』。ユーザーが IsPasswordProtectionEnabled=ON にして PasswordDialog で Enter を即押し（空入力）すると、ArchiveCompressor は password="" を CompressionOption に渡す → 7-Zip は『パスワード無し』として処理 → ユーザーは『パスワードかけたつもり』。Explorer のプロパティを見るまで気付けない。
   - fix: PasswordDialog.OkButton_Click で `_passwordBox.Text` が string.IsNullOrEmpty なら mismatch warning と同じ位置に『パスワードを入力してください』を表示して Close しない。同様に CompressNew モードでは ConfirmBox も空チェック（mismatch チェックの前に空判定）。Settings 側でも防御層として ArchiveProcessor 直前に『IsPasswordProtectionEnabled && string.IsNullOrEmpty(password) → 暗号化なし圧縮にフォールバックせず例外 or 再プロンプト』のガード。
- **[high]** always-save モードでも IsRetry のフィードバックなし／パスワード忘れリカバリーパスが未設計
   - detail: always-save で保存中のパスワードが何らかの理由で復号失敗（別ユーザー/PC コピー、DPAPI master key 破損 → settings-aot-dpapi の Caveats(2)）になったとき、CompressionPassword getter は null を返し EncryptedCompressionPassword を自動 clear する設計。すると圧縮実行時に『あれ、保存してたはずなのに毎回確認が出る』状態になるが UI には何も理由が表示されない。さらに『パスワードを変更したい』機能パスがない（一度 confirm-per-drop に切替 → wipe → always-save に戻す → 設定し直し、という 3 ステップを暗黙的に強いる）。
   - fix: (1) Settings UI に『保存済みパスワードを変更』ボタンを追加（IsPasswordProtectionEnabled && PasswordMode=="Remember" && EncryptedCompressionPassword!=null のとき visible）。クリックで PasswordDialog(CompressNew) → 上書き保存。(2) Getter の CryptographicException 発生時に Logger.Log（Warning）+ 一度だけ MessageService.NotifyAsync で『保存していたパスワードを復元できなかったため再設定が必要です』通知。(3) 『保存済みパスワードを削除』ボタンも追加（破壊操作なので ConfirmDialog で確認）。
- **[medium]** 圧縮中エラー → 再試行時のパスワード取扱いが confirm-per-drop / always-save で挙動が分かれる（明文化が必要）
   - detail: ArchiveCompressor が AccessException 等で途中失敗 → ArchiveErrorHandler が分類 → ユーザーが再ドロップ。confirm-per-drop モードではダイアログが再表示される（仕様通り）。一方 always-save では『保存パスワードを使ってサイレント再試行』が期待されるが、ユーザーが『前回失敗した = パスワードが原因では？』と疑う可能性。何が原因でリトライしているのかメッセージがないと『無限リトライしてる？』と不安になる。さらに『パスワード自体は writer 生成失敗の原因ではない』ケースが大半（disk full / locked files など）なので、毎回パスワードを再確認する意味はない。
   - fix: ArchiveErrorHandler の分類が EncryptedOrWrongPassword の場合のみ、always-save モードでも『保存パスワードが間違っている可能性があります。再入力しますか？』を出す（PasswordDialog を IsRetry=true で表示）。それ以外の error type（ディスク満杯・アクセス不能・破損）では保存パスワードを再利用して通常リトライ。これにより無駄な再入力を防げる。実装は ArchiveProcessor の retry ループ内で error.Type による分岐。
- **[medium]** PasswordChar='●' の TextBox は screen reader / Narrator に『パスワード入力欄』として認識されない
   - detail: Avalonia の TextBox は PasswordChar を設定しても WAI-ARIA 的には plain TextBox。Windows Narrator は内容を 1 文字ずつ読み上げる可能性がある（『黒丸、黒丸、黒丸』または直接『P、a、s、s』と漏洩読み上げ）。AutomationProperties.Name や IsPassword 相当の指定が必要。Tab 順序も提案 XAML に明示なく、現状 PasswordBox → ConfirmBox（追加時） → OK ボタン → Cancel ボタン になっているか要確認（XAML 順序通り）。
   - fix: PasswordBox と ConfirmBox に `AutomationProperties.Name="{DynamicResource Text.Password.Placeholder}"` を明示。さらに可能なら Avalonia 12 で `<TextBox.Classes><Classes>password</Classes></TextBox.Classes>` で password スタイルを付け、Narrator が『パスワード欄、内容は読み上げません』モードを使うようにする。Avalonia 公式の MaskedTextBox or サードパーティ PasswordBox 検討も視野。Tab 順序は XAML 上の TabIndex を明示（0=primary, 1=confirm, 2=OK, 3=Cancel）。
- **[medium]** 暗号化ファイル名（7z `he=on`）の説明が『ファイル名も暗号化』だけでは不足（中身を知っている前提でないと脅威モデルが分からない）
   - detail: 技術者なら『中央ディレクトリを隠す』と分かるが、一般ユーザーは『パスワードかけてるのにファイル名は見えるの？』と思わず ON にするか、逆に『これ ON にすると展開できなくなりそう』と OFF にしがち。ON にしないと『パスワード解析しなくてもアーカイブ中身の一覧は見える（7-Zip でファイル名表示）』という事実が伝わらない。
   - fix: Description を『ON: アーカイブを開いてもファイル一覧が見えません（パスワードなしでは中身の存在も隠せます）。OFF: パスワードがなくてもファイル名は閲覧できます。』に変更。ON 推奨のニュアンスで（デフォルト ON は仕様通り）。さらに『初回 ON → 7z でない形式に切替』『初回 ON → ZIP に切替』時に『この設定は 7z 形式でのみ有効です』を 1 度だけ通知。
- **[medium]** Settings.json 直接編集で IsPasswordProtectionEnabled=true + EncryptedCompressionPassword 不在の状態を作れる（壊れ状態）
   - detail: ユーザーが手で settings.json を編集して { "IsPasswordProtectionEnabled": true, "PasswordMode": "Remember" } だけ書く（EncryptedCompressionPassword フィールド無し）と、起動後に『パスワード保護 ON + 保存パスワード無し』という矛盾状態が表示される。SanitizeAfterLoad / Settings.Load の 3-stage fallback ではこのケースを検出しない。挙動: 次回ドロップ時に『保存パスワード読込み失敗 → 確認ダイアログを開く』フォールバックなら親切だが、設計仕様にない。
   - fix: Settings.SanitizeAfterLoad に『if (IsPasswordProtectionEnabled && PasswordMode=="Remember" && (EncryptedCompressionPassword==null || EncryptedCompressionPassword.Length==0)) → PasswordMode = "PromptEachTime"（confirm-per-drop に degrade）+ Logger.Warning』。これでサイレントに整合性を取り、次回ドロップで通常の確認ダイアログが開く。代替案: IsPasswordProtectionEnabled = false に倒すと『勝手に OFF になった』と感じるので degrade の方がベター。
- **[medium]** DiagnosticsCollector の支援 ZIP に EncryptedCompressionPassword が漏れる
   - detail: Phase 1 settings-aot-dpapi の Caveats(4) で既に指摘されている既知ポイント。DiagnosticsCollector が settings.json をマスクして同梱する仕組みがあり、現在は UpdateBaseUrl のみが ignored fields。EncryptedCompressionPassword は DPAPI ciphertext だが、同じマシン上の別プロセスに渡されれば復号可能（DPAPI の CurrentUser scope は『同じ Windows ユーザーセッションなら復号できる』が定義）。サポート ZIP を SNS 等にアップする運用は想定されないが、危機管理として除外すべき。
   - fix: DiagnosticsCollector.cs:18 の sensitive field list に `EncryptedCompressionPassword` を追加。masking 関数で settings.json から該当 field を削除した上で同梱する。テスト: DiagnosticsCollectorTests に『support ZIP の settings.json には EncryptedCompressionPassword が含まれない』アサーション。
- **[medium]** PasswordDialog の Ctrl+V ペーストは 1 つ目には効くが、ConfirmBox 側はテスト未確認（実装依存）
   - detail: Avalonia TextBox は標準で Ctrl+V を扱うが、PasswordChar 設定時に IME / clipboard ハンドリングが期待通り動かない既知パターンがある（過去の Avalonia issue）。パスワードマネージャーから auto-fill する運用はかなり多い。確認入力が正しくペーストできないと『2 回入力』要件で詰まる。
   - fix: (1) Phase 1 提案の Tab 順序を最適化（PasswordBox で Tab → ConfirmBox に自然に移動できるように、IsTabStop=true 明示）。(2) E2E 風の手動テストチェックリストに『パスワードマネージャから両方の box に同じ値をペーストできる』を含める。(3) もし Avalonia の PasswordChar 仕様で Ctrl+V が効かない場合、KeyBinding で `Ctrl+V` を ApplicationCommands.Paste にバインドして強制有効化。(4) PasswordDialog 上部に小さく『（Ctrl+V でペースト可能）』hint を出すと迷子防止。
- **[medium]** 圧縮ボタン押下 → パスワード取得 → ArchiveProcessor 起動 までの間に MainWindow が閉じられた場合の動作未定義
   - detail: PasswordDialog は ShowDialog<bool>(parentWindow) で MainWindow を owner にしているため、MainWindow が閉じた瞬間 PasswordDialog も close される（Avalonia 仕様）。このとき Password=null + bool=false が返るので ArchiveProcessor は cancel 扱いで早期 return → 問題なし。ただし parentWindow が null の経路（IpcService 二重起動の secondary instance 経由のドロップ等）は ShowFromBackgroundAsync の else 分岐（`dialog.Show()` + TaskCompletionSource）が走り、parent が無いため画面の端に出るかフォーカスを取りこ
   - fix: ShowFromBackgroundAsync の else 分岐で parentWindow=null のとき WindowStartupLocation を `Manual` にして primary screen 中央に明示配置 + Topmost=true で一時的に最前面化（after-shown で Topmost=false に戻す）。さらに IpcService 経由のドロップで MainWindow が hidden/minimized 状態のときは『MainWindow を先に Activate してからダイアログ表示』を保証する。テスト: secondary instance による drop で PasswordDialog が画面外/裏側に行かないこと。
- **[medium]** 圧縮設定タブを開いていない（展開タブ表示中の）状態でドロップ → パスワード保護 ON 設定が見えない状態で確認ダイアログだけ出る
   - detail: MainWindow は展開タブ / 圧縮タブ が分離。ユーザーが圧縮タブを 1 度も見ずに（=設定ダイアログを開かずに）デフォルト OFF のまま使い続けることが多い。逆に、過去に ON にして閉じた後、何ヶ月か後に開いたユーザーが『なぜいつもパスワードを聞かれるんだろう』状態に陥る。ON 状態を視覚的に示す indicator がメイン画面上にあると親切。
   - fix: MainWindow のメイン領域（DropZone）またはタイトルバー付近に小さな鍵アイコン + ツールチップ『圧縮時にパスワード保護が有効』を出す（IsPasswordProtectionEnabled && IsZipOrSevenZipFormat のとき visible）。クリックで圧縮設定タブにジャンプ。これで『設定状態を常に意識』できる。実装は SymbolIcon / PathIcon + Binding。
- **[low]** v1.0.181 で新規 3 プロパティが追加されるが、TryRecoverFromJsonDocument への追加忘れリスク（既知のフットガン）
   - detail: Phase 1 settings-aot-dpapi gotcha (1) で再三警告されている『TryRecoverFromJsonDocument に追加忘れると stage-2 recovery で値が消える』フットガン。新規 3 プロパティ（IsPasswordProtectionEnabled / EncryptFileNames / PasswordMode）+ EncryptedCompressionPassword で計 4 件。1 件でも漏れると stage-2 fallback で『パスワード設定 ON のまま EncryptedCompressionPassword だけ消える → degraded 状態』が起きる（medium issue「Settings.json 矛盾状態」と合流）。
   - fix: PR 内で Settings.TryRecoverFromJsonDocument に 4 件全てを追加し、Lhamiel.Tests.Unit に『partial-corrupt JSON で stage-2 recovery 経由で新規 4 プロパティが復元される』テストを 1 件追加。CLAUDE.md の §Settings.Load 3 段フォールバック節に『新プロパティ追加チェックリスト』として ResetToDefaults + TryRecoverFromJsonDocument + AppJsonContext（自動）+ ApplySettingsToManager + LoadFromSettings + OnXxxChanged の 6 点を箇条書きで明記。
- **[low]** settings.json 初回マイグレーション通知は不要（仕様判断 OK）だが、リリースノートには明記すべき
   - detail: ユーザーが v1.0.180 → v1.0.181 に自動更新（Velopack）した後、初回起動で IsPasswordProtectionEnabled が無いので default=false（既存挙動完全互換）→ サイレント OK。telemetry も無いので surprise なし。ただ自動更新後に『新機能が増えた』ことを気付かれないまま使い続けるユーザーは多い。Velopack 更新後の changelog 表示はないので、リリースノートと README に書くだけだと埋もれる。
   - fix: (1) リリースノートに『パスワード保護機能を追加（圧縮設定タブから有効化）』を明記。(2) 任意: 初回起動時に『新機能のお知らせ』通知を 1 度だけ出す仕組み（Settings.LastSeenFeatureNoticeVersion を追加）。コスト高ければ (1) のみ。(3) README.md の機能スクリーンショット差し替え。


## aot-compat

- **[low]** ProtectedData は AOT 安全 (.NET 10 / win-x64・win-arm64)。ただし NuGet 追加と Optimize=true 下の DllImport 振る舞いは要確認
   - detail: `System.Security.Cryptography.ProtectedData` は `CryptProtectData` / `CryptUnprotectData` への直 P/Invoke だけで構成され、リフレクション・Reflection.Emit・動的型生成・open generics serializer は使わない。.NET 10 / Native AOT で `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` 注釈は付いていない。AOT 安全。確認した範囲: Phase 1 調査が言う通り、現状 Lhamiel.csproj には `System.Security.Cryptography.ProtectedData` の PackageReference は無い (.csproj 全 ItemGroup 
   - fix: Lhamiel.csproj の <ItemGroup> に `<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.*" />` を追加 (10.0.5+ stable)。TrimmerRootAssembly への追加は不要。`OperatingSystem.IsWindows()` ガードも不要 (TFM が net10.0-windows8.0 で確定)。CI の AOT publish (velopack-release.yml) で 1 回 `dotnet publish -c Release -r win-x64` を回して `IL2026` / `IL3050` / `IL3053` 警告が一切出ないことを確認 (TreatWarningsAsErrors な
- **[high]** byte[] EncryptedCompressionPassword の Base64 シリアライズは AppJsonContext に必ず追加 (新規プロパティ追加だけでは不十分なケースがある)
   - detail: Phase 1 調査は「Settings に新プロパティを足せば AppJsonContext のソースジェネレータが自動追従する」としているが、これは Settings の `[JsonSerializable(typeof(Settings))]` ルートから到達可能な型に限る。`byte[]` は System.Text.Json のビルトイン型として Base64 自動変換されるので追加の `[JsonSerializable(typeof(byte[]))]` 登録は不要 — ここは Phase 1 の指摘通り。確認済: AppJsonContext.cs:9-17 は `[JsonSerializable(typeof(Settings))]` + `[JsonSerializable(typeof(string[]))]` の 2 つのみ。`byte[]` (= `Byte[
   - fix: (1) Settings.cs に `public byte[]? EncryptedCompressionPassword { get; set; }` を追加するだけで足りる (AppJsonContext.cs 変更不要)。(2) **必ず** `dotnet publish src/Lhamiel/Lhamiel.csproj -c Release -r win-x64` を 1 回回して `warning IL2026` / `IL3050` が出ないことを確認 (TreatWarningsAsErrors でビルドエラーになる)。出た場合は AppJsonContext.cs に `[JsonSerializable(typeof(byte[]))]` を明示追加 (副作用なし・コンテキストに型を増やすだけ)。(3) JsonDocument フォールバックパス (TryRec
- **[high]** PasswordMode を `string` で持つ提案は AOT 安全だが、`enum` に格上げするなら EnumToBoolConverter を作ってはいけない (Lhamiel 流のコードビハインド Tag パターンを使う)
   - detail: Lhamiel の src ツリーには `IValueConverter` / `MarkupExtension` 実装が 0 件 (grep で確認済)。`EnumToBoolConverter` などの汎用コンバータは存在しない。Avalonia の汎用 EnumToBoolConverter は型引数や `Enum.Parse` を使うものが多く、`Enum.Parse(Type, string)` は AOT 下で `IL2026 (RequiresUnreferencedCode)` 警告を出す (型の保存が保証されないため)。Phase 1 調査の `StringEqualsConverter` 提案も同じ理由で却下 (新規コンバータ実装 = AOT 警告リスク + コードベース規約違反)。**正しい AOT 安全パターン**: MainWindow.axaml の既存 Dir
   - fix: (1) Settings.PasswordMode は `string` で実装 (enum 化しない)。SanitizeAfterLoad で `if (!(PasswordMode is "PromptEachTime" or "Remember")) PasswordMode = "PromptEachTime";` の allow-list 検証を入れる。(2) XAML 側は RadioButton に `Tag="PromptEachTime"` / `Tag="Remember"` を設定し、`IsCheckedChanged="PasswordModeRadio_Changed"` でコードビハインドにルーティング。コードビハインドで `if (sender is RadioButton { IsChecked: true, Tag: string mode }) View
- **[medium]** Avalonia compiled bindings (x:CompileBindings=True) — 新規 VM プロパティは public + DataContext の x:DataType と一致必須
   - detail: Lhamiel.csproj:19 で `AvaloniaUseCompiledBindingsByDefault=true`、TreatWarningsAsErrors=true。compiled binding はビルド時に型チェックされるので、Phase 1 が提案する `IsZipOrSevenZipFormat` / `IsSevenZipFormat` / `IsPasswordProtectionEnabled` / `EncryptFileNames` / `PasswordMode` は **すべて public** で MainWindowViewModel に置く必要がある (`internal` でビルド失敗の可能性)。`[ObservableProperty] private bool _isPasswordProtectionEnabled;` は Commun
   - fix: (1) MainWindowViewModel に追加する derived プロパティは `public bool IsSevenZipFormat => ...` の形 (private/internal 禁止)。(2) `SelectedCompressionFormat` の宣言に `[NotifyPropertyChangedFor(nameof(IsSevenZipFormat))]` / `[NotifyPropertyChangedFor(nameof(IsZipOrSevenZipFormat))]` を追加しないと UI が更新されない (Phase 1 指摘通り)。(3) PasswordDialog の追加プロパティも `public` (Mode, IsConfirmVisible, DialogTitle, MessageText)。INPC は不要 (ダイアログ
- **[medium]** PasswordDialog の PasswordDialogMode enum と分岐 XAML は AOT 安全 — ただし `App.Text(Mode switch { ... })` の switch 式は遅延評価ではなく getter のため、ロケール切替時の更新には DynamicResource を併用
   - detail: Phase 1 提案の `public string DialogTitle => App.Text(Mode == Extract ? "Password.Title" : "Password.SetTitle");` は AOT 安全 (App.Text は静的呼び出し)。switch 式・三項演算子・enum 比較いずれもリフレクション 0。**実害は AOT ではなく ロケール動的切替** — ダイアログ表示中にユーザが locale を変えても `DialogTitle` getter が再評価されないので Title 文字列が古いままになる。ただし PasswordDialog は modal で短命のためこのケースは実用上問題なし。**ただし XAML 側で `Title="{Binding DialogTitle}"` ではなく `Title="{DynamicResour
   - fix: (1) PasswordDialog.axaml の Title は `Title="{DynamicResource Text.Password.Title}"` のまま据え置き、Mode=CompressNew のときは別途 SubtitleTextBlock を Mode 分岐で IsVisible トグルする方式に変更。または DialogTitle getter 方式を維持するなら `Opened` ハンドラで明示的に `Title = App.Text(...)` を 1 回設定する (ロケール変更非追従を受容)。(2) 新 i18n キー (Text.Password.SetTitle/SetMessage/ConfirmPlaceholder/MismatchWarning) は 17 ロケール .axaml 全てに追加 — 漏れは DynamicResource 解決時に
- **[low]** 1llum1n4t1s.Sevenzip の CompressionOption (Password / EncryptionMethod / CustomParameters) は AOT 安全 — ただし CustomParameters Dictionary は string→string 限定で OK
   - detail: `new CompressionOption { Password = "...", EncryptionMethod = EncryptionMethod.Aes256, CustomParameters = new Dictionary<string,string> { ["he"] = "on" } }` は record/POCO のオブジェクト初期化子で、ライブラリ側 (CompressionOptionSetter.Invoke) は `Dictionary<string,string>` を foreach して `PropVariant.Create(BSTR)` に変換する。`Dictionary<string,string>` も `PropVariant.Create(string)` も AOT 安全 (前者はジェネリック値型 + 参照型の組み合わせで AOT で完
   - fix: ArchiveCompressor.CreateArchiveWriter の `CompressionOption` 初期化子に Password / EncryptionMethod / CustomParameters を追加するときは型注釈を明示: `var custom = new Dictionary<string, string>(); if (encryptFileNames && format == Format.SevenZip) custom["he"] = "on";` のように **必ず string→string** にする。`object` を値に使わない。AOT publish ビルドで動作確認 (`dotnet publish -c Release -r win-x64` で 1 回)。
- **[low]** テスト側 (xUnit 3 + Moq) は AOT publish 対象外なので Moq のリフレクションは無問題 — ただし IPasswordDialogService の静的差し替えパターンは [Collection("ArchiveProcessor")] 必須
   - detail: `Lhamiel.Tests.Unit` は `dotnet test` で実行され、`PublishAot=true` の対象ではない (Lhamiel.csproj のみ AOT publish される)。Moq の DynamicProxy / Castle.Core のリフレクションコード生成は JIT 環境で動くので AOT 警告も実行時例外も起きない。Phase 1 が提案する `IPasswordDialogService` + `PasswordDialogImpl` 静的プロパティ差し替えは Lhamiel の `MessageServiceImpl` / `UiDispatcherImpl` / `ConflictDialogImpl` パターンと一致 (ServiceContracts.cs:1-57)。**並列実行リスク** — `ArchiveProcessor.
   - fix: (1) IPasswordDialogService を `internal interface` で ServiceContracts.cs に追加し、`ArchiveProcessor.PasswordDialogImpl` を既存 3 件の static impls と同じ列に並べる。(2) 既存 ArchiveExtractor.cs:1080 の直接呼び出し (`PasswordDialog.ShowFromBackgroundAsync`) も `ArchiveProcessor.PasswordDialogImpl.PromptForPasswordAsync` 経由に同時リファクタ — 二経路放置はテストスタブが extraction パスをカバーできない。(3) PasswordDialogImpl を差し替える adversarial テストは必ず `[Collecti
- **[high]** TryRecoverFromJsonDocument に EncryptedCompressionPassword / IsPasswordProtectionEnabled / EncryptFileNames / PasswordMode の救出ハンドラを必ず追加 (手動メンテ箇所)
   - detail: AOT とは独立だが、Settings.cs:614-698 の `TryRecoverFromJsonDocument` は手動メンテで、新規プロパティを追加し忘れると **stage-2 リカバリで黙って消える** バグになる (Phase 1 が gotcha で挙げている #1 footgun)。これは Velopack 自動更新中に他プロパティの型不整合 (例: 新バージョンで enum に値を増やしたケース) で stage-2 に落ちると、暗号化パスワードだけ消失して「設定を保存したのに次回ロード時に空になっている」事象を引き起こす。`byte[]?` を `JsonElement.GetBytesFromBase64()` で復元する場合は `FormatException` と `InvalidOperationException` の両方を catch (Phase 1
   - fix: Settings.cs:614-698 (TryRecoverFromJsonDocument) に以下 4 件を追加 (既存 TryGetString/TryGetBool ヘルパに揃える):
```csharp
if (TryGetBool(root, nameof(IsPasswordProtectionEnabled), out var ipe)) { s.IsPasswordProtectionEnabled = ipe; recoveredCount++; }
if (TryGetBool(root, nameof(EncryptFileNames), out var efn)) { s.EncryptFileNames = efn; recoveredCount++; }
if (TryGetString(root, nameof(PasswordMode), out var
- **[medium]** DiagnosticsCollector のマスクリストに EncryptedCompressionPassword を追加しないと support ZIP に暗号文が漏れる
   - detail: AOT 問題ではないが Phase 1 が gotcha で挙げている重要な追従修正。`%LocalAppData%\Lhamiel\settings.json` をそのままサポート ZIP に同梱する経路で `EncryptedCompressionPassword` の Base64 文字列が混入する。DPAPI 暗号化済みなので「他人の PC では復号できない」が、本人 PC でサポート ZIP を attacker に渡してしまうケース (例: GitHub Issue にうっかり添付) で復元可能。DPAPI は CurrentUser scope だが、サポート ZIP を提出する相手の PC 上で本人が動かしたら復号できてしまう。**defense in depth として必ずマスクすべき**。Settings.cs の `JsonIgnore` (Phase 1 が提案する
   - fix: Util/DiagnosticsCollector.cs:18 付近のマスクリスト (UpdateBaseUrl と同列) に `"EncryptedCompressionPassword"` を追加。マスク対象は **シリアライズ前に null 化したコピー** を出力するか、シリアライズ後の JSON 文字列を正規表現で置換するか、いずれかの既存実装パターンに合わせる。マスク済み出力をテスト (`/stst` の adversarial パス) で確認: `EncryptedCompressionPassword` が暗号文付きで settings.json に書かれているシナリオでサポート ZIP を作って、ZIP 内 settings.json に当該キーが含まれない (もしくは値が空) ことを assert。


## integrity-and-edge

- **[blocker]** ZIP + Password の暗号化方式が未指定だと黙って ZipCrypto (1989年の壊れた暗号) にダウングレードされる
   - detail: Phase 1 が確認した通り `1llum1n4t1s.Sevenzip` の `ZipOptionSetter.AddEncryptionMethod` は `EncryptionMethod == Default` のとき `em` プロパティを emit せず、7z.dll は ZIP デフォルトの ZipCrypto (PKZIP legacy) にフォールバックする。ZipCrypto は数秒で破られる完全に壊れた暗号で、Phase 1 自身が '#1 footgun' と明言している。

現状 `ArchiveCompressor.CreateArchiveWriter` (ArchiveCompressor.cs:660-671) の ZIP 分岐は `CompressionLevel` / `CompressionMethod` / `ThreadCount` / `Co
   - fix: `CreateArchiveWriter` の ZIP 分岐で password が非空のとき必ず `EncryptionMethod = EncryptionMethod.Aes256` を同時設定する。例:
```csharp
var zipOptions = new CompressionOption {
    CompressionLevel = (CompressionLevel)settings.ZipCompressionLevel,
    CompressionMethod = CompressionMethod.Deflate,
    ThreadCount = threadCount,
    CodePage = CodePage.Utf8,
};
if (!string.IsNullOrEmpty(password)) {
    zipOptions = z
- **[blocker]** AccessException 全件スキップ + パスワード = 『中身ゼロだがパスワード保護』アーカイブが作られ、ユーザーは喪失に気付かない
   - detail: v1.0.180 の『アクセス不能ファイルをスキップして圧縮続行』(ArchiveCompressor.cs:172-194) は単一ファイルの `writer.Add` 失敗を吸収して圧縮を続行する。これは正しい設計だが、**パスワード保護と組み合わさると独自の地雷** になる:

1. ユーザーが `C:\Users\Me\Documents\機密` を圧縮対象に追加 + パスワード ON
2. 当該ディレクトリ全体が EFS で暗号化済みで CurrentUser が読み取り権を失っている (別アカウントから取得した、Domain 移行で SID が変わった、etc.)
3. **全ファイルが `AccessException` でスキップ** され、`inaccessibleSkipped == filesToCompress.Count`
4. それでも writer.Save(
   - fix: 1. **0/0 検出**: `writer.Add` 成功カウンタを追加し (`addedCount++` を try の成功側)、`emptyDirMarker` 経由以外で 0 件しか追加できなかった場合は `writer.Save` を呼ばずに throw する。
```csharp
if (addedCount == 0 && inaccessibleSkipped > 0)
    throw new InvalidOperationException(App.Text("Error.AllSourcesInaccessible", inaccessibleSkipped));
```
2. **パスワード時のスキップ警告 UI**: `inaccessibleSkipped > 0` かつ password が設定されているとき、完了時に `MessageServiceImp
- **[blocker]** writer.Save 直前で OperationCanceledException が出るとパスワード平文が GC まで `CompressionOption` に居座る (TryDeletePartialOutput 経路の盲点)
   - detail: `CompressionOption.Password` は **init-only string**。`using var writer = CreateArchiveWriter(...)` が Dispose されても、内部の `Options` 参照は writer.Dispose では `null` 化されない (ライブラリ側コードを Phase 1 が読んだ範囲では明示的なクリアは無し)。

通常フローでは `using` スコープ脱出で writer は不到達になり Gen0 で回収されるが、Lhamiel の現実装には writer の生存を JIT 最適化から保護する `NativeInteropHelper.KeepAliveCallbacks(writer, ...)` (ArchiveCompressor.cs:225) があり、これが **意図的に writer 
   - fix: **Settings 本体に平文 string 型のパスワードを置かない** (Phase 1 ui-vm-wiring の最後の gotcha も明示)。設計を以下のように分離:
1. `Settings.IsPasswordProtectionEnabled` (bool) と `Settings.PasswordMode` (string enum) と `Settings.EncryptedCompressionPassword` (byte[]?) **のみ** persist。
2. 平文 string は専用 `CompressionPasswordSession` static クラスに `SecureString` または短寿命 `char[]` で保持し、`using` で確実に zero-fill する:
```csharp
internal static class
- **[high]** NativeArchiveGate スコープ内に PasswordDialog (UI) を入れるとデッドロック確実
   - detail: Phase 1 が確認した通り `NativeArchiveGate` は **非リエントラント** な `SemaphoreSlim(1, 1)`。現在の `CompressFilesAsync` (ArchiveCompressor.cs:140-227) は `NativeArchiveGate.EnterAsync` 取得 → `Task.Run` 内で `CreateArchiveWriter` → `writer.Add` → `writer.Save` → Dispose を一気に行う。

Phase 1 ui-vm-wiring の設計は『パスワードを `ArchiveCompressor.Compress` に **パラメータ渡し** する』方針なので OK だが、もし将来あるいは設計検討時に『compressor 側で password が必要になった瞬間に dialo
   - fix: 1. **`CompressFilesAsync` のシグネチャに `string? password` を明示追加** し、設計ドキュメント (CLAUDE.md の ArchiveCompressor 節 + コメント) に『パスワード入力 UI は gate の **外側** で完結させ、CompressFilesAsync 呼び出し時点で平文を確定させる』を明記。
2. `NativeArchiveGate` 直上に `[Conditional("DEBUG")]` のリエントラント検出を入れる: AsyncLocal<int> でカウントして 1 を超えたら `throw new InvalidOperationException("NativeArchiveGate is non-reentrant")`。これで gate 保持中に UI await を入れる回帰を CI でキ
- **[high]** バッチ圧縮 (`CompressItemsAsync`) で N アーカイブごとに password を聞き直すと UX 破綻、しかし一括だと『各アーカイブ別 password』要件と衝突
   - detail: 現状 `MainWindowViewModel.ProcessDroppedPathsAsync` (ViewModels:730/771) は `validPaths` (複数フォルダのドロップ) を `CompressItemsAsync` に渡して並列圧縮する。`IoBoundParallelism` (2〜4) で並列。各タスクは独立して `CompressItemAsync` を呼ぶ。

**Phase 1 ui-vm-wiring の `PasswordMode = PromptEachTime` / `Remember` の 2 択は、バッチ圧縮の意味を全く考慮していない**:

- **PromptEachTime + バッチ 5 件**: 5 個の password ダイアログが並列同時に出る (全て CenterOwner なので互いに重なる)。ユーザーは『どれがどのア
   - fix: 1. **設計決定を明示**: バッチ圧縮 (CompressItemsAsync) では『最初の 1 個の password 取得後、残りのアーカイブにも同じ password を再利用する』をデフォルトにする (Phase 1 が言う Remember モードに近いが、PromptEachTime でもバッチ内では reuse)。これにより並列 N 個ダイアログ問題を完全回避。ユーザーが個別に分けたい場合は『1 個ずつドロップする』ワークフローを明示。
2. UI 上の文言: `Text.Password.SetMessage` に『複数のアーカイブを同時に作成する場合は、全てに同じパスワードが設定されます』を追加。
3. CompressItemsAsync 内で *並列開始前に* 1 回だけ `PasswordDialogImpl.PromptForPasswordAsync` を
- **[high]** TryDeletePartialOutput で『書きかけの暗号化アーカイブ』を削除する際の race / 削除失敗時のデータ可視性
   - detail: `TryDeletePartialOutput` (ArchiveCompressor.cs:289-302) はキャンセル/エラー時に書きかけアーカイブを best-effort で削除する。`File.Delete` が失敗した場合 (Defender スキャン中ロック・ネットワークドライブ etc.) は警告ログを残して呑む。

パスワード保護コンテキストでは追加のリスクが 2 つある:

1. **部分書きの暗号化 ZIP/7z は中身が読める形で残る**: 中央ディレクトリが書かれていないだけで、暗号化済みの個別エントリ data stream はディスク上に存在する。暗号は ZIP-AES / 7z-AES (両方とも AES-256-CTR) で正しく暗号化されていれば技術的には key 無しで読めないが、**もしユーザーが Phase 1 #1 の罠で ZipCrypto 
   - fix: 1. `CompressionOption.AtomicSave = true` を **必ず指定** する (パスワード有無に関わらず)。AtomicSave true なら writer は `outputPath.tmp_xxx` 等に書いて完了時に rename するので、Save 失敗時の outputPath は『そもそも何も書かれていない or 旧版のまま』になり、TryDeletePartialOutput が削除すべきは tmp ファイル側。
2. `TryDeletePartialOutput` の失敗時 (`File.Delete` 例外) はパスワード保護 ON のときに限り `MessageServiceImpl.ShowError(App.Text("Error.PartialEncryptedFileLeft", outputPath))` で **明示的にユ
- **[high]** settings.json の `.corrupt_*.bak` 退避ファイルに DPAPI 暗号化済みとはいえ ciphertext が無期限残存
   - detail: Phase 1 settings-aot-dpapi の設計通り `EncryptedCompressionPassword: byte[]` (DPAPI CurrentUser scope) を採用すると、**JSON パース失敗時の 3 段階フォールバック** (Settings.cs:323-359) で生成される `{path}.corrupt_yyyyMMddHHmmss_fff.bak` ファイルに、DPAPI 暗号化済みの ciphertext がそのまま残る。

- DPAPI CurrentUser は確かに同一ユーザーセッションでないと復号できないので、別ユーザー/別 PC への持ち出しは安全。
- **しかし**: 同一 PC の同一アカウントを攻撃者 (マルウェア・物理アクセス) が掌握した場合、`%LocalAppData%\Lhamiel\settings.j
   - fix: 1. **`.corrupt_*.bak` を 7 日以上経過したら自動削除** (Logger の CleanupOldLogFiles と同じパターン)。Settings.Load の冒頭で `CleanupOldCorruptBackups(AppDataDirectory, TimeSpan.FromDays(7))` を呼ぶ。
2. **退避時に password ciphertext を strip**: `File.Move` する前に json を一度 JsonDocument でパースし、`EncryptedCompressionPassword` プロパティを除いた状態で `.bak` を書く (元の壊れた json 構造は失われるが、ユーザーがサポートに送る用途では password ciphertext を残す意味は無いし、壊れた json の構造を保全したいなら別
- **[high]** ScanSourceFiles → DetectConflicts → ResolveByRenaming の重複名解決後に password 設定すると、ユーザーの『元のファイル名』理解と暗号化アーカイブ内の実名が乖離
   - detail: 現状の compress 経路は ScanSourceFiles でファイル列挙 → DetectConflicts で同名衝突を検出 → ConflictDialog で解決 (パス保持 or `_1`/`_2` リネーム) → CompressFilesAsync に `resolvedFiles` を渡す。

7z + `EncryptFileNames = true` (-mhe=on) を有効にすると、**アーカイブ内のファイル名は暗号化されているのでユーザーは展開時まで実名を確認できない**。Lhamiel 自身で展開すれば PasswordDialog を経て名前が見えるが、別 PC や別ツールで開くまでは確認手段が無い。

ここで起きる嫌な状態:
1. ユーザーが `A/file.txt` と `B/file.txt` をドロップ → DetectConflicts 検出 
   - fix: 1. **password ON のときは ConflictDialog のヘッダーに警告**: `Text.Conflict.PasswordWarning` を追加し、『アーカイブ内のファイル名はパスワードで保護されるため、展開時まで実際の名前を確認できません。リネーム結果を別途記録することを推奨します』を出す。
2. **リネームマッピング .txt の同梱オプション**: 圧縮完了時、リネームが 1 件でも発生していたら、出力ディレクトリに `{archiveName}.rename_log.txt` (UTF-8) として『`A/file.txt` → `file.txt`, `B/file.txt` → `file_1.txt`』を残す。**ただしこのファイルは暗号化されない**ので機密度の判断はユーザー任せ。デフォルト OFF、設定で ON にできる。
3. **暗号化 ON
- **[medium]** ArchiveIntegrityVerifier はパスワード保護アーカイブを 'CRC 検証スキップ' し silently パスする (展開側) — 圧縮側 round-trip 検証は無い
   - detail: ArchiveIntegrityVerifier.cs:40-66 は既にパスワード保護アーカイブを検出して『hasEncryptedItems → return new VerificationResult(true) (CRC 検証スキップ)』する。これは展開側の `VerifyAfterExtraction` 経路で発火する。

問題は 2 点:

1. **展開側**: Lhamiel が **自分で生成した** password-protected アーカイブを別のフローで展開するとき、CRC 検証は無条件にスキップされて『成功』を返す。つまり (a) 暗号化部分の改竄、(b) 中央ディレクトリの破損、(c) Lhamiel の compress 側バグで壊れた暗号化アーカイブ — どれも検出できない。これは現在のコード設計上の trade-off (`reader.Test()
   - fix: 1. **新規 `Settings.VerifyAfterCompression` (bool, デフォルト false)** を追加。true のとき `CompressFilesAsync` の finally で writer Dispose 後、別 reader を開いて password 込みで `reader.Test()` する。失敗したら **圧縮成果物を削除して throw**。データロスを防ぐため重要。デフォルト false なのは時間コストが大きいから。
2. **`ArchiveIntegrityVerifier.VerifyArchiveAsync` に `string? password` 引数を追加**: password が非 null なら `ArchiveOption { Password = password }` 相当を渡して通常検証する。null 
- **[medium]** MotwPropagator は展開側のみだが、Lhamiel が生成した password-protected ZIP/7z をダウンロード経由で受け取ったクライアントの展開動作は変わる (副次効果)
   - detail: MotwPropagator 自体は展開専用で、圧縮ロジックには触れない (Phase 1 が言う通り 'orthogonal')。だが副次的な相互作用がある:

1. **Lhamiel が生成した暗号化 ZIP/7z をブラウザでダウンロードすると `Zone.Identifier:ZoneId=3` が付く** → そのファイルを Lhamiel で展開すると `MotwPropagator.ReadZoneIdentifier` → `PropagateToDirectory` でルートアイテムに伝播。これは現状通り動く。
2. **問題**: `PropagateToDirectory` (MotwPropagator.cs:49) は `Directory.EnumerateFiles(...AttributesToSkip = ReparsePoint)` で展開済みファイル
   - fix: 1. CLAUDE.md の '## Key Technical Details' の MotW 節に『パスワード保護展開で EncryptionException 等が出た場合、部分展開ファイルへの MotW 伝播は実行されない』を 1 行追記。
2. `ArchiveProcessor.ExtractArchiveAsync` の catch (line 215-231) で、`ex is EncryptionException` のときログレベルを `Warning` で『部分展開ファイルが残る可能性があります』を追加 (現状は `LogException` のみ)。
3. 圧縮側は変更不要 — MotwPropagator は extract 専用。
- **[medium]** LockedFileRetryPolicy は password と直交だが、リトライ中のキャンセル → password 平文 string がリトライ closure 経由で長寿命化
   - detail: Phase 1 が言う通り `LockedFileRetryPolicy` は SHARING_VIOLATION / LOCK_VIOLATION の指数バックオフリトライで、password 自体とは直交。ただし `LockedFileRetryPolicy.ExecuteAsync(() => Task.Run(() => new ArchiveReader(archivePath, passwordQuery, extractOption)), ...)` (ArchiveExtractor.cs:946-961) の **lambda closure に passwordQuery が捕捉される** ため、リトライが 3 回失敗してから throw されるまでの間 (200ms+400ms+ ... = 数秒〜数十秒オーダー)、`passwordQuery` (= AsyncPa
   - fix: 1. 圧縮側で `LockedFileRetryPolicy` をネイティブな『writer.Save リトライ』に流用しない方針を採用する。ArchiveCompressor.cs 現状の `writer.Save` は 1 回限り (リトライしない) なのでこの問題は表面化していない。**現状維持**。
2. もし将来圧縮側で SHARING_VIOLATION リトライを入れるなら、closure に password 全文を捕捉せず、**最初のリトライ後は password local を null クリア** する設計にする (writer インスタンスは Save 内部で password を保持するが、外側 closure から外す)。
3. AsyncPasswordQuery 経由の extract 側パスワード retain についても、今は ArchiveReader
- **[medium]** ArchiveProcessor.ShouldSkipFolderCreation はパスワードに直交だが、暗号化ヘッダー (-mhe=on) で reader.Items が password 無しでアクセス不能になる影響を確認
   - detail: ShouldSkipFolderCreation は ArchiveStructureInfo の `RootItemNames` を見て『ルートフォルダ名がアーカイブ名と一致』を判定する。`GetArchiveStructureInfo` は内部で `reader.Items` を走査して `ParseArchiveRootLevel` を呼ぶ (ArchiveExtractor.cs:350)。

Lhamiel 自身が生成した 7z + `EncryptFileNames=true` を別フロー (例: ユーザーが手動で Lhamiel の展開機能を呼ぶ、または別ツールで圧縮されたヘッダー暗号化アーカイブを展開する) で開くとき、**`reader.Items` 自体が password 無しでアクセス不能** で例外を投げる (Phase 1 library-api gotcha 
   - fix: 1. `GetArchiveStructureInfo` (ArchiveExtractor.cs) でヘッダー暗号化を検出する分岐を追加: `reader.Items` 走査が `EncryptionException` で失敗した場合、`ArchiveStructureInfo` の `RootItemNames` / `SingleRootItemName` を空、`ShouldSkipFolderCreation = false`、新規 `IsHeaderEncrypted = true` フラグを立てて返す。
2. ArchiveProcessor.cs:73-83 で `IsHeaderEncrypted = true` の場合は構造解析を skip し、`CreateArchiveNameFolder` の論理にフォールバック (= アーカイブ名フォルダを必ず作成、二重ネスト
- **[medium]** TAR + Password 選択時に ArchiveWriter ctor で `InvalidOperationException` が throw されてエラーダイアログが汚い
   - detail: Phase 1 library-api 確認済み: `Options.Validate(Format.Tar)` が CompressionOption.Password 非空時に throw する ("Format.Tar does not support encryption.")。

Phase 1 ui-vm-wiring は `IsZipOrSevenZipFormat` derived bool で password セクションを TAR 選択時に disable する設計を提案しているが、これは UI 層の guard。実際の `ArchiveCompressor.CreateArchiveWriter` (line 634-674) の TAR 分岐は `new ArchiveWriter(format)` (オプション無し) なので、もし内部状態の race (ユーザーが
   - fix: 1. UI 上の guard (Phase 1 ui-vm-wiring の `IsZipOrSevenZipFormat`) に加えて、**`CreateArchiveWriter` 内部でも fail-fast** する:
```csharp
private static ArchiveWriter CreateArchiveWriter(Format format, Settings settings, string? password = null, int maxThreads = -1) {
    if (!string.IsNullOrEmpty(password) && !FormatExtension.IsEncryptionSupported(format))
        throw new ArgumentException(App.Text("Error.Fo
- **[low]** AppJsonContext の SourceGenerator が `byte[] EncryptedCompressionPassword` を Base64 シリアライズするが、不正に巨大な値 (例: 100MB) を settings.json に書かれると `JsonSerializer.Deserialize` が OOM
   - detail: Phase 1 settings-aot-dpapi の設計は `byte[]? EncryptedCompressionPassword { get; set; }` を Settings に直接追加し、`AppJsonContext` のソースジェネレータが自動で Base64 文字列シリアライズに対応する、と言っている。これは正しい。

但し攻撃シナリオ: ユーザー (または malware) が `%LocalAppData%\Lhamiel\settings.json` を手で編集し、`"EncryptedCompressionPassword": "<100MB の Base64>"` を書き込む。

- `JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings)` は Base64 を decode し
   - fix: 1. `SanitizeAfterLoad` (Settings.cs:429-477) で `EncryptedCompressionPassword` の長さチェックを **必須** にする:
```csharp
const int MaxEncryptedPasswordBytes = 4096; // DPAPI cipher の現実的上限
if (EncryptedCompressionPassword is { Length: > MaxEncryptedPasswordBytes }) {
    Logger.Log($"EncryptedCompressionPassword が異常巨大 ({EncryptedCompressionPassword.Length} bytes)、null クリア", LogLevel.Warning);
    EncryptedComp
- **[low]** Logger.Log に password 平文/暗号化前 byte 列が混入する経路の網羅確認
   - detail: 現状の ArchiveCompressor / ArchiveProcessor は `Logger.Log($"ファイルにアクセスできません（スキップ）: {fullPath} - {ex.Message}")` 等で Exception.Message を string interpolation で吐く。`AccessException` / `EncryptionException` / `IOException` のメッセージに『password』『key』等の sensitive 情報が含まれる可能性は低いが、`1llum1n4t1s.Sevenzip` ライブラリの将来バージョンで例外メッセージに『Password was: "xxxx"』のような debug 情報が混入する可能性を排除できない。

さらに自社コードで `Logger.Log($"Password lengt
   - fix: 1. **コード規約に明記**: ArchiveCompressor.cs 等の冒頭コメントに『Logger.Log に password.Length / password.GetHashCode / password.Substring 等を絶対に出力しない』を追加。
2. CompressionPassword 関連のコードで `Logger.Log` を使う場合は **必ず password 関連の変数を出力しない** ことを Code Review で確認。
3. DiagnosticsCollector の `MaskSensitiveValues` の Regex (`SensitivePatternRegex = (?i)(token|secret|password|key|credential|apikey|api_key)`) はプロパティ名のマッチなので、Logger
- **[low]** v1.0.180 の inaccessibleSkipped 警告ログがパスワード保護 ON のとき diagnostics 経由でサポートに送られるので OK だが、UI 警告ダイアログにも上げるべき
   - detail: blocker #2 と関連するが、こちらは『一部スキップ』(ゼロでない add 件数あり) のケース。現状 ArchiveCompressor.cs:189-194 は `inaccessibleSkipped > 0` のとき `LogLevel.Warning` でログを出すだけ。ユーザーは『圧縮完了』ダイアログを見て『成功した』と理解し、本当は機密ファイルがアーカイブから漏れていることに気付かない。

password 保護 OFF の通常圧縮ではこの程度の UX は許容範囲だが、password 保護 ON では『暗号化対象のはずだったファイルが暗号化されずに元 PC に残った』ことを明示する必要がある。例: 機密プロジェクトを暗号化アーカイブ化 → `.vsidx` が VS でロックされてスキップ → 元の `.vsidx` だけ平文で残る (これは VS の内部ファイルなの
   - fix: 1. blocker #2 の対応に合わせて、password 保護 ON かつ `inaccessibleSkipped > 0 && addedCount > 0` の場合に warning ダイアログを表示する: `App.Text("Compression.PartialSkipWithPasswordWarning", inaccessibleSkipped, addedCount)`。`{0}件のファイルがアクセス不能でスキップされました ({1}件は暗号化アーカイブに含まれています)。スキップされたファイルは元の場所に残っています。`
2. password 保護 OFF の場合は現状通り Logger のみ (UX を維持)。
3. テストは blocker #2 と統合。
