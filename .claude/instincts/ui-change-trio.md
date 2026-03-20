---
id: lhamiel-ui-change-trio
trigger: "when modifying the main window UI or adding new UI features"
confidence: 0.85
domain: ui
source: local-repo-analysis
---

# UI変更は3ファイルセットで行う

## Action
メインウィンドウのUI変更時は、以下の3ファイルを同時に確認・更新する:
1. `View/MainWindow.xaml` — XAML レイアウト
2. `ViewModels/MainWindowViewModel.cs` — データバインディング・ロジック
3. `App.xaml.cs` — 初期化・ロケール・テーマ管理

## Evidence
- この3ファイルの同時変更パターンが複数のコミットで繰り返されている
- MainWindow.xaml (14回), MainWindowViewModel.cs (20回), App.xaml.cs (50回) が高頻度で変更
