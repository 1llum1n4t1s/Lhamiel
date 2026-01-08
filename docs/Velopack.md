# Velopack 導入・配布手順

## 目的

- GitHub Releases を更新元として Velopack で自動更新する。
- 初回インストールは Velopack の `Setup.exe` を配布する。
- 更新ファイルは GitHub Releases に一式アップロードする。

## 前提

- リポジトリの GitHub URL を把握していること。
- Velopack CLI (`vpk`) を利用できること。

## 1. CLI の準備

```powershell
# 初回のみ
dotnet tool install -g vpk
# 既に入っている場合
dotnet tool update -g vpk
```

## 2. パッケージ作成 (vpk 生成)

`build/pack-velopack.ps1` で Windows 用のパッケージを生成します。

```powershell
./build/pack-velopack.ps1 -Version 1.0.0
```

- `-Version` は `AssemblyInfo.cs` のバージョンと合わせてください。
出力先は `Releases/` です。

## 3. GitHub Releases へアップロード

`Releases/` に生成されたすべてのファイルを GitHub Releases にアップロードします。
以下は目安のファイル群です（バージョンによって差異あり）。

- `Setup.exe` (初回インストール用)
- `RELEASES`
- `Lhamiel-<version>-full.nupkg`
- `Lhamiel-<version>-delta.nupkg` (ある場合)

手動アップロードでも構いませんが、`vpk upload github` を使う場合は次のように実行できます。

```powershell
vpk upload github --outputDir Releases --repoUrl "https://github.com/<OWNER>/<REPO>" --tag "v1.0.0" --publish
```

### GitHub Actions で自動公開する場合

タグ `v1.0.0` のように `v` から始まるタグをプッシュすると、
`Releases/` 配下の成果物を GitHub Releases に自動アップロードします。

```bash
git tag v1.0.0
git push origin v1.0.0
```

## 4. 配布運用

- 初回インストール: `Setup.exe` を配布。
- 以後の更新: GitHub Releases に `Releases/` の中身を必ずすべてアップロード。
  - `Setup.exe` のみでは更新が機能しません。
  - `RELEASES` と `*.nupkg` が更新配信に必要です。

### ダウンロード確認

GitHub Releases の対象リリースから `Setup.exe` をダウンロードしてテストできます。

## 5. アプリ側設定

`settings.json` に更新元リポジトリ情報を設定してください。

- `updateRepoOwner`: GitHub のオーナー名
- `updateRepoName`: リポジトリ名
- `updateChannel`: `vpk pack` と同じチャンネル名 (既定: `win`)

設定例:

```json
{
  "updateRepoOwner": "YOUR_GITHUB_OWNER",
  "updateRepoName": "YOUR_GITHUB_REPO",
  "updateChannel": "win"
}
```

### 追加確認

- `settings.json` が存在しない場合は初回起動時に自動生成されます。
- `updateRepoOwner` / `updateRepoName` が未設定の場合は更新チェックをスキップします。
