# 更新履歴

Lhamiel の利用者に関係する変更をバージョンごとにまとめています。日付はリリース日です。

更新は自動で適用されます（「全般」設定の「起動時にアップデートを確認」が ON のとき、メイン画面の起動時にチェックします）。手動で確認したいときは「バージョン」設定タブの「アップデート確認」ボタンを使ってください。

## 未リリース

## v1.0.209 — 2026-09-06

- **ダイアログのデザインを統一** — 確認・通知・パスワード・ファイル競合・問い合わせ・更新などの画面を、共通のタイトルバー、角丸の本文枠、区切り線付きの操作バーに揃えました。アクリル背景はメイン画面と同じ設定を使います
- **進捗・容量不足画面の表示を改善** — 内容に合わせて高さを調整し、進捗テキストや容量不足の説明が欠けないようにしました
- **パスワード画面終了時の入力消去を改善** — タイトルバーから閉じた場合や外部からキャンセルされた場合も、入力欄にパスワードが残らないようにしました

## v1.0.208 — 2026-09-05

- **大量選択時の右クリック操作を改善** — Windows 11 の右クリックメニューで選択したファイルのパスが起動引数の上限を超える場合も、一時リストを使って展開・圧縮へまとめて渡せるようにしました
- **右クリックメニュー設定の不要な再登録を抑制** — 登録済みパッケージのバージョン・配置先・状態が一致する場合は、再登録せずメニューの表示設定だけを切り替えるようにしました
- **アプリアイコン設定を移動** — 「全般」から「ショートカット」タブの一番上へ移動しました

## v1.0.207 — 2026-09-04

- **右クリックメニューの切り替えを修正** — 両方のチェックを OFF にしても登録パッケージを削除せず、メニューだけを非表示にするようにしました。Windows がパッケージの更新を保留している場合も、既存メニューの表示設定を切り替えられます
- **展開キャンセル時の原本保護を改善** — 上書き対象のファイルを一時退避した直後にキャンセルした場合も、元の場所へ復元するようにしました

## v1.0.206 — 2026-09-04

- **右クリックメニューを展開と圧縮に分離** — 「Lhamielで展開」と「Lhamielで圧縮」を個別に有効化できるようになりました。展開メニューは自己展開形式の EXE にも対応し、既存設定は従来の ON/OFF を両方へ引き継ぎます
- **用途別のデスクトップショートカットを追加** — 通常の「Lhamiel」に加えて、展開へ直行する「Lhamiel展開」と圧縮へ直行する「Lhamiel圧縮」を作成できます。いずれもダブルクリックでは通常どおり Lhamiel を起動します
- **設定画面を整理** — 「関連付け」と「バージョン」の間に「ショートカット」タブを新設し、ショートカット作成と右クリックメニュー設定を集約しました。デフォルトの圧縮形式は「圧縮」タブへ移動しました
- **エラーダイアログのデザインを統一** — 展開・圧縮エラーなどのメッセージ画面を、ほかのダイアログと同じ外観と操作感に揃えました

## v1.0.205 — 2026-08-30

- **内部コンポーネントを更新** — 圧縮・展開処理とログ出力に使うライブラリを最新安定版へ更新しました

## v1.0.204 — 2026-08-30

- **アプリアイコンに「レガシー」を追加** — 細かな装飾を整理して見やすくブラッシュアップしたデザインを、全般設定から3つ目のアイコンとして選べるようになりました

## v1.0.203 — 2026-08-29

- 白いデスクトップ背景が透ける場合でも、設定画面や各ダイアログの文字を読み取りやすい配色に改善しました
- 別のアプリが一時的に使用中のアーカイブは構造解析を再試行し、安全な構造を取得できない場合は不完全な状態で展開を続けないようにしました
- macOS 由来などの分解された Unicode ファイル名でも、展開後の Mark of the Web 伝播とスキップ処理を正しいファイルへ適用するようにしました
- ローカルドライブとネットワーク共有の空き容量を Windows API から正確に取得し、取得できない場合に容量チェックを誤って通過しないようにしました

## v1.0.202 — 2026-08-27

- 左ペインのタブアイコンをモノクロのアイコンに統一し、タブ切り替え時の右ペイン見出しにも同じアイコンを表示するようにしました
- 展開先に既存ファイルがある状態での上書き展開で、移動処理が途中で失敗しても元のファイルが失われないようにしました

## v1.0.201 — 2026-08-25

- 「バージョン」設定からLhamielについてのお問い合わせを送信し、対応状況を確認できるようにしました
- お問い合わせフォームの確認コード欄を表示したときも入力欄やボタンが重ならないよう、必要な高さへ自動で広がります
- Windowsの予約デバイス名を含むアーカイブの検査を強化し、既存の展開先にジャンクションがある場合もリンク先ファイルの属性を変更しないようにしました

## v1.0.200 — 2026-08-20

- **圧縮・展開後にフォルダを開く動作が既定のファイルマネージャーに従うよう修正** — これまでは常に Windows 標準エクスプローラーが開いていましたが、Kiriha や Files などをフォルダの既定アプリに設定している場合はそちらで開くようになりました

## v1.0.199 — 2026-08-19

- **アプリアイコンを2種類から選択可能** — 全般設定で、既定の「従来」と透明感のある「パステルクリスタル」をプレビュー付きで切り替えられるようになりました。選択したアイコンは各ウィンドウとデスクトップ／スタートメニューのショートカットへ反映され、アプリ更新後も維持されます
- **安全でないアーカイブ名による展開先逸脱を防止** — 空名、予約デバイス名、末尾のドットや空白などを含むアーカイブでは安全な代替フォルダー名を使い、意図しない親フォルダーや保護対象へ展開されないようにしました
- **展開用フォルダー内のジャンクション／シンボリックリンクを安全に拒否** — リンク先にある展開対象外のファイルを移動・削除してしまう可能性を防ぎました
- **異常終了後に残った一時フォルダーの回収を強化** — 出力ドライブ上に作成した作業用フォルダーも追跡し、次回起動時にリンク先をたどらない安全な方法で清掃するようにしました
- **処理されなかった起動要求を明示** — 更新処理中などで実行中のLhamielへ要求を転送できなかった場合にエラーを表示し、無効なコマンドライン入力だけで起動したプロセスが残らないようにしました

## v1.0.198 — 2026-08-14

- **圧縮元フォルダーごとの除外ルールを柔軟に設定可能** — `.lhamielignore` や `.gitignore` など、認識するファイル名と優先順位を設定できるようになりました。共通の `.lhaignore` と組み合わせて、フォルダーごとに圧縮対象を細かく制御できます
- **除外ファイルを空き容量の見積りから除外** — 圧縮前の必要容量を実際の圧縮対象だけで計算するようにし、大きな除外ファイルがあると空き容量不足と誤判定される問題を修正しました
- **複数の圧縮・展開操作が重なったときの安全性を向上** — ドラッグ＆ドロップ、コマンドライン、右クリックメニューから始まる処理を順番に実行し、同じ出力先への展開が競合しないようにしました
- **ファイル関連付けアイコンを2種類追加** — 「ゆるふわリボン」と「氷のフォルダ」を追加し、従来の2種類と合わせて設定画面から切り替えられるようになりました
- **内部コンポーネントを更新** — Windows のデータ保護ライブラリを 10.0.11 へ更新しました

## v1.0.197 — 2026-08-06

- **更新履歴を CHANGELOG.md に分離** — README に直接書いていた全バージョンの更新履歴を CHANGELOG.md へ移し、README からはリンクで参照する形にしました（アプリの動作面の変化はありません）

## v1.0.196 — 2026-08-06

- **リポジトリの依存自動更新設定を整理** — Dependabot の依存自動更新設定を他プロジェクトと統一し、脆弱性アラート・自動修正 PR を有効化しました（アプリの動作面の変化はありません）

## v1.0.195 — 2026-08-06

- **配布ファイルの発行者情報を Kagayoi に統一** — 実行ファイルのプロパティに表示される発行者名・著作権表記を屋号「Kagayoi」へ揃えました（動作面の変化はありません）

## v1.0.194 — 2026-08-02

- **ウィンドウの色味を調整** — テーマ色に重ねて表示していたアクセントカラーの色付けを廃止し、より自然な配色になりました
- **ウィンドウの透明度を向上** — アクリル背景の透明度を高め、背景がより透けて見えるようになりました

## v1.0.193 — 2026-07-27

- **展開エラー時の待ち時間を短縮** — 展開に失敗したファイルの処理方法を選ぶダイアログが表示されている間も、まとめてドロップした他の書庫の展開が止まらなくなりました
- **内部コンポーネントを更新** — 圧縮・展開ライブラリを 1.0.84 へ更新し、リソース解放まわりの堅牢性を高めました

## v1.0.192 — 2026-07-20

- **スタートメニューのショートカット作成を修正** — インストール時にスタートメニューの正しい場所へ Lhamiel のショートカットが作成されるよう、Velopack のパッケージ設定を修正しました

## v1.0.191 — 2026-07-15

- **Windows 11の新しい右クリックメニューに対応** — ファイルやフォルダを右クリックした直後のメニューへ「Lhamielへ」が表示されるようになりました。「その他のオプションを確認」を開かずに圧縮・展開を開始できます

## v1.0.190 — 2026-07-15

- **ファイルやフォルダの右クリックメニューに「Lhamielへ」を追加** — 選択した項目をショートカットへドロップしたときと同じ動作で圧縮・展開でき、全般設定からON/OFFを切り替えられます
- **アンインストール時の後片付けを強化** — スタートアップ登録と右クリックメニューに加え、Lhamielが設定した全対応形式のファイル関連付けも自動で解除します。別アプリへ変更済みの関連付けや共有の「プログラムから開く」情報は維持されます
- **内部コンポーネントを更新** — Avaloniaを12.1.0、Microsoft.NET.Test.Sdkを18.8.1、Windowsデータ保護ライブラリを10.0.10へ更新しました

## [1.0.189] — Git 記録日: 2026-07-15

- 右クリックメニューとアンインストール時の解除を追加

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/84f7c5fc46f26a37a8b83a8cb827da5c714846da) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/bb9550b810bb01f4393e7e4671fbae2762aba6c1...84f7c5fc46f26a37a8b83a8cb827da5c714846da)。

## v1.0.188 — 2026-07-01

- **展開失敗時にファイルが失われる可能性がある不具合を修正** — 展開中にエラーが起きて元のファイルを退避していた場合に、退避前の状態へ正しく復元されないことがある問題を修正しました
- **同名のファイル／フォルダが衝突したときの検出漏れを修正** — 展開先に同名のフォルダとファイルが両方存在するケースで、衝突が検出されず上書き破壊してしまうことがある問題を修正しました
- **ジャンクション／シンボリックリンクの先まで処理してしまう問題を修正** — 圧縮対象のフォルダ内にジャンクションやシンボリックリンクが含まれる場合、リンク先の本来は対象外のファイルまで圧縮・MotW（ダウンロードしたファイルの警告マーク）付与の対象になってしまう問題を修正しました
- **パスワード入力に失敗してもエラーが表示されないことがある問題を修正** — ヘッダー暗号化された 7z アーカイブでパスワード入力を 3 回失敗した際、無言で処理が中止されエラーメッセージが表示されないことがある問題を修正しました
- **複数ファイルをまとめて圧縮した際に進捗バーが早期に 100% で止まって見える問題を修正**
- **ファイル関連付けを解除すると関係ない拡張子の関連付けまで消えてしまう問題を修正**
- **圧縮の除外設定（.lhaignore）で一部のパターンが正しく機能しないことがある不具合を修正**
- その他、安定性に関わる内部修正を複数実施しました

## v1.0.187 — 2026-06-16

- **展開先フォルダが開かない問題を修正（再修正）** — エクスプローラーでアーカイブをダブルクリックして展開したとき、「展開先フォルダを開く」設定を ON にしていても展開先フォルダが開かないことがある問題を修正しました。v1.0.186 でも同様の修正を行っていましたが不十分だったため、フォルダを開く処理が完了してからアプリが終了するよう内部の終了処理を見直しました
- **ファイル関連付けが起動のたびに再登録される問題を修正** — アプリを起動するたびに全ての対応拡張子の関連付けが不要に再登録され、ログが関連付け処理で埋まる問題を修正しました。関連付けは設定を実際に変更したときだけ適用されるようになりました

## v1.0.186 — 2026-06-16

- **展開・圧縮の完了後に出力フォルダが開かない問題を修正** — エクスプローラーでアーカイブをダブルクリックしたり、アイコンにドロップして展開・圧縮したとき、「展開先（圧縮先）フォルダを開く」設定を ON にしていてもフォルダが開かないことがある問題を修正しました。あわせて、圧縮で「元と同じ場所に保存」を選んでいるときも、ドラッグ＆ドロップ後に出力フォルダが開くようになりました
- **バージョン画面にアプリアイコンを表示** — 「バージョン」設定タブのタイトル横にアプリアイコンを表示するようにしました
- **内部コンポーネントを最新化** — ログ出力ライブラリとアップデートダイアログを最新バージョンへ更新しました

## v1.0.185 — 2026-06-13

- **ウィンドウの背景に Windows のアクセントカラーをほんのり反映** — Windows の設定で選んでいるアクセントカラーを、ライト／ダークそれぞれのテーマ基調色の上にごく薄く重ねるようにしました（例: アクセントが青なら、ライトテーマの背景がほんのり水色がかります）
- **Windows の「透明効果」を OFF にしているときの背景を改善** — 透明効果を無効にしている環境で、ライトテーマの背景がくすんだ灰色に見えてしまう問題を解消し、テーマ本来のすっきりした背景色で表示するようにしました
- **サイドバーの影が途中で途切れて見える問題を修正** — 設定画面左側のサイドバーの影がウィンドウ下端で不自然に切れていたのを、自然にフェードするように調整しました
- **アプリアイコンを更新** — アプリ本体のアイコンデザインを刷新しました

## v1.0.184 — 2026-06-12

- **自動更新コンポーネントを最新化** — 内部の自動更新ライブラリ (Velopack) とアップデートダイアログを最新バージョンへ更新しました

## v1.0.183 — 2026-06-12

- **すべての実行ファイルにコード署名を導入** — インストーラー・アプリ本体・自動更新パッケージを Authenticode 署名（Certum Open Source Code Signing + タイムスタンプ）付きで配布するようになりました。ダウンロード時の SmartScreen 警告が段階的に改善されます
- **展開完了までの時間を短縮** — 展開後にアーカイブ全体をもう一度読み直して CRC 検証する処理を廃止しました。CRC は展開中に 7-Zip エンジンが常時照合しているため安全性は変わらず、大きなアーカイブほど展開完了が速くなります

## v1.0.182 — 2026-06-12

- **大量ファイル圧縮時に進捗が 100% のまま長時間止まって見える問題を修正** — 数十万ファイル規模の圧縮で、実際にはまだ処理中なのに進捗バーが早い段階で 100% に達してしまう内部ライブラリの集計バグを修正しました（7-Zip ラッパーライブラリ 1.0.79 へ更新）。あわせて、ファイル一覧の確認中・圧縮の準備中・仕上げ処理中など、これまで表示が止まって見えた区間にも進行状況のテキストを表示するようにしました
- **アップデート後にタスクバーのピン留めアイコンが白紙になる問題を修正** — 自動更新の適用後、タスクバーにピン留めした Lhamiel のアイコンが汎用の白紙アイコンになってしまうことがある問題に対処しました（反映には一度ピン留めし直しが必要な場合があります）
- **関連付けアイコンを選べるようになりました** — 「関連付け」設定にファイルアイコンの選択（クラシック / フォルダ）を追加し、選択中のアイコンをプレビュー表示します
- **アプリアイコンを更新** — アプリ本体のアイコンデザインを一新しました

## v1.0.181 — 2026-06-11

- **パスワード付き圧縮機能を追加** — ZIP / 7z を AES-256 で暗号化して作成できるようになりました。「圧縮」設定でパスワード保護を ON にすると、圧縮のたびに入力する「毎回入力」モードと、DPAPI（Windows アカウント紐づけ）で暗号化保存する「記憶する」モードを選べます。7z ではファイル名も隠すヘッダ暗号化に対応。パスワード付き 7z アーカイブ（ヘッダ暗号化含む）の展開・CRC 検証も強化しました
  - 補足: パスワード付き ZIP は WinZip AE-2（AES-256）形式のため、Windows エクスプローラー標準機能では展開できません（Lhamiel / 7-Zip / WinRAR 等をご利用ください）。また ZIP のパスワードは半角英数記号（ASCII）のみ対応です
- **まれにアプリ全体が応答しなくなる不具合を修正** — リモートデスクトップ環境などで、ログ書き込み中に Windows のユーザー名取得処理が応答しなくなり、アプリ全体が固まることがある問題を修正しました
- **内部ライブラリの更新** — 7-Zip ラッパーライブラリを更新し、大量のファイルを含むアーカイブ処理時のメモリリークを修正しました

## v1.0.180 — 2026-06-07

- **アクセスできないファイルが 1 つあるだけで圧縮全体が失敗する不具合を修正** — Visual Studio が握っている `.vs\<sln>\FileContentIndex\*.vsidx` のように、他プロセスが完全排他（`FileShare.None`）で開いているファイルを圧縮対象に含めると、圧縮そのものが途中で止まってしまう問題がありました。アクセスできないファイルだけスキップして、残りのファイルで圧縮を最後まで続行するようにしました（スキップ件数はログに記録されます）

## v1.0.179 — 2026-06-06

- **ネスト `.gitignore` 尊重時の除外判定を git 準拠に修正** — 「サブフォルダの `.gitignore` を尊重」を ON にして圧縮する際、ディレクトリを否定パターン（`!`）で復活指定しているケース（例: Xcode プロジェクトの共有設定 `*.xcodeproj/*` ＋ `!*.xcodeproj/xcshareddata/`）で、その配下のファイルがアーカイブから漏れてしまう不具合を修正しました。git 本体と同じ判定になり、否定で復活させたフォルダの中身が正しくアーカイブに含まれます

## v1.0.178 — 2026-06-06

- **複数アーカイブの同時処理の安定性向上** — 複数のアーカイブをまとめてドラッグ＆ドロップして並行に展開・圧縮する際、内部の 7-Zip ネイティブ処理を 1 件ずつ順番に行うように整理しました。まれに発生しうる競合や予期しない停止を防ぎ、バッチ処理がより安定します
- **展開後に出力先へ空の一時フォルダが残る不具合を修正** — 展開のたびに出力フォルダ内へ空の作業用フォルダが残ってしまう問題を解消しました
- **圧縮の除外設定が空フォルダ経由で漏れる不具合を修正** — `.lhaignore` で除外したファイルが、空ディレクトリの取り込み処理を通じてアーカイブに混入することがある問題を修正しました
- **破損・暗号化アーカイブのエラー表示を改善** — 同梱の圧縮ライブラリを更新し、破損したアーカイブが「キャンセル」と誤って扱われるケースなど、エラーの種類判定をより正確にしました

## v1.0.177 — 2026-06-01

- **依存ライブラリ更新** — 自動更新ダイアログライブラリ `VelopackUpdateDialog.Avalonia` を 1.0.5 → 1.0.6 に更新（同梱される自動更新ライブラリ Velopack も 1.0.1 → 1.1.1 に追従）。安定性向上・最新化のためのメンテナンス更新で、アプリ機能の変更はありません

## v1.0.176 — 2026-05-30

- **複数起動時にメイン画面が前面に出ない不具合を修正** — 関連付けやアイコンへのドロップでの圧縮処理中に、ショートカットやスタートメニューからアプリを再起動してもメイン画面が表示されないことがある問題を修正。すでに起動しているインスタンスのメイン画面が確実に前面化されるようになりました

## v1.0.175 — 2026-05-29

- **依存ライブラリ更新** — 自動更新ダイアログライブラリ `VelopackUpdateDialog.Avalonia` を 1.0.4 → 1.0.5 に更新。安定性向上・最新化のためのメンテナンス更新で、アプリ機能の変更はありません

## v1.0.174 — 2026-05-29

- **ドキュメント更新** — v1.0.172 で「アップデート確認」ボタンが `Could not find file 'NuGet.Versioning'` エラーで失敗する既知の不具合（v1.0.173 で修正済み）について、README 冒頭に案内を追加。アプリ機能の変更はありません

## v1.0.173 — 2026-05-29

- **依存ライブラリ更新** — UI フレームワーク Avalonia を 12.0.3 → 12.0.4 に、自動更新ダイアログライブラリ `VelopackUpdateDialog.Avalonia` を 1.0.3 → 1.0.4 に更新。アプリ機能の変更はなく、安定性向上・最新化のためのメンテナンス更新
- **自動更新ライブラリの参照整理** — 本体が直接参照していた `Velopack` を `VelopackUpdateDialog.Avalonia` 経由の参照に一本化（同梱される Velopack のバージョンは 1.0.1 のまま据え置き）。重複参照を解消する内部整理で、自動更新の動作に変更はなし

## v1.0.172 — 2026-05-27

- **Velopack を 1.0.1 に更新** — 自動更新ライブラリを GA 版（`0.0.1369-g1d5c984` プレリリース → `1.0.1` 正式リリース）に切り替え。アップデート判定・差分ダウンロード・適用ロジックの安定性が向上
- **テスト基盤を Microsoft.NET.Test.Sdk 18.6.0 に更新** — 開発時のテスト実行環境を最新版に追従

## v1.0.171 — 2026-05-27

<img width="600" alt="image" src="https://github.com/user-attachments/assets/b976bdb8-d5d7-4562-a371-9b3f46389614" />

- **圧縮時の除外パターンを `.gitignore` 互換構文に刷新** — 従来のファイル名/フォルダ名のリテラル一致から、`.gitignore` 仕様のグロブ（`*` / `?` / `**` / `[abc]`）、否定（先頭 `!`）、アンカー（先頭 `/`）、ディレクトリ限定（末尾 `/`）に対応。除外設定は `%LocalAppData%\Lhamiel\.lhaignore` に保存され、圧縮設定タブから「追加 / 削除 / 既定に戻す / 除外設定ファイルを開く」の 4 操作で管理可能。お好きなテキストエディタで `.lhaignore` を直接編集しても即座に UI に反映される
- **ネスト `.gitignore` 取り込み（オプトイン）** — 「全般」設定の「サブフォルダの `.gitignore` も尊重する」を ON にすると、圧縮対象のサブディレクトリ内にある `.gitignore` を自動で取り込んで除外判定に使用。プロジェクトコードをそのまま圧縮するときに `.gitignore` で除外しているファイルをまとめて外せる
- **フィードバックボタン押下後の UI フリーズを修正 (Issue #54)** — 「バージョン」設定タブの「フィードバックを送る (GitHub)」を押下した直後にアプリ全体が操作不能になる事象を修正。「展開後にフォルダを開く」「除外設定ファイルを開く」など外部アプリを起動する他の経路も同じ仕組みで統一し、ブラウザ・エクスプローラー・エディタの起動時に UI が固まらなくなった
- **USB ドライブ抜去時のエラー判定を安定化** — 展開・圧縮処理中に USB ドライブを抜いた際のエラー検出が誤判定するケースを修正
- **ダウンロード導線を R2 直リンクに変更** — README の「インストール方法」と「自動更新が失敗する場合」のリンクを Cloudflare R2 配信元の直リンク（x64: `https://lhamiel.kagayoi.com/Lhamiel-win-Setup.exe` / ARM64: `Lhamiel-win-arm64-Setup.exe`）に変更。固定 URL なので常に最新版がダウンロードされる
- **品質改善** — `.gitignore` パターン解釈の仕様準拠強化（`**` 境界、ネゲート文字クラス、character class range 内の `/` 除外、否定ディレクトリルールの挙動など）、設定ファイル破損時の段階的フォールバック整理、起動失敗時の緊急ログ書き出し、ロック中ファイルのリトライ動作改善など多数の内部品質改善

## v1.0.170 — 2026-05-20

- **自動更新の配信ドメインを中立ドメイン `lhamiel.kagayoi.com` に移行** — 旧 `lhamiel.1llum1n4t1.com` はクラウド/企業の egress セキュリティが SNI ベースのフィルタで誤検知し、更新確認時に `Received an unexpected EOF or 0 bytes from the transport stream` を引き起こす事例があったため、中立ドメインへ切替。配信元の R2 バケット (`lhamiel-updates`) は変更なし。旧ドメインは配信期間が短くクリーン廃止。超旧 `GithubSource` クライアント (v1.0.167 以下) 救済のため GitHub Releases には本バージョン (kagayoi.com 版) を踏み台として publish。アプリ機能の変更はなし

## v1.0.169 — 2026-05-20

- **R2 移行の踏み台バージョン** — 旧 `GithubSource` クライアント (v1.0.167 以下) が Cloudflare R2 配信へ乗り換えるための "踏み台" として、本バージョンを GitHub Releases にも publish。旧クライアントはこれを経由して R2 化 (2 段階アップデート) する。**以降の通常リリースは R2 単独配信**で、GitHub Releases への継続 publish はしない (踏み台 Release は削除せず永続保持)。アプリ機能の変更はなし

## v1.0.168 — 2026-05-20

- **自動更新の配信元に Cloudflare R2 を追加 (GitHub Releases と併用配信)** — `Settings.UpdateBaseUrl` (`https://lhamiel.1llum1n4t1.com`、`[JsonIgnore]` + getter-only ハードコード固定) を `Velopack.Sources.SimpleWebSource` 経由で取得。新クライアントは R2 から、旧クライアント (v1.0.167 以下の `GithubSource` クライアント) は引き続き GitHub Releases から自動更新を受け取る。CI workflow (`velopack-release.yml`) に R2 アップロード job (`wrangler@4.92.0` で `wrangler r2 object put` + 配信確認 `curl --fail` HTTP 200 検証) を追加し、既存の GitHub Releases upload job は **Legacy fallback** として `continue-on-error: true` + `needs: [..., r2-upload]` で併用継続 (`actions/setup-node@v6.4.0` SHA pin)。数バージョン経過後に旧クライアントが概ね R2 版へ移行したタイミングで GitHub Releases 側を停止予定
- **アップデート確認ボタンの自動無効化** — `App.UpdateCheckStateChanged` 静的イベントを追加し、`_isCheckingUpdate` フラグ遷移を `TryBeginUpdateCheck` / `EndUpdateCheck` ヘルパーで一元化。`MainWindowViewModel.IsCheckingUpdate` がイベントを購読することで、起動時自動チェック中も「アップデート確認」ボタンが disabled になる (並走実行を未然に防止)
- **Velopack 重複ダイアログ撤去** — 17 ロケールから `Text.Update.AlreadyChecking` キーを削除、`App.Check4Update` の `MessageService.ShowInfo("AlreadyChecking")` 呼び出しを撤去 (Velopack 自身のプログレスダイアログと表示が重複していたため)
- **テスト** — `SettingsTests` / `VelopackIntegrationAdversarialTests` を `UpdateBaseUrl` の JSON injection 防御 / 不変性テストに更新 (692/692 合格)
- **ドキュメント** — `docs/SETTINGS_SCHEMA.md` / `docs/ARCHITECTURE.md` / `CLAUDE.md` を R2 配信元に追従

## v1.0.167 — 2026-05-18

- **「アップデート確認」ボタンのサイレント failure を修正** — リポジトリ未設定 / 開発実行 (`IsInstalled=false`) / 既に確認中の 3 経路で UI フィードバックがなく「ボタンを押しても何も起きない」状態だったのを、それぞれメッセージダイアログで明示するよう修正。17 ロケールに `Text.Update.AlreadyChecking` を追加
- **プライバシー強化** — MiniDump の tier を `MiniDumpWithDataSegs + MiniDumpWithHandleData` (0x05) から `MiniDumpNormal` (0x00) に削減し、診断 ZIP 経由でグローバル変数 (Settings 内容) やファイルハンドル情報が漏れる経路を遮断。`PasswordDialog` も取得直後にダイアログ側 Password 参照を即クリア
- **信頼性向上** — `Logger._logger` 未初期化時の `LogException` で `%LocalAppData%\Lhamiel\Lhamiel_emergency.log` への直書きフォールバックを追加。Avalonia 起動失敗時にも例外情報を残せるように。`DiagnosticsCollector` も Logger 非同期クリーンアップとの race で `FileNotFoundException` / `DirectoryNotFoundException` が出ても警告を出さず無視する
- **パフォーマンス** — `MotwPropagator.PropagateToDirectory` を `Parallel.ForEach` で並列化 (CPU 数上限、最大 8)。`ArchiveExtractor.DetectExtractionConflicts` の stat 重複 (`File.Exists` + `new FileInfo`) を 1 回にまとめて I/O 半減。`IpcService` の固定 50ms リトライを指数バックオフ (50ms → 400ms) に変更
- **保守性** — `PartialExtractionHandler` の未使用 `[Obsolete] RetryExtraction` 削除。`AppPathResolverTests` を Windows 専用テスト 3 件に再構成。`IpcServiceTests` に `[Collection("Sequential")]` を付与してパイプ名衝突による flaky を解消
- **ドキュメント** — `SETTINGS_SCHEMA.md` に `Check4UpdatesOnStartup` / `IgnoreUpdateTag` を追加。`ARCHITECTURE.md` の Infrastructure / Utility レイヤー一覧を最新化

## v1.0.166 — 2026-05-05

- **Hidden/System 属性の圧縮対象化** — 圧縮時のファイル列挙で `.git` など Hidden/System 属性のファイル・フォルダを既定で含めるよう変更。設定から従来どおりスキップする挙動にも切り替え可能
- **圧縮除外リスト管理UIを追加** — 圧縮設定から除外パターンの追加・削除・既定値リセットが可能に。`.DS_Store`、`Thumbs.db`、`node_modules`、`__MACOSX` などを既定除外として管理
- **設定とドキュメント整備** — 17言語ローカライズ、設定スキーマ、AGENTS.md を実装に合わせて更新

## [1.0.165] — Git 記録日: 2026-05-03

- 展開後の移動が一時的なファイルロックで失敗する場合に再試行し、エラーダイアログの表示と全体の安定性を改善。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/0812bad3f63b2a5f6dca866a7cf931be74f8b819) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/5c30ca22ffcdd1acc50f96b8e642afc541a7352e...0812bad3f63b2a5f6dca866a7cf931be74f8b819)。

## v1.0.164 — 2026-04-29

- **依存パッケージ更新** — Avalonia 12.0.1 → 12.0.2（バグ修正パッチ：OneWay バインディング修正、TabControl 高速切替クラッシュ修正等）、MessageBox.Avalonia 3.3.1.1 → 12.0.0（Avalonia 12 対応版、API 互換）、Microsoft.NET.Test.Sdk 18.4.0 → 18.5.1
- **確認ダイアログのデザイン統一** — サポートページ遷移時の確認ダイアログを `MessageBox.Avalonia` の既定デザインから、他のダイアログと同じアクリル背景のカスタムデザイン (`ConfirmDialog`) に統一

## v1.0.163 — 2026-04-29

- **ロケール辞書のロード方式を ResourceInclude 静的登録に戻す** — v1.0.162 までの「選択ロケールのみ `AvaloniaXamlLoader.Load` でオンデマンドロード」方式は Native AOT / compiled XAML 環境で辞書がビルド成果物に含まれない問題があったため、17 言語すべてを `App.axaml` の `ResourceInclude` として静的登録する方式に戻した。`MergedDictionaries` への投入は引き続き選択ロケールのみで、複数言語のキーが同時に有効化されることはない
- **`_localeCache` 撤去** — 上記方式変更に伴い `App.axaml.cs` のオンデマンドキャッシュ用 `Dictionary<string, IResourceProvider>` および try/catch 付きランタイムロード処理を削除。`Resources[localeKey]` 参照のみのシンプルな実装に
- **依存パッケージ更新** — `Microsoft.DotNet.ILCompiler` / `Microsoft.NET.ILLink.Tasks` を 10.0.6 → 10.0.7 に更新（Native AOT ツールチェーン）

## v1.0.162 — 2026-04-25

- **/rere 6 人分隊レビュー指摘 33 件を再適用 + 17 ラウンドのレビュー反映** — v1.0.161 ベースに対して PR #47 の 33 件を再提出（PR #48）し、CodeRabbit / Gemini / Codex の 17 ラウンド指摘を順次反映。主な変更:
  - **セキュリティ**: `IsSystemCriticalDirectory` のサブディレクトリ未保護バグ修正（`C:\Windows\System32\drivers` 等が settings.json 改竄経由で素通りしていた経路を `StartsWith` ベース + Subdir/Exact 2 段分割で塞いだ）/ Mutex（`Local\` プレフィックス）+ IPC パイプ名（`_S<SessionId>` 付与）+ `ActivateExistingInstance` のセッションスコープ統一 / NTFS ADS（`:`）拒否 / `--format` 引数の allow-list 検証 / CRDebugger.Avalonia をリリースバイナリから除外
  - **障害耐性**: Settings 破損時の段階的フォールバック（Move → Delete → 空 JSON 上書き）/ Logger 未初期化時のサイレント握りつぶし対策 / `PasswordDialog` の Cancel-前-Show Race 解消 / `FileConflictDialog` 二重 Closed 冪等化 / IPC タイムアウト逆転（`RequestReadTimeoutMs > ConnectTotalTimeoutMs`）解消
  - **設定永続化**: 出力先設定の `Directory.Exists` チェックを撤去し、未接続 NAS / USB ドライブが Desktop へサイレントリセットされる経路を解消
  - **CI**: `gh release create` に `--target ${{ github.sha }}` を明示（タグが既定ブランチ HEAD ではなく push 対象 commit に固定される）
  - **パフォーマンス**: `PathValidator` の重複コードを `ResolveNormalizedCandidates` / `IsAnyCandidateProtected` に共通化 / IPC リクエストの `Span<byte>` 直接 Deserialize / `ArrayPool` 経由バッファ管理 / `Parallel.ForEachAsync` への移行
  - **ドキュメント**: SETTINGS_SCHEMA.md / CLAUDE.md / AGENTS.md の整合性回復、サイレントスキップ xUnit を `Assert.SkipUnless` 化
- **テストカバレッジ強化** — `IsSystemCriticalDirectory` 直接テスト 11 件、Theme 正規化テスト 3 件、UpdateChannel canonical テスト 1 件等を追加し 555 → 568 件
- **総コミット数**: PR #48 で 17 ラウンドにわたる修正を 28 ファイル / +1713 / -264 行で squash merge

## v1.0.161 — 2026-04-25

- **v1.0.160 を取り下げ、v1.0.159 の状態で再リリース** — v1.0.160 で取り込んだ修正に不具合が確認されたため、当該変更を revert し、v1.0.159 と同等のコード状態に戻したうえで再リリース。v1.0.160 の GitHub Releases / タグは削除済み

## [1.0.160] — Git 記録日: 2026-04-25

- 利用案内と開発用文書を更新。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/d668b171e0d895ea552b5914c489b36ac6b7f7d4) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/95ea96e292320c8f9bd3c6167eb848dea19d1cb2...d668b171e0d895ea552b5914c489b36ac6b7f7d4)。

## v1.0.159 — 2026-04-22

- **パスワード保護アーカイブ対応** — 暗号化 ZIP / 7z / RAR をドロップすると専用の入力ダイアログが開くように。誤入力時は再試行メッセージ付きで自動的にダイアログが再表示される
- **暗号化エラーの表示改善** — これまで「I/O error occurred.」と表示されていた `EncryptionException` を、ユーザーが理解できる「パスワードが必要、または不一致です」に分類して案内
- **パスワード入力キャンセル対応** — ダイアログで「キャンセル」を押した場合は通常のキャンセルフローとして扱い、誤って「パスワードが違います」と案内しないよう修正
- **ロケール拡張** — 17 言語に `Text.Password.*` / `Text.ErrorHandler.EncryptedOrWrongPassword*` の 7 キーを追加

## v1.0.158 — 2026-04-20

- **セキュリティ強化** — Zip Slip ガード、`UpdateRepoOwner/Name` のハードコード固定化、保護ディレクトリチェック、`IsProtectedDirectory` のシンボリックリンク追跡、`FolderOpener` の `ArgumentList` 化、IPC の `PipeOptions.CurrentUserOnly`、GitHub Actions の SHA 固定など P0/P1 系 25 件を反映
- **設定スナップショット** — `Settings.Snapshot` / `SettingsManager.CreateSnapshot` を導入し、圧縮・展開中に UI 側で設定を変更されても race しないよう根治
- **パフォーマンス改善** — `ShouldExcludeFile` の stackalloc 64 超え対応、`DetectFileSystemConflicts` の stat 回数削減、`DeduplicateByIdentity` の OS 依存コンパラ最適化、`FileIconHelper` のスレッドセーフなアイコンキャッシュ、`RefreshCompressionLevels` の差分更新化など 12 件
- **7z.dll 配置刷新** — CI での直ダウンロードを廃止し `1llum1n4t1s.Sevenzip` NuGet 同梱方式に一本化
- **TempCleanup 追加** — アプリ起動時に残存した `Lhamiel_Temp_*` ディレクトリを安全に掃除
- **ロケール追加キー** — 17 言語に `Error.ProtectedDirectory` / `Compressor.Processing` を追加
- **ドキュメント整合** — `SETTINGS_SCHEMA.md` / `ARCHITECTURE.md` を実装と同期
- **テスト** — 524 / 524 合格（`UpdateRepoOwner_IsHardcodedAndImmutable` 新規追加）

## v1.0.157 — 2026-04-16

- **依存パッケージ更新** — 1llum1n4t1s.Sevenzip 1.0.66、SuperLightLogger 1.0.6、CRDebugger.Avalonia 1.0.24 に更新。パフォーマンス改善とバグ修正を含む
- **コンパイル済みバインディング有効化** — メイン画面に `x:CompileBindings` を適用しバインディングパフォーマンスを向上
- **UI調整** — タイトルバーにバージョン表示を追加、アクリル背景の透明度を調整

## [1.0.156] — Git 記録日: 2026-04-16

- NLog→SuperLightLogger移行、7z.dll同梱方式をNuGet自動配置に刷新、テスト時のexplorer起動を抑止

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/6b96a860becf0d356cc00912f5579157956f4f63) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/cfc628b6a8bfd8dd677adae1333a6a3f0e2a0cac...6b96a860becf0d356cc00912f5579157956f4f63)。

## [1.0.154] — Git 記録日: 2026-04-12

- 不要コード削除・パフォーマンス改善・UI修正・adversarialテスト追加
- README に v1.0.152 の変更履歴を追加

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/cfc628b6a8bfd8dd677adae1333a6a3f0e2a0cac) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/2a83c588f23a453714b2943efd4d87a1f3523982...cfc628b6a8bfd8dd677adae1333a6a3f0e2a0cac)。

## v1.0.152 — 2026-04-11

- **Avalonia 12 対応** — Avalonia UI を 11.3 から 12.0 にアップグレード。削除された `ExtendClientAreaChromeHints` プロパティを除去
- **圧縮時の一時コピー方式を最適化** — 全ファイルを事前コピーする方式を廃止し、ライブラリ（1llum1n4t1s.Sevenzip v1.0.52）側でロック中ファイルのみ自動コピーする方式に変更。ディスク使用量と処理時間を大幅に削減

## v1.0.150 — 2026-04-11

- **展開後にフォルダを開く機能** — 展開完了後にアーカイブ名フォルダをエクスプローラーで自動的に開く機能を追加。二重ネスト防止スキップ時もルートフォルダを正しく開くよう対応
- **展開後に開くフォルダのバグ修正** — 「展開後にフォルダを開く」設定ON時にアーカイブ名フォルダではなく親ディレクトリが開かれるバグを修正
- **圧縮コピーの I/O 最適化** — 圧縮前の一時コピー処理でバッファサイズ最適化、`CopyToAsync` を手動ループに置き換えてスナップショット一貫性を保証
- **圧縮・展開パフォーマンス改善** — 圧縮・展開処理全体のパフォーマンスを最適化
- **TOCTOU レース修正** — 圧縮時のゼロバイトファイル判定における競合状態を修正
- **CI ビルド安定性向上** — 7z.dll ダウンロードにリトライ処理を追加し、タイムアウトによるビルド失敗を防止

## [1.0.148] — Git 記録日: 2026-04-07

- 展開後に開くフォルダが親ディレクトリになるバグを修正し、バージョンを 1.0.148 に更新
- 展開後にアーカイブ名フォルダを開く挙動を追加
- PRレビュー指摘対応（4件）

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/cbc95c851106864cf058163517ecc46303f35f93) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/bcd5c1a947506175d2d3487e8d92c643d305342f...cbc95c851106864cf058163517ecc46303f35f93)。

## [1.0.146] — Git 記録日: 2026-04-05

- 圧縮・展開処理のパフォーマンス最適化
- 7z.dll ダウンロードにリトライ処理を追加（タイムアウト対策）

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/bcd5c1a947506175d2d3487e8d92c643d305342f) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/2e92e11641db1038cd840272d527e72d6709300a...bcd5c1a947506175d2d3487e8d92c643d305342f)。

## v1.0.144 — 2026-04-02

- **展開ロジックの簡略化** — スマート展開の複雑なヒューリスティック判定を除去。「アーカイブ名でフォルダを作成する」設定に基づくシンプルな ON/OFF 方式に変更。二重ネスト防止はアーカイブのルートフォルダ名とアーカイブ名の一致判定のみで実現
- **ロック中ファイルの圧縮対応** — 圧縮前に全ファイルを一時コピーすることで、プロセスがロック中のファイルも圧縮可能に
- **進捗ダイアログの改善** — 進捗表示の精度向上とUI改善
- **全ハードコード文字列のローカライズ** — コード内に残っていたハードコード文字列を全てローカライズリソースに移行
- **複合アーカイブ拡張子の正規化** — `.tar.gz` / `.tar.xz` 等の複合コンテナ拡張子を正しく処理してフォルダ名を決定
- **空ディレクトリの圧縮修正** — 空ディレクトリが圧縮時にアーカイブに含まれないバグを修正
- **展開時のバグ修正** — 一時フォルダ方式の展開における衝突検出・上書き確認の不具合を複数修正

## [1.0.142] — Git 記録日: 2026-03-28

- 進捗ダイアログの改善 + 全ハードコード文字列のローカライズ + コード最適化

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/b85afc06f91129a4e8f42996bbdc10bbe220d020) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/dbf4954a6406c98a8b76be5cccce0bd53b546bec...b85afc06f91129a4e8f42996bbdc10bbe220d020)。

## [1.0.140] — Git 記録日: 2026-03-28

- CRDebugger によるデバッグモード・ダイアログプレビュー + FileConflictDialog 改善 + バージョン 1.0.140

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/dbf4954a6406c98a8b76be5cccce0bd53b546bec) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/72dcf3e28cccca62aa652309b91dbff8d89165fa...dbf4954a6406c98a8b76be5cccce0bd53b546bec)。

## [1.0.136] — Git 記録日: 2026-03-27

- 圧縮時に全ファイルを一時コピーしてロック中ファイルも圧縮可能に

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/72dcf3e28cccca62aa652309b91dbff8d89165fa) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/bd424db4b11a8063e6b8878d3b4addc3ad3c076a...72dcf3e28cccca62aa652309b91dbff8d89165fa)。

## [1.0.134] — Git 記録日: 2026-03-27

- 空ディレクトリが圧縮時にアーカイブに含まれないバグを修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/bd424db4b11a8063e6b8878d3b4addc3ad3c076a) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/a06c13fcaf3c9aae54cf321f807e102384e1ea79...bd424db4b11a8063e6b8878d3b4addc3ad3c076a)。

## [1.0.132] — Git 記録日: 2026-03-25

- 同名ファイルやファイル・フォルダーの衝突を検出し、上書き確認で選んだ保持・置換の動作を尊重。空ディレクトリと置換失敗時の原本を保護。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/a06c13fcaf3c9aae54cf321f807e102384e1ea79) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/9c82ac91c01ce72cb7cacac0175bc94c3f213c70...a06c13fcaf3c9aae54cf321f807e102384e1ea79)。

## v1.0.130 — 2026-03-25

- **ファイル衝突ダイアログ** — 展開・圧縮時に同名ファイルが競合した場合、Windows風のファイル比較ダイアログで選択的に処理。サムネイル表示・一括チェック・同一ファイルスキップ機能を搭載
- **展開オプション追加** — 「アーカイブ名でフォルダを作成する」設定を追加。OFFにするとアーカイブ内容を直接展開先に配置
- **圧縮オプション追加** — ディレクトリ構造モード（ルートディレクトリを含める / 含めない / フラット）を選択可能に
- **ディスク容量チェック** — 展開・圧縮前に空き容量を事前チェック。処理中も定期監視し、容量不足時は一時停止して対応可能
- **アクリルブラー効果** — 全ダイアログにアクリルブラー背景を適用
- **ARM64対応** — Windows ARM64ビルドを追加

## [1.0.123] — Git 記録日: 2026-03-21

- パフォーマンス最適化・コード簡潔化・実装正規化
- ARM64ビルドのリストアエラーを修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/79c744492836614dbf00f38dafd31a64274a6d53) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/88a314417361599ed20c3c3e027c7e50e3ca003e...79c744492836614dbf00f38dafd31a64274a6d53)。

## [1.0.122] — Git 記録日: 2026-03-19

- ワークフローをKomorebiパターンに再構成
- リリースワークフローのアーキテクチャ間競合を修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/88a314417361599ed20c3c3e027c7e50e3ca003e) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/97de9734c6fbb7a7034e88fd364714016f379b09...88a314417361599ed20c3c3e027c7e50e3ca003e)。

## [1.0.120] — Git 記録日: 2026-03-19

- ARM64 (Windows) ビルド対応

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/97de9734c6fbb7a7034e88fd364714016f379b09) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/e3d080d1e8a63f25d23040e7d562a4c46f401295...97de9734c6fbb7a7034e88fd364714016f379b09)。

## v1.0.118 — 2026-03-19

- **ローカライズ不具合の修正** — アプリ起動時にロケール辞書が適用されず、全UIにリソースキーがそのまま表示される問題を修正
- **バージョンタブに7-Zipバージョン表示** — 設定画面のバージョンタブで使用中の7-Zipライブラリバージョンを表示
- **ファイル関連付けの即時反映** — ファイル関連付けの変更が即座にシステムに適用されるよう修正
- **ライセンス参照の修正** — ライセンス表示のリンク先を修正
- **アクセントカラーオーバーレイ追加** — UIにアクセントカラーのオーバーレイ効果を追加

## [1.0.116] — Git 記録日: 2026-03-19

- ローカライズ全面崩壊の根本原因を修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/b11f1aa444e4abb553a260f713a0eec5e332584f) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/e84bc3b36544a64cee3058788eb4da129ae1ac07...b11f1aa444e4abb553a260f713a0eec5e332584f)。

## [1.0.114] — Git 記録日: 2026-03-19

- 上書き確認ダイアログのローカライズ不具合を修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/e84bc3b36544a64cee3058788eb4da129ae1ac07) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/f6f64ae15aaa0fabe0c3c081fb16bc2e82f00734...e84bc3b36544a64cee3058788eb4da129ae1ac07)。

## [1.0.112] — Git 記録日: 2026-03-17

- バージョンタブに7-Zipバージョン表示、ライセンス参照の修正、アクセントカラーオーバーレイ追加
- 7zライブラリ名の表記を1llum1n4t1s.Sevenzip に修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/f6f64ae15aaa0fabe0c3c081fb16bc2e82f00734) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/ab2b79c4de24945693b7a609a8d758526e21a691...f6f64ae15aaa0fabe0c3c081fb16bc2e82f00734)。

## [1.0.110] — Git 記録日: 2026-03-16

- Vector掲載基準に準拠したREADME修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/ab2b79c4de24945693b7a609a8d758526e21a691) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/c78e1ebfaa9e2bc7857aa2c8b9735a425f2fcaa8...ab2b79c4de24945693b7a609a8d758526e21a691)。

## [1.0.108] — Git 記録日: 2026-03-16

- ファイル関連付けの変更が即座に適用されない問題を修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/c78e1ebfaa9e2bc7857aa2c8b9735a425f2fcaa8) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/24c5e43a8047ca5bd747201472917ef0edf226f9...c78e1ebfaa9e2bc7857aa2c8b9735a425f2fcaa8)。

## v1.0.106 — 2026-03-16

- **デザインリニューアル** — サイドバーにアクリル風マテリアルを適用
- **テーマ切替** — ダーク / ライト / システム追従の3モード対応
- **17言語対応** — ラテン語、サンスクリット語を追加
- **複数ファイルまとめ圧縮** — 複数ファイルを1つのアーカイブにまとめる機能を追加
- **複数ファイル処理のバグ修正** — 複数ファイル指定時にアーカイブファイルが誤って展開される問題を修正

## [1.0.102] — Git 記録日: 2026-03-16

- プロジェクト構造変更、ラテン語・サンスクリット語追加、UI改善

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/04406f80fd5cdbf794265fd6149c0d05694ff292) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/d50d2e892dd4c5a2c0ce38fdcf21d26915472933...04406f80fd5cdbf794265fd6149c0d05694ff292)。

## [1.0.100] — Git 記録日: 2026-03-16

- テーマComboBoxをAOT安全なThemeItem recordに変更

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/d50d2e892dd4c5a2c0ce38fdcf21d26915472933) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/955ede413134c1c16ab3095c44f4c8cee6c91a20...d50d2e892dd4c5a2c0ce38fdcf21d26915472933)。

## [1.0.98] — Git 記録日: 2026-03-16

- 複数ファイルのコマンドライン圧縮対応、テーマドロップダウン修正、バグ修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/955ede413134c1c16ab3095c44f4c8cee6c91a20) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/58785eba76e1573baef51e19d905822cbb8eea60...955ede413134c1c16ab3095c44f4c8cee6c91a20)。

## [1.0.94] — Git 記録日: 2026-03-16

- 複数ファイルまとめ圧縮機能を追加、ドロップオーバーレイの透過を除去
- ドロップオーバーレイの枠色修正、テーマ選択のコード改善
- Actipro → Avalonia FluentTheme移行、macOS Tahoe Liquid Glass風UIデザイン適用

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/58785eba76e1573baef51e19d905822cbb8eea60) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/48675b39e08c0764229ff010d4fa83001c295c87...58785eba76e1573baef51e19d905822cbb8eea60)。

## v1.0.90 — 2026-03-07

- コード品質・パフォーマンス改善

## [1.0.86] — Git 記録日: 2026-02-18

- 更新確認ボタン設置

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/62ba7fbef441f78fef4b42c8c5ae8c3ad06df6a1) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/607a791a3e678f1c01b6e1d8e027ca6d5255a3f9...62ba7fbef441f78fef4b42c8c5ae8c3ad06df6a1)。

## [1.0.84] — Git 記録日: 2026-02-18

- 上書きが正常にできていない不具合の修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/607a791a3e678f1c01b6e1d8e027ca6d5255a3f9) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/cdab3d2bf2e4b45da36b7e469544914960eaeb8d...607a791a3e678f1c01b6e1d8e027ca6d5255a3f9)。

## [1.0.82] — Git 記録日: 2026-02-15

- ドットを含むフォルダーを圧縮した際の書庫名を修正し、コマンドラインから開始した処理の完了を正しく待機。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/cdab3d2bf2e4b45da36b7e469544914960eaeb8d) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/f45702778d57567e53d9a146268418551b331137...cdab3d2bf2e4b45da36b7e469544914960eaeb8d)。

## v1.0.80 — 2026-02-18

- 上書き確認ダイアログの不具合修正
- 更新確認ボタンの追加

## [1.0.76] — Git 記録日: 2026-02-10

- AOT対応

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/63ab834e4cd79e626dc66fe5d64b618084244b70) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/1cc2f807f6d6a2dca191f8fa7655204d8559e526...63ab834e4cd79e626dc66fe5d64b618084244b70)。

## [1.0.74] — Git 記録日: 2026-02-07

- .tzファイル未認識・並列圧縮の進捗計算・設定読み込みのバグを修正

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/1cc2f807f6d6a2dca191f8fa7655204d8559e526) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/7e0d8530f260c66634f14aa6ab0b4891c3b2b9a7...1cc2f807f6d6a2dca191f8fa7655204d8559e526)。

## [1.0.72] — Git 記録日: 2026-02-06

- パフォーマンス改善

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/7e0d8530f260c66634f14aa6ab0b4891c3b2b9a7) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/55b951138ce90268b425cc408e03c9a5e5d83167...7e0d8530f260c66634f14aa6ab0b4891c3b2b9a7)。

## v1.0.70 — 2026-02-10

- ネイティブ AOT ビルド対応
- アプリ更新プロセスの改善

## [1.0.68] — Git 記録日: 2026-02-04

- README.md更新

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/7ecbfe112e5e4ee2435c3bc00c73d22b4db3de3a) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/120468eb03e76c2ba91b82ebf256f4ee89b3029f...7ecbfe112e5e4ee2435c3bc00c73d22b4db3de3a)。

## [1.0.66] — Git 記録日: 2026-02-04

- アプリ更新タイミング調整

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/120468eb03e76c2ba91b82ebf256f4ee89b3029f) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/d149140367d8ded082ca2d9fc1316d92c25510a9...120468eb03e76c2ba91b82ebf256f4ee89b3029f)。

## [1.0.64] — Git 記録日: 2026-02-03

- Aot対応延期

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/d149140367d8ded082ca2d9fc1316d92c25510a9) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/ff3f4cb335927029bfad27b989e1bc9b5cf0f127...d149140367d8ded082ca2d9fc1316d92c25510a9)。

## [1.0.62] — Git 記録日: 2026-02-03

- Aot対応
- ファイル整理

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/ff3f4cb335927029bfad27b989e1bc9b5cf0f127) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/15dcdc7f2229f7e5b265d669f8bb494b1f52f112...ff3f4cb335927029bfad27b989e1bc9b5cf0f127)。

## v1.0.60 — 2026-02-06

- パフォーマンス改善
- `.tz` ファイル未認識、並列圧縮の進捗計算、設定読み込みのバグ修正

## [1.0.58] — Git 記録日: 2026-02-02

- System.Text.Jsonへ移行

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/437c526cb1040f1169861713b1f0a503f08ec911) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/cc66aa1749639195c9c5f96f8cf249d7cefea622...437c526cb1040f1169861713b1f0a503f08ec911)。

## [1.0.56] — Git 記録日: 2026-02-02

- ドキュメント整理
- 展開仕様の見直し
- 不要なフォールバック削除

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/cc66aa1749639195c9c5f96f8cf249d7cefea622) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/b036624b1ba40a2a109010dc777225d38ec0b97c...cc66aa1749639195c9c5f96f8cf249d7cefea622)。

## [1.0.52] — Git 記録日: 2026-02-01

- ドキュメント整理
- 二重フォルダ回避ロジック修正(リフトアップ処理修正)
- パスを修正
- 処理タイミング統一
- 不具合修正とMVVM化
- Avalonia化

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/b036624b1ba40a2a109010dc777225d38ec0b97c) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/6d9f9db8c1fc6eefe4a29f51aa5a1b51850f7a57...b036624b1ba40a2a109010dc777225d38ec0b97c)。

## v1.0.50 — 2026-02-03

- UI 改善
- README 整備

## [1.0.44] — Git 記録日: 2026-01-25

- ライブラリ差し替え

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/ff41de54312beba43810550b5ae4806267a1379b) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/48c364d0098759e07593430038bc2c92462b7371...ff41de54312beba43810550b5ae4806267a1379b)。

## [1.0.42] — Git 記録日: 2026-01-24

- 配布用のバージョン情報を更新。記録された差分は版番号の変更のみ。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/48c364d0098759e07593430038bc2c92462b7371) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/dd30dc5eb5d4fc0272466a62ffd31b2a7d40f5c7...48c364d0098759e07593430038bc2c92462b7371)。

## [1.0.40] — 公開記録日: 2026-01-23

- GitHub に公開記録がありますが、リリース本文がなく、手元の Git 履歴でも対応する版の変更内容を特定できませんでした。

出典: [公開記録](https://github.com/1llum1n4t1s/Lhamiel/releases/tag/untagged-d78a9872811492e98b1f)。

## [1.0.38] — Git 記録日: 2026-01-22

- 圧縮時の出力ファイル名と進捗の UI スレッドへの通知を修正し、進捗表示エラーをログへ記録。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/dd30dc5eb5d4fc0272466a62ffd31b2a7d40f5c7) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/cfa94961a9e1b3055086fd140418f55697a27616...dd30dc5eb5d4fc0272466a62ffd31b2a7d40f5c7)。

## [1.0.36] — Git 記録日: 2026-01-21

- 圧縮進捗が 0% のままになる問題、ドットで始まるフォルダーの書庫名、既定の圧縮形式の保存・復元を修正。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/cfa94961a9e1b3055086fd140418f55697a27616) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/a52d515253c9522d3828f0fc8619684fd28bed5a...cfa94961a9e1b3055086fd140418f55697a27616)。

## [1.0.34] — Git 記録日: 2026-01-20

- 設定とログの保存先をアプリの実行フォルダーからユーザーデータ領域へ移し、旧ファイルの移行処理を追加。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/a52d515253c9522d3828f0fc8619684fd28bed5a) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/3c112e3bbf3579b9e335f312680652f98a4cf9b7...a52d515253c9522d3828f0fc8619684fd28bed5a)。

## [1.0.32] — Git 記録日: 2026-01-19

- 圧縮・展開のキャンセル後に破棄済みオブジェクトへアクセスする問題と、進捗ウィンドウを重複して閉じる処理を改善。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/3c112e3bbf3579b9e335f312680652f98a4cf9b7) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/42c48fba52301aa15d852b15f56739486841f6b7...3c112e3bbf3579b9e335f312680652f98a4cf9b7)。

## [1.0.30] — Git 記録日: 2026-01-19

- ZIP の既定の圧縮レベルを Fast から Normal へ変更。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/42c48fba52301aa15d852b15f56739486841f6b7) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/038064a515eef203e198459dde5f9581763b0aac...42c48fba52301aa15d852b15f56739486841f6b7)。

## [1.0.28] — Git 記録日: 2026-01-19

- 開発支援用の設定を更新。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/038064a515eef203e198459dde5f9581763b0aac) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/267a73a4df822fb9abc84c2d1c876ff9fc0d26fb...038064a515eef203e198459dde5f9581763b0aac)。

## [1.0.25] — Git 記録日: 2026-01-19

- 更新確認をバックグラウンドで行い、実行中の圧縮・展開が完了してから更新を適用するよう変更。
- 自己展開 EXE の判定範囲を広げ、通常の EXE は圧縮、自己展開形式は展開へ振り分け。手動更新確認ボタンを削除。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/267a73a4df822fb9abc84c2d1c876ff9fc0d26fb) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/633eee0083e3404308bce193bf1d27cf0b4f53f1...267a73a4df822fb9abc84c2d1c876ff9fc0d26fb)。

## [1.0.22] — Git 記録日: 2026-01-18

- 不要な名前空間の参照を整理し、配布用のバージョン情報を更新。機能変更はありません。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/633eee0083e3404308bce193bf1d27cf0b4f53f1) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/db943ac470b6bf4d9379e05a65d551a160c27e3f...633eee0083e3404308bce193bf1d27cf0b4f53f1)。

## [1.0.21] — Git 記録日: 2026-01-18

- 進捗ウィンドウが自分自身を所有ウィンドウに設定しないよう修正。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/db943ac470b6bf4d9379e05a65d551a160c27e3f) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/55a7d0fb202e6f6edfda026155162eb3e01a1e2a...db943ac470b6bf4d9379e05a65d551a160c27e3f)。

## [1.0.20] — Git 記録日: 2026-01-18

- LHA 圧縮時のクラッシュと複数項目の進捗計算を修正し、進捗画面の操作名・状態表示を整理。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/55a7d0fb202e6f6edfda026155162eb3e01a1e2a) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/828280691666d2e6b6b72dbdac973c5e9683068d...55a7d0fb202e6f6edfda026155162eb3e01a1e2a)。

## [1.0.18] — Git 記録日: 2026-01-16

- 展開後フォルダの表示と進捗表示を改善

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/828280691666d2e6b6b72dbdac973c5e9683068d) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/d4160b7ad938e6d94b0f0777b8b3aaf17ac7d95a...828280691666d2e6b6b72dbdac973c5e9683068d)。

## [1.0.17] — Git 記録日: 2026-01-14

- 手動の更新確認ボタンと、前回の更新確認時刻を保存する仕組みを追加。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/d4160b7ad938e6d94b0f0777b8b3aaf17ac7d95a) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/9c3b759ed8117019b834d7426ea61d4708076242...d4160b7ad938e6d94b0f0777b8b3aaf17ac7d95a)。

## [1.0.16] — Git 記録日: 2026-01-14

- LHA 圧縮の表示名と出力拡張子を LZH・.lzh に変更し、対応形式の判定を更新。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/9c3b759ed8117019b834d7426ea61d4708076242) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/37f88352ffeea4031c50e4392875b0f190b30ff9...9c3b759ed8117019b834d7426ea61d4708076242)。

## [1.0.15] — Git 記録日: 2026-01-14

- Implement LHA compression feature and add comprehensive tests

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/37f88352ffeea4031c50e4392875b0f190b30ff9) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/eb827618cac79c2116a81ef1b22c301f4ed3ab33...37f88352ffeea4031c50e4392875b0f190b30ff9)。

## [1.0.12] — Git 記録日: 2026-01-14

- 書庫直下が単一ファイル・単一フォルダー・複数項目の場合を区別し、展開先フォルダーの作成と二重フォルダーの解消を修正。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/eb827618cac79c2116a81ef1b22c301f4ed3ab33) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/acd131db84db790ccb54c96039884683a96061ea...eb827618cac79c2116a81ef1b22c301f4ed3ab33)。

## [1.0.11] — Git 記録日: 2026-01-14

- 書庫直下に複数の項目がある場合だけ書庫名のフォルダーを作成するよう、展開先の判定を変更。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/acd131db84db790ccb54c96039884683a96061ea) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/68495834ce5f737ccf2f242c0a8af2d95e32ade4...acd131db84db790ccb54c96039884683a96061ea)。

## [1.0.10] — Git 記録日: 2026-01-14

- 関連付け対象の拡張子を小文字に揃え、設定画面での照合を修正。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/68495834ce5f737ccf2f242c0a8af2d95e32ade4) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/3fd4052ebd0521e5e52854caf4f739f2ea968e1b...68495834ce5f737ccf2f242c0a8af2d95e32ade4)。

## [1.0.9] — Git 記録日: 2026-01-14

- 複数書庫の圧縮・展開を CPU コア数で制限した並列処理に対応させ、キャンセル処理を追加。
- 展開時の二重フォルダー判定と、圧縮形式ごとのオプション設定を整理。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/3fd4052ebd0521e5e52854caf4f739f2ea968e1b) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/3c457c7437d72dee204b6e63766b779c073a61ca...3fd4052ebd0521e5e52854caf4f739f2ea968e1b)。

## [1.0.6] — Git 記録日: 2026-01-14

- AssemblyInfo.cs をコンパイル対象に明示的に追加

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/3c457c7437d72dee204b6e63766b779c073a61ca) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/c4c0ce2532b60299277d42adb9bd3959a1372e66...3c457c7437d72dee204b6e63766b779c073a61ca)。

## [1.0.5] — Git 記録日: 2026-01-14

- UTF-8エンコーディング機能を復元: CompressionOptionを使用
- ビルドエラーを修正: using宣言の追加とArchiveWriterのAPI変更対応
- 圧縮処理の改善: UTF-8エンコーディングとファイル除外機能を追加
- 複数フォルダのドロップ&並行圧縮処理に対応
- Fix file association for tar archive extensions
- Migrate to Log4net for logging

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/058c2c12a5b81fd73a80bfdd01ffd3818b18643c) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/900cdf7f39809fcffdbaf29c93a894745587cdf9...058c2c12a5b81fd73a80bfdd01ffd3818b18643c)。

## [1.0.1] — Git 記録日: 2026-01-13

- 圧縮・展開後に出力フォルダーを開く機能と二重フォルダーを防ぐ処理を追加し、再公開時の配布処理を改善。

出典: [版の記録](https://github.com/1llum1n4t1s/Lhamiel/commit/900cdf7f39809fcffdbaf29c93a894745587cdf9) / [変更差分](https://github.com/1llum1n4t1s/Lhamiel/compare/cee837adaecbd91ecfd35979c8ffd1f8e294518a...900cdf7f39809fcffdbaf29c93a894745587cdf9)。

## v1.0.0 — 2026-02-02

- 初回リリース
- ドラッグ＆ドロップによる圧縮・展開
- スマート展開（二重フォルダ防止）
- エラー回復機能
- ファイル関連付け
- 自動更新（Velopack）
