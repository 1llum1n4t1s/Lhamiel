# Worker 結果の E2E

実製品の launcher と worker の `Program.Main`、進捗画面・エラー画面を使用する。
書庫・設定・出力・証拠は指定した新しいディレクトリへ隔離する。通常アプリや別 GUI E2E と同時実行しない。
エラー画面は表示記録後に一度閉じる。個別取消は実進捗画面の `Close`、全体取消は親 CTS → named event の経路を使う。

```powershell
dotnet build scripts/WorkerOutcomeE2E/Probe.csproj -c Release -o scripts/WorkerOutcomeE2E/bin/e2e
$env:LHAMIEL_OUTCOME_TEST_ROOT = 'C:\absolute\fresh-artifacts-directory'
& scripts/WorkerOutcomeE2E/bin/e2e/Lhamiel.Tests.Unit.exe --observe
```

`--observe` は修正前 DLL でも実行でき、正常 ZIP と壊れた無害 ZIP の子終了コード・親進捗を結果へ記録する。合格判定を終了コードへ反映しない。
修正後は同じプロジェクトをビルドし直し、毎回新しい artifact directory で `--mixed`、`--individual`、`--group` を逐次実行する。
`--password` は `password.zip` がなければ無害な `hello.txt` と固定のテスト用パスワードから、公開 `ArchiveCompressor.CompressFilesAsync` で暗号化 ZIP を生成する。ユーザーの秘密値は使わない。実パスワードダイアログを閉じる場合を検証し、生成した書庫・ソースの SHA-256 を `password-fixture.json` に残す。
`--cli` は実 `--extract` 親プロセスの終了コード 10 と全子退出を確認する。
`--ui` は実 MainWindow の ViewModel へ同じ正常・破損書庫を渡し、終了後もメイン画面が可視・終了コード 0 であることを記録する。
両モードは、子のエラー本文を親の MessageDialog で再表示しないことも検証する。

`failure-cases.json`、worker ごとの開始・表示画面・退出記録、`result.json` を保持する。
合格条件は全子の退出、成功データ一致、失敗/取消の非ゼロ終了、成功だけの進捗計上、全体取消の OCE。
`--ui` は VM 受付経路の結果検証であり、実 Drag routed event・右クリック・ショートカットの全入口試験は既存の並列 E2E を使う。
