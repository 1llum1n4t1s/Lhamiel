using Avalonia.Controls;
using Avalonia.Interactivity;
using Kagayoi.Support.Client;
using Lhamiel.Util;

namespace Lhamiel.View;

/// <summary>メール認証を挟んでKagayoi Supportへ問い合わせを登録するダイアログ。</summary>
public partial class SupportDialog : Window
{
    internal const string ProductId = "lhamiel";
    internal const double CompactMinHeight = 480;
    internal const double CompactHeight = 500;
    internal const double VerificationMinHeight = 550;
    internal const double VerificationHeight = 570;

    private static readonly SupportApiClient _client = new(
        new SupportClientOptions
        {
            ProductId = ProductId,
            Channel = "desktop",
            ErrorSink = (operation, exception) =>
                Logger.Log($"サポートAPI ({operation}) に失敗: {exception.Message}", LogLevel.Debug),
        });

    private readonly CancellationTokenSource _cancellation = new();
    private readonly SupportSubmissionSession _session = new(_client);
    private readonly string _locale;
    private bool _isCodeRequested;
    private bool _isSubmitted;

    public SupportDialog() : this(App.DetectDefaultLocale()) { }

    public SupportDialog(string locale)
    {
        _locale = string.IsNullOrWhiteSpace(locale) ? App.DetectDefaultLocale() : locale;
        InitializeComponent();
        MinHeight = CompactMinHeight;
        Height = CompactHeight;
        Title = App.Text("Support.Title");
        // アクリル代替背景 → 共通枠 → アイコンの順で初期化する（順序は DialogChrome.InitializeDialog が固定）。
        DialogChrome.InitializeDialog(this);
        Opened += (_, _) => NameBox.Focus();
        Closed += (_, _) => _cancellation.Cancel();
    }

    private async void SendButton_Click(object? sender, RoutedEventArgs e)
    {
        if (CategoryBox.SelectedItem is not ComboBoxItem { Tag: string category })
        {
            SetMessage("Support.InvalidFields");
            return;
        }

        var ticketInput = CreateTicketInput(category);
        var validation = SupportInputRules.Validate(EmailBox.Text, ticketInput);
        if (validation != SupportInputValidation.Valid)
        {
            SetMessage(validation == SupportInputValidation.InvalidEmail
                ? "Support.InvalidEmail"
                : "Support.InvalidFields");
            return;
        }

        try
        {
            SetBusy(true);
            if (!_isCodeRequested)
            {
                await RequestCodeAsync();
                return;
            }

            var code = CodeBox.Text?.Trim() ?? string.Empty;
            if (code.Length != 6 || !code.All(char.IsAsciiDigit))
            {
                SetMessage("Support.InvalidCode");
                return;
            }

            SetMessage("Support.Sending");
            var submitted = await _session.VerifyAndSubmitAsync(
                code,
                CreateTicketInput(category),
                _cancellation.Token);
            if (submitted.Status == SupportSubmissionStatus.Submitted
                && submitted.Reference is { Length: > 0 } reference)
            {
                _isSubmitted = true;
                SetMessage("Support.Success", reference);
                NameBox.IsEnabled = false;
                EmailBox.IsEnabled = false;
                CategoryBox.IsEnabled = false;
                ContentBox.IsEnabled = false;
                CodeBox.IsEnabled = false;
                SetVerificationPanelVisible(false);
                SendButton.IsVisible = false;
                CancelButton.Content = App.Text("Button.OK");
                return;
            }

            SetMessage(submitted.Status switch
            {
                SupportSubmissionStatus.InvalidCode => "Support.InvalidCode",
                SupportSubmissionStatus.InvalidRequest => "Support.InvalidFields",
                _ => "Support.ServerUnreachable",
            });
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // ダイアログを閉じた後はUIを更新しない。
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ResendButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!SupportInputRules.IsValidEmail(EmailBox.Text))
        {
            SetMessage("Support.InvalidEmail");
            return;
        }

        try
        {
            SetBusy(true);
            await RequestCodeAsync();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // ダイアログを閉じた後はUIを更新しない。
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ChangeEmailButton_Click(object? sender, RoutedEventArgs e)
    {
        _isCodeRequested = false;
        EmailBox.IsEnabled = true;
        CodeBox.Text = string.Empty;
        SetVerificationPanelVisible(false);
        SendButton.Content = App.Text("Support.Send");
        MessageText.Text = string.Empty;
        EmailBox.Focus();
    }

    private async Task RequestCodeAsync()
    {
        SetMessage("Support.Sending");
        var requested = await _session.RequestCodeAsync(
            EmailBox.Text!,
            _cancellation.Token);
        switch (requested)
        {
            case SupportRequestCodeStatus.Sent:
                _isCodeRequested = true;
                EmailBox.IsEnabled = false;
                CodeBox.Text = string.Empty;
                SetVerificationPanelVisible(true);
                SendButton.Content = App.Text("Support.VerifyAndSend");
                SetMessage("Support.CodeSent");
                CodeBox.Focus();
                break;
            case SupportRequestCodeStatus.InvalidEmail:
                SetMessage("Support.InvalidEmail");
                break;
            case SupportRequestCodeStatus.TooSoon:
                SetMessage("Support.TooSoon");
                break;
            default:
                SetMessage("Support.ServerUnreachable");
                break;
        }
    }

    private async void StatusButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await ShellOpener.OpenWithDefaultHandlerAsync(_client.PortalUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            Logger.LogException("お問い合わせ状況確認ページを開けませんでした", ex);
            SetMessage("Support.ServerUnreachable");
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy)
    {
        SendButton.IsEnabled = !busy && !_isSubmitted;
        ResendButton.IsEnabled = !busy && !_isSubmitted;
        ChangeEmailButton.IsEnabled = !busy && !_isSubmitted;
        CancelButton.IsEnabled = !busy;
        EmailBox.IsEnabled = !busy && !_isCodeRequested && !_isSubmitted;
        CodeBox.IsEnabled = !busy && !_isSubmitted;
    }

    private void SetVerificationPanelVisible(bool visible)
    {
        CodePanel.IsVisible = visible;
        MinHeight = visible ? VerificationMinHeight : CompactMinHeight;
        if (visible && Height < VerificationHeight)
            Height = VerificationHeight;
        else if (!visible && Math.Abs(Height - VerificationHeight) < 0.5)
            Height = CompactHeight;
    }

    private void SetMessage(string textKey, params object[] args)
        => MessageText.Text = args.Length == 0
            ? App.Text(textKey)
            : App.Text(textKey, args);

    private SupportTicketInput CreateTicketInput(string category)
        => new()
        {
            Name = NameBox.Text,
            Category = category,
            Content = ContentBox.Text ?? string.Empty,
            AppVersion = typeof(SupportDialog).Assembly.GetName().Version?.ToString(3),
            OsVersion = Environment.OSVersion.VersionString,
            Locale = _locale,
        };
}
