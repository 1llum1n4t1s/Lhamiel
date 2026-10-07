# Progress / IPC E2E

変更前の失敗条件: 同名・同セッションの worker を親より先に列挙すると前面化許可先が IPC 所有者と異なる。既存 WindowOpened 証拠の 2 PID がともに「展開中 - Lhamiel」、本文にも対象がない。

検証条件（コード変更前に固定）:
- 専用パイプ接続の callback は実 server PID を JSON 受信前に返す。同名の別 process が存在しても選択に影響しない。
- callback を指定しない既存 IPC API も送信できる。
- 同名の別親フォルダー、長いパス、複数対象で Title / 本文 / Automation name が対象を識別できる。長パスは最大高さ内で省略し全文 tooltip を持つ。
- 単体取消で別窓の token が取消されず、グループ取消は全対象であると表示される。
- ja_JP / en_US の画面を実際に開き、snapshot JSON と PNG を成果物として保存する。

実 GUI は親が統合 build 後に単独実行する。fixture の settings は専用 root に隔離し実ユーザー設定を参照・変更しない。

実行（統合 Release build 後、単独で実行）:
```powershell
dotnet build scripts/ProgressFocusE2E/Probe.csproj -c Release
$env:LHAMIEL_PROGRESS_TEST_ROOT = 'C:\Users\IMT\dev\Lhamiel\.codex\gogo-rere-parallel-20261007\progress-after-ui'
& scripts/ProgressFocusE2E/bin/Release/net10.0-windows8.0/win-x64/Lhamiel.Tests.Unit.exe
```
同じ root の再利用は拒否する。`--ipc-only` では GUI を開かず、同名 decoy を起動して専用パイプの実 PID / callback 先 / 受信前 callback / 既存 overload を検証する。`--before` は対象表示の欠落を記録して失敗条件を残す。before を新規ビルドするときは `-p:AssetDir=C:/Users/IMT/dev/Lhamiel/.codex/gogo-rere-parallel-20261007/baseline` を指定し、after build に戻す。

成果物: `ipc-owner.json`、`ipc-legacy.json`、`ja_JP-windows.json` / `en_US-windows.json`（タイトル・全文・tooltip・Automation名・実 bounds）、両 locale の `*-cancel.json`、画面PNG 各3枚、`result.json`。失敗は `*-failed.json` に保存。PNG は RenderTargetBitmap による実ウィンドウ内容のレンダリングで、OS タスク切替やスクリーンリーダー音声の試験ではない。

今回作成・保持した一時物: `scripts/ProgressFocusE2E/bin` / `obj`（probe実行物）、`.codex/gogo-rere-parallel-20261007/ipc-before-ui`（before結果・隔離settings・decoy signal）。before decoy は finally で退出済み。統合 GUI に必要なためこの時点で削除しない。復元は `.codex/gogo-rere-parallel-20261007/backups/ui/src/...` のファイルを元の同相対パスへコピーする。

追加境界の失敗条件（追加実装前に固定）:
- `C:\` / `D:\` / UNC share root の名前部分が空になる場合でも、Title と取消ボタンの Automation 名から root を識別できること。対象表示だけを行い、root やネットワークへの圧縮・I/O は実行しない。
- 5,000 件の synthetic group は全 target を保持し、本文の MaxHeight と取消ボタンの可視・画面内 bounds を保つこと。Show 前の準備時間と表示後の測定値を実値として記録し、性能改善・上限時間は断定しない。
- 新しい出力リンクのエラー key は全 17 locale に一度だけ存在し、全訳が `{0}` を保持すること。

追加成果物: 各 locale の `*-roots.json`（C/D/UNC の Title・取消名・本文・bounds）と `*-large-group.json` / `*-large-group.png`（5,000 target、Show 前準備時間、観測時間、target高さ、取消ボタンの画面内配置）。`displayObservationMs` は意図的な 300ms 待機を含む観測時間であり、描画所要時間そのものではない。追加変更の復元元は `.codex/gogo-rere-parallel-20261007/backups/ui/additional-boundaries`（locale17+probe source+README、19ファイル）。
