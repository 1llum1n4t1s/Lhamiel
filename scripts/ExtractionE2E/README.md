# 展開並行ルート E2E

ユーザーの設定ファイルを変更せず、実製品の Program.Main と実 MainWindow を使用する。
製品ビルド後、以下をリポジトリルートで実行する。

```powershell
dotnet build scripts/ExtractionE2E/Probe.csproj -c Release -o scripts/ExtractionE2E/bin/e2e
pwsh -NoProfile -File scripts/Test-ParallelExtraction.ps1 -ArtifactDirectory .codex/extraction-parallel-20261007/extraction-probe/artifacts/new-explicit -Mode Explicit
```

各 mode は新しい artifact directory で逐次実行する。実製品の IPC を使うため、別 mode・通常の Lhamiel と同時実行しない。

| Mode | 入口 |
|---|---|
| Explicit | 実 CLI `--extract`、先行 native 展開開始後に追加起動 |
| Automatic | 実 CLI 自動判定、先行開始後に追加起動 |
| AutomaticShortcut | 保存した通常 `.lnk` の実シェル起動に入力を追加 |
| Shortcut | 保存した `--extract` 専用 `.lnk` の実シェル起動に入力を追加 |
| Ui | 実 MainWindow + 実 StorageProvider の Drop routed event を 2 回 |
| UiBatch | 同じ実 Drop に 2 書庫を渡す |
| Batch | 実 CLI `--extract first.zip second.zip` |
| Snapshot | 設定オブジェクトの出力先を要求間で変え、各 worker の出力を照合 |

ZIP は System.IO.Compression で生成する。先行は 384 MiB と 20,000 小ファイル、追加は小さな正常ファイル。Batch/UiBatch だけ追加にも 384 MiB の Deflate Fastest エントリを含め、構造解析が先行する間に小書庫が完了して native 区間が交差しない測定を避ける。失敗条件を fixture 作成前に failure-cases.json へ保存する。
全 20,002 出力（Batch/UiBatch は 20,003）の SHA-256、製品の ZIP CRC 検証成功、worker ごとの PID・設定・native 展開開始ログ・要求 token 消費・自己終了を result.json と content-verification.json に保存する。
native の重なりは追加 worker の native 開始が先行 worker の Save 終了後の最終移動ログより前であることを確認する。追加開始が先行プロセス退出より前であること、worker exit code が 0、要求に保持した設定と実出力先の一致も確認する。詳細時系列は PID 別ログで確認できる。
両 native 区間の交差を検証し、worker プロセスの重なりと最終移動以降の後処理区間の重なりも別項目として保存する。後処理区間の重複は合格条件ではない。

キャンセル検証は親が用意した暗号化 ZIP または衝突 fixture を first.zip / second.zip として使える。

```powershell
$env:LHAMIEL_EXTRACTION_TEST_ROOT = 'C:\absolute\fixture'
$env:LHAMIEL_EXTRACTION_CANCEL_DELAY_MS = '10000'
& scripts/ExtractionE2E/bin/e2e/Lhamiel.Tests.Unit.exe --batch-cancel
```

batch-cancel-ready.json は実 worker 2 つの開始後に生成する。指定時間後に親 CTS を取り消し、製品が named event を伝播する。最大 60 秒で全 worker task の終了を待ち、batch-cancel-result.json に結果を保存する。
実ダイアログの表示は親の UI 証拠で確認する。worker task は子プロセスの退出を待つので、キャンセル後の task 完了をダイアログ閉鎖の根拠とする。

再検証だけ行う場合は `-VerifyOnly` を指定できる。過去 artifact の製品 hash は当時の result.json を正本とし、再検証前に保存しておく。
今回の成果物と `.bak` は削除しない。`.bak` は対応する元ファイルに Copy-Item で戻せる初版・修正前バックアップ。
