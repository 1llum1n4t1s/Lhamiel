# Lhamiel

Lhamiel のインストーラーは以下からダウンロードできます。

- インストーラー（Windows）: https://github.com/1llum1n4t1s/Lhamiel/releases
  - Releases の Assets から `Setup.exe` をダウンロードしてください。

## インストール手順

1. 上記リンクから `Setup.exe` をダウンロードします。
2. ダウンロードした `Setup.exe` を実行してインストールします。

インストール時に以下が自動的に実行されます：
- ✅ Windows のアプリケーション一覧に「Lhamiel」として登録
- ✅ スタートメニューに「Lhamiel」というショートカットを作成
- ✅ デスクトップに「Lhamiel」というショートカットを作成
- ✅ 関連付けアイコンの設定（`file.ico`）

## アンインストール手順

1. Windows の設定から「アプリ」→「インストール済みアプリ」を開く
2. 「Lhamiel」を検索して選択
3. 「アンインストール」をクリック
4. 確認画面で「アンインストール」を再度クリック

または、コントロールパネルの「プログラムと機能」から Lhamiel をアンインストールできます。

アンインストール時に以下が自動的に削除されます：
- ✅ インストール済みファイル
- ✅ ファイル関連付け
- ✅ 作成されたショートカット
- ✅ アプリケーション一覧からのエントリ

## 概要

Lhamiel は、Windows 向けのアーカイブ圧縮・展開ツールです。圧縮形式の選択、出力先の指定、ファイル関連付けの管理、デスクトップショートカットの作成などを GUI から行えます。

## システム要件

- **OS**: Windows 10 (ビルド 26100.0) 以上
- **フレームワーク**: .NET 10.0 Runtime (インストーラーに含まれます)
- **アーキテクチャ**: x64
- **必要な権限**: ユーザー権限（管理者権限不要）

## 主な機能

- ファイル/フォルダの圧縮とアーカイブの展開に対応
- 圧縮/展開それぞれの出力先ディレクトリを指定、または元のファイルと同じ場所に出力
- 圧縮形式の選択（設定保存で次回起動時も維持）
- 対応拡張子のファイル関連付けを一括選択・解除
- デスクトップショートカットの作成
- 進捗表示とキャンセル操作に対応

## 対応形式

### 圧縮対応

- 7z
- xz
- bz2
- gz
- tar
- zip
- wim
- cab

### 展開対応

- zip
- 7z
- tar
- gz
- bz2
- lzma
- xz
- rar
- lzh
- cab
- arj
- z
- 自己解凍形式（.exe）

## 設定ファイル

設定はアプリの実行ディレクトリに `settings.json` として保存されます。圧縮形式、出力先ディレクトリ、出力先パターン（同じディレクトリに出力するかどうか）などの情報が保持されます。

詳細は [SETTINGS_SCHEMA.md](SETTINGS_SCHEMA.md) を参照してください。

## ファイル関連付け

対応拡張子（zip/7z/tar/gz/bz2/lzma/xz/rar/lzh/cab/arj/z）の関連付けを GUI から設定できます。全選択・全解除の操作も可能です。

## ショートカット

「デスクトップにショートカット作成」ボタンから、アプリのデスクトップショートカットを作成できます。

## 更新について

更新配信の詳細は `docs/Velopack.md` を参照してください。

## 開発者向け情報

### ドキュメント

- [アーキテクチャドキュメント](ARCHITECTURE.md) - システム設計と実装の詳細
- [設定スキーマ](SETTINGS_SCHEMA.md) - settings.json の詳細仕様

### ビルド方法

```bash
# 依存関係の復元
dotnet restore Lhamiel.slnx

# ビルド
dotnet build Lhamiel.slnx --configuration Release

# テスト実行
dotnet test Lhamiel.slnx --configuration Release
```

### 技術スタック

- .NET 10.0 (Windows)
- WPF (Windows Presentation Foundation)
- Cube.FileSystem.SevenZip (圧縮ライブラリ)
- Velopack (自動更新)

### 最近の改善点

- ✅ 本番環境対応のログレベル実装
- ✅ コード重複の解消（IsSelfExtractingArchive）
- ✅ マジックナンバーの定数化
- ✅ パス検証ユーティリティの実装
- ✅ 動的型（dynamic）の削除と型安全性の向上
- ✅ CI/CDパイプラインの改善（テスト・品質チェック追加）
- ✅ ユニットテストプロジェクトの追加
- ✅ 包括的なドキュメント整備

## 作者・連絡先

- 作者: ゆろち
- 連絡先: https://github.com/1llum1n4t1s
