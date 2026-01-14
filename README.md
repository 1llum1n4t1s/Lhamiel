# Lhamiel

**シンプル＆高速なアーカイブ圧縮・展開ツール**

Lhamielは、Windows向けの使いやすいアーカイブ管理ツールです。ファイルやフォルダの圧縮、複数のアーカイブ形式への対応、直感的なドラッグ&ドロップUIで、快適なファイル操作を実現します。

## 🚀 主な特徴

- **シンプルな操作**: ドラッグ&ドロップで圧縮・展開
- **豊富な展開形式対応**: ZIP、7z、RAR、tar、gz など多数の形式に対応
- **複数の圧縮形式**: ZIP、7z、TAR、LHA で圧縮
- **ファイル関連付け**: 対応形式のファイルをダブルクリックで自動処理
- **柔軟な出力先設定**: 元のファイルと同じ場所か、指定した場所に出力を選択可能
- **進捗表示**: 処理の進行状況をリアルタイムで確認でき、キャンセルも可能
- **自動更新**: 新しいバージョンが利用可能になると自動更新
- **管理者権限不要**: 一般ユーザー権限で動作

## 📥 ダウンロード & インストール

### インストーラーのダウンロード

以下から最新版の `Setup.exe` をダウンロードしてください：

**[GitHub Releases](https://github.com/1llum1n4t1s/Lhamiel/releases)**

### インストール手順

1. ダウンロードした `Setup.exe` を実行します
2. インストール画面の指示に従います

インストール完了後、以下が自動的に設定されます：
- スタートメニューにショートカットを作成
- デスクトップにショートカットを作成  
- Windows のアプリケーション一覧に登録

## ⚙️ システム要件

- **OS**: Windows 8 以上
- **アーキテクチャ**: x64
- **必要な権限**: ユーザー権限（管理者権限不要）
- **.NET Runtime**: .NET 10.0 以上がインストール必要

## 📖 使い方

### 圧縮する

#### 方法1: メイン画面から圧縮

1. アプリケーションを起動して「圧縮」タブを開きます
2. 「ファイルを選択」をクリックして、圧縮したいファイルやフォルダを選択します
3. 圧縮形式を選択します（ZIP、7z、TAR、LHA から選択）
4. 出力先を設定します：
   - **元のファイルと同じ場所**: ファイルやフォルダのある場所に圧縮ファイルを作成
   - **指定した場所**: 別のフォルダを指定して圧縮ファイルを作成
5. 「圧縮」ボタンをクリックします

#### 方法2: ドラッグ&ドロップで圧縮

1. アプリケーションを起動します
2. ファイルやフォルダをアプリケーションのドロップゾーンにドラッグ&ドロップします
3. 自動的に圧縮が開始されます

#### 方法3: ファイル関連付けで圧縮

1. 設定タブで対応形式をチェック（ファイル関連付けを有効化）
2. エクスプローラーで任意のファイルを右クリック
3. 「プログラムから開く」→「Lhamiel」を選択して圧縮

### 展開する

#### 方法1: メイン画面から展開

1. アプリケーションを起動して「展開」タブを開きます
2. 「ファイルを選択」をクリックして、展開したいアーカイブファイルを選択します
3. 出力先を設定します：
   - **元のファイルと同じ場所**: アーカイブのある場所にファイルを展開
   - **指定した場所**: 別のフォルダを指定してファイルを展開
4. 「展開」ボタンをクリックします

#### 方法2: ドラッグ&ドロップで展開

1. アプリケーションを起動します
2. アーカイブファイルをアプリケーションのドロップゾーンにドラッグ&ドロップします
3. 自動的に展開が開始されます

#### 方法3: ダブルクリックで展開

1. 設定タブで対応形式をチェック（ファイル関連付けを有効化）
2. エクスプローラーでアーカイブファイルをダブルクリック
3. 自動的に展開されます

### ファイル関連付けの設定

1. アプリケーションを起動して「設定」タブを開きます
2. 対応させたい拡張子にチェックを入れます
   - 「全選択」: すべての形式に関連付け
   - 「全解除」: すべての関連付けを削除
3. 「設定を保存」をクリック

完了後、対応形式のファイルをダブルクリックすることで Lhamiel で自動的に処理できるようになります。

## 📦 対応形式

### 圧縮対応形式

以下の形式で新しいアーカイブを作成できます：

- ZIP
- 7z（セブンジップ）
- TAR
- LZH

### 展開対応形式

以下の形式のアーカイブを展開できます：

- ZIP
- 7z（セブンジップ）
- TAR
- GZIP（.gz）
- BZIP2（.bz2）
- LZMA（.lzma）
- XZ（.xz）
- RAR
- LZH
- CAB
- ARJ
- Z

## ⚙️ 詳細設定

### 出力先フォルダの指定

- **圧縮の出力先**: 圧縮後のアーカイブファイルを保存するフォルダを指定
- **展開の出力先**: 展開後のファイルを保存するフォルダを指定

デフォルトでは、元のファイルと同じフォルダに出力されます。

### 処理後の動作設定

設定タブで以下の項目をチェックすることで、処理完了後に自動的に出力フォルダを開くことができます：

- **圧縮後にフォルダを開く**: 圧縮完了後に出力フォルダをエクスプローラーで開く
- **展開後にフォルダを開く**: 展開完了後に出力フォルダをエクスプローラーで開く

### 自動更新機能

アプリケーションは起動時に自動的に新しいバージョンをチェックします。新しいバージョンが利用可能な場合、自動的にダウンロードして更新されます。更新後はアプリケーションが再起動されます。

## 🗑️ アンインストール

### Windows 設定から削除

1. Windows の設定を開きます
2. 「アプリ」→「インストール済みアプリ」から「Lhamiel」を検索
3. 「アンインストール」をクリック
4. 確認画面で「アンインストール」をクリック

### コントロールパネルから削除

1. Windows のコントロールパネルを開きます
2. 「プログラムと機能」から「Lhamiel」を選択
3. 「アンインストール」をクリック

アンインストール後、以下は自動的に削除されます：

- インストール済みファイル
- ファイル関連付け設定
- ショートカット
- アプリケーション一覧のエントリ

## ❓ トラブルシューティング

### アーカイブが展開できない

- ファイルが破損していないか確認してください
- 対応形式のアーカイブか確認してください
- 十分なディスク容量があるか確認してください

### ファイル関連付けが機能しない

- 設定タブで対応形式にチェックが入っているか確認してください
- アプリケーションを再起動してから再度お試しください

### エラーが発生する場合

処理中にエラーが発生した場合は、エラーメッセージを確認してから以下の対応をお試しください：

- ファイル/フォルダのパスに特殊文字が含まれていないか確認
- ディスク容量に余裕があるか確認
- アプリケーションを再起動してお試しください

## 📞 サポート

問題が発生した場合や機能のリクエストがある場合は、以下からお知らせください：

**[GitHub Issues](https://github.com/1llum1n4t1s/Lhamiel/issues)**

またはTwitterで作者（[@1llum1n4t1s](https://twitter.com/1llum1n4t1s)）までご連絡ください。

## 📄 ライセンス

Lhamielは MIT License の下で公開されています。詳細は [LICENSE](LICENSE) ファイルをご参照ください。

```
MIT License

Copyright (c) 2024-2026 Lhamiel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

**Lhamiel** - シンプルで強力なアーカイブツール
