using System.Windows;
using System.IO;
using System.Windows.Threading;

namespace GGEZArchiver.View;

/// <summary>
/// 処理進行状況を表示する汎用ウィンドウ
/// 圧縮・展開処理時の進行状況をユーザーに視覚的に提供
/// 処理タイプ（"圧縮"または"展開"）を指定して使用
/// </summary>
public partial class ProgressWindow : Window
{
    /// <summary>
    /// 処理タイプ（圧縮または展開）
    /// </summary>
    private readonly string _processType;

    /// <summary>
    /// 進行状況ウィンドウのコンストラクタ
    /// ウィンドウの初期化とタイマーの設定を行う
    /// </summary>
    /// <param name="processType">処理タイプ（"圧縮"または"展開"）</param>
    public ProgressWindow(string processType = "処理")
    {
        InitializeComponent();
        _processType = processType;
        SetupTimer();
    }

    /// <summary>
    /// 進行状況表示用のタイマーを設定する
    /// アニメーション効果を提供するためのタイマーを初期化
    /// </summary>
    private void SetupTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += Timer_Tick;
        timer.Start();
    }

    /// <summary>
    /// タイマーのティックイベントハンドラー
    /// 進行状況バーのアニメーション効果を制御
    /// </summary>
    /// <param name="sender">イベントの送信元オブジェクト</param>
    /// <param name="e">タイマーイベント引数</param>
    private void Timer_Tick(object? sender, EventArgs e)
    {
        // 進行状況バーのアニメーション効果
        if (ProgressBar.Value < ProgressBar.Maximum)
        {
            ProgressBar.Value += 1;
        }
    }

    /// <summary>
    /// 処理対象のファイル名を設定する
    /// ウィンドウタイトルとファイル名表示を更新
    /// </summary>
    /// <param name="fileName">処理対象のファイル名</param>
    public void SetFileName(string fileName)
    {
        Title = $"{_processType}中: {System.IO.Path.GetFileName(fileName)}";
        FileNameTextBlock.Text = System.IO.Path.GetFileName(fileName);
    }

    /// <summary>
    /// 進行状況を更新する
    /// 進行状況バーとステータステキストを更新
    /// </summary>
    /// <param name="percentage">進行状況のパーセンテージ（0-100）</param>
    /// <param name="status">現在の処理状況を表すテキスト</param>
    public void UpdateProgress(int percentage, string status)
    {
        ProgressBar.Value = percentage;
        StatusTextBlock.Text = status;
        ProgressTextBlock.Text = $"{percentage}%";
    }

    /// <summary>
    /// 処理完了時の表示を設定する
    /// 完了メッセージを表示し、タイマーを停止
    /// </summary>
    /// <param name="message">完了時に表示するメッセージ</param>
    public void SetCompleted(string message)
    {
        StatusTextBlock.Text = message;
        ProgressBar.Value = 100;
        ProgressTextBlock.Text = "100%";
        Title = $"{_processType}完了";
    }
} 