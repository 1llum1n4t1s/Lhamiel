# 並列圧縮の出力混入を検証する helper

製品の最新 Release ビルドを参照する隔離 E2E です。設定、入力、出力、登録台帳は指定した新規ディレクトリ内へ保存します。GUI は起動しません。

```powershell
dotnet run --project scripts/CompressionInputE2E/Probe.csproj -c Release -- '.codex/gogo-grok-parallel-20261007/inputs-after' --after
```

先に固定した失敗モードは、包含入力の個別並列圧縮で兄弟の final/temp/bak が混入すること、出力先が圧縮元配下にある単体・まとめ圧縮で自身の出力が混入すること、別 worker が走査中に終了してから完成した出力が入力になること、crash 残留 stage と登録の残存、既存の完成アーカイブや明示したファイル入力を誤除外することです。

実プロセスの正常終了と異常終了、走査中の出力生成、後発走査、明示入力、完成 ZIP 内容と元入力の SHA256、一括出力計画、単体・上書き・まとめ・包含組の個別圧縮、TAR/LZH の内部 sidecar を通します。異常終了残留の追跡清掃には隔離した設定台帳と将来時刻を渡します。既存ユーザー設定と実アプリの worker は操作しません。

`result.json` が成功時の再現成果物です。検証ディレクトリと生成物は調査証拠として保持し、不要になったらグローバルのごみ箱対応 cleanup helper で指定したディレクトリだけを清掃します。製品の GUI と通常の各起動入口は親が別途統括して検証します。

改修前の再現は `.codex/gogo-grok-parallel-20261007/inputs-before/result.json` に保存されています。元の before helper は `.codex/gogo-grok-parallel-20261007/backups/inputs/scripts/CompressionInputE2E/Program.cs` に保持しています。
