# 出力ゲートのプロセス間検証

失敗モードは Program.cs と各スクリプトの冒頭に記載する。実ゲートをリンクした独立プロセスで旧競合・修正後の相互待機・兄弟出力の並列・取消後の解放を確認する。

`dotnet run --project scripts/OutputGateE2E/OutputGateE2E.csproj -c Release -- <未使用の成果物絶対パス>`

製品を x64 Native AOT publish 後、他の製品/probe と重複せず以下を逐次実行する。設定は単回消費tokenへ渡し、ユーザーの設定は変更しない。

- `pwsh -NoProfile -File scripts/OutputGateE2E/Test-NativeWorkerGate.ps1 -AppPath <Lhamiel.exe絶対パス> -ArtifactDirectory <未使用絶対パス>`: 実workerの圧縮/展開が相手の出力保護を待ち、解除後の内容が一致することを確認。
- `pwsh -NoProfile -File scripts/OutputGateE2E/Test-NativeParallelCompression.ps1 -AppPath <Lhamiel.exe絶対パス> -ArtifactDirectory <未使用絶対パス>`: 同じ親の別ファイルへの実圧縮が並列に進み、後発が先に終わることとZIP内SHA256を確認。

result.json、ログ、ソースを保持する。生成データだけを共通ごみ箱helperで清掃する。安定名の製品lock cacheは削除しない。
