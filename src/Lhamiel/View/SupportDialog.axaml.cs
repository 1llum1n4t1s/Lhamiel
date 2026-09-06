using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
    private TextBox _nameBox = null!;
    private TextBox _emailBox = null!;
    private ComboBox _categoryBox = null!;
    private TextBox _contentBox = null!;
    private TextBox _codeBox = null!;
    private StackPanel _codePanel = null!;
    private TextBlock _messageText = null!;
    private Button _sendButton = null!;
    private Button _resendButton = null!;
    private Button _changeEmailButton = null!;
    private Button _cancelButton = null!;
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
        AcrylicFallbackHelper.Attach(this);
        DialogChrome.Attach(this, "DialogBody", "DialogActions");
        AppIconManager.Apply(this);
        Opened += (_, _) => _nameBox.Focus();
        Closed += (_, _) => _cancellation.Cancel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _nameBox = FindRequired<TextBox>("NameBox");
        _emailBox = FindRequired<TextBox>("EmailBox");
        _categoryBox = FindRequired<ComboBox>("CategoryBox");
        _contentBox = FindRequired<TextBox>("ContentBox");
        _codeBox = FindRequired<TextBox>("CodeBox");
        _codePanel = FindRequired<StackPanel>("CodePanel");
        _messageText = FindRequired<TextBlock>("MessageText");
        _sendButton = FindRequired<Button>("SendButton");
        _resendButton = FindRequired<Button>("ResendButton");
        _changeEmailButton = FindRequired<Button>("ChangeEmailButton");
        _cancelButton = FindRequired<Button>("CancelButton");
    }

    private T FindRequired<T>(string name) where T : Control
        => this.FindControl<T>(name)
           ?? throw new InvalidOperationException($"SupportDialog control '{name}' was not found.");

    private async void SendButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_categoryBox.SelectedItem is not ComboBoxItem { Tag: string category })
        {
            SetMessage("Support.InvalidFields");
            return;
        }

        var ticketInput = CreateTicketInput(category);
        var validation = SupportInputRules.Validate(_emailBox.Text, ticketInput);
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

            var code = _codeBox.Text?.Trim() ?? string.Empty;
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
                _nameBox.IsEnabled = false;
                _emailBox.IsEnabled = false;
                _categoryBox.IsEnabled = false;
                _contentBox.IsEnabled = false;
                _codeBox.IsEnabled = false;
                SetVerificationPanelVisible(false);
                _sendButton.IsVisible = false;
                _cancelButton.Content = App.Text("Button.OK");
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
        if (!SupportInputRules.IsValidEmail(_emailBox.Text))
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
        _emailBox.IsEnabled = true;
        _codeBox.Text = string.Empty;
        SetVerificationPanelVisible(false);
        _sendButton.Content = App.Text("Support.Send");
        _messageText.Text = string.Empty;
        _emailBox.Focus();
    }

    private async Task RequestCodeAsync()
    {
        SetMessage("Support.Sending");
        var requested = await _session.RequestCodeAsync(
            _emailBox.Text!,
            _cancellation.Token);
        switch (requested)
        {
            case SupportRequestCodeStatus.Sent:
                _isCodeRequested = true;
                _emailBox.IsEnabled = false;
                _codeBox.Text = string.Empty;
                SetVerificationPanelVisible(true);
                _sendButton.Content = App.Text("Support.VerifyAndSend");
                SetMessage("Support.CodeSent");
                _codeBox.Focus();
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
        _sendButton.IsEnabled = !busy && !_isSubmitted;
        _resendButton.IsEnabled = !busy && !_isSubmitted;
        _changeEmailButton.IsEnabled = !busy && !_isSubmitted;
        _cancelButton.IsEnabled = !busy;
        _emailBox.IsEnabled = !busy && !_isCodeRequested && !_isSubmitted;
        _codeBox.IsEnabled = !busy && !_isSubmitted;
    }

    private void SetVerificationPanelVisible(bool visible)
    {
        _codePanel.IsVisible = visible;
        MinHeight = visible ? VerificationMinHeight : CompactMinHeight;
        if (visible && Height < VerificationHeight)
            Height = VerificationHeight;
        else if (!visible && Math.Abs(Height - VerificationHeight) < 0.5)
            Height = CompactHeight;
    }

    private void SetMessage(string textKey, params object[] args)
        => _messageText.Text = args.Length == 0
            ? App.Text(textKey)
            : App.Text(textKey, args);

    private SupportTicketInput CreateTicketInput(string category)
        => new()
        {
            Name = _nameBox.Text,
            Category = category,
            Content = _contentBox.Text ?? string.Empty,
            AppVersion = typeof(SupportDialog).Assembly.GetName().Version?.ToString(3),
            OsVersion = Environment.OSVersion.VersionString,
            Locale = _locale,
        };
}
