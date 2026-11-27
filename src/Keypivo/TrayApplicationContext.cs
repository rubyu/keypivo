using System.Runtime.InteropServices;
using Keypivo.Core;

namespace Keypivo;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SwitchController _controller;
    private readonly SettingsStore _store;
    private AppSettings _settings;
    private readonly StatusForm _form = new();
    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _tray;
    private readonly Action<int, string, ToolTipIcon> _showBalloon;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    private bool _refreshing;
    private TaskCompletionSource? _backgroundRefresh;
    private bool _editingSettings;
    private bool _closing;
    private bool _notificationsEnabled;
    private Exception? _lastError;
    private ManualSetup? _setup;
    private SetupCheckResult _setupResult = new(false, false, null);
    private string? _setupMessage;
    private (ConnectionState State, string? InstanceId, string? BusId)? _lastNotification;

    public TrayApplicationContext(SwitchController controller, SettingsStore store, SettingsLoadResult loaded, bool showWindow,
        Action<int, string, ToolTipIcon>? showBalloon = null)
    {
        _controller = controller;
        _store = store;
        _settings = loaded.Settings;
        _tray = new NotifyIcon { Text = "Keypivo", Icon = _icons.For(ConnectionState.Checking), Visible = true };
        _showBalloon = showBalloon ?? ((timeout, text, icon) => _tray.ShowBalloonTip(timeout, "Keypivo", text, icon));
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right) ShowStatusFromTray();
        };
        _tray.BalloonTipClicked += (_, _) => ShowStatusFromTray();
        _form.SwitchRequested += async () => await RunActionAsync(SwitchAction.Toggle);
        _form.SettingsRequested += async () => await ShowSettingsAsync();
        _form.ExitRequested += () => ExitThread();
        _form.DetailsRequested += ShowDetails;
        _form.SetupBackRequested += () => { CloseSetup(); Render(); ShowStatus(); };
        _form.SetupCopyRequested += async () => await CopySetupCommandAsync();
        _controller.SnapshotChanged += OnSnapshot;
        _controller.VerificationRecovered += OnVerificationRecovered;
        _timer.Tick += async (_, _) => await RefreshAsync();

        _ = _form.Handle;
        Render();
        _form.BeginInvoke((Action)(async () =>
        {
            await RefreshAsync();
            if (_closing) return;
            _notificationsEnabled = true;
            NotifyState(show: false);
            if (loaded.RecoveredFromError)
                Notify(NotificationKind.SettingsCouldNotBeLoaded, 5000, "Settings could not be loaded. Default settings are in use.", ToolTipIcon.Warning);
            else
                NotifyStateMessage(_controller.Current.State, NotificationKind.CurrentStatusAtLaunch);
            if (showWindow) ShowStatus();
            _timer.Start();
        }));
    }

    private void ShowStatusFromTray()
    {
        if (_closing) return;
        // Let the shell finish its tray-click callback before activating the popup.
        _form.BeginInvoke((Action)ShowStatus);
    }

    private void ShowStatus()
    {
        if (!_closing && !_editingSettings) _form.ShowAt(_settings.Position);
    }

    internal async Task RefreshAsync()
    {
        if (_busy || _refreshing || _editingSettings || _closing) return;
        if (_backgroundRefresh is { } pending)
        {
            await pending.Task;
            return;
        }
        // Automatic reads are invisible to the UI. Explicit actions wait for
        // this read instead of being dropped while their buttons look enabled.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _backgroundRefresh = completion;
        try
        {
            if (_closing) return;
            if (_setup is { } setup)
            {
                var result = await _controller.CheckSetupAsync(setup, _lifetime.Token);
                // Back remains usable while a background read is in flight.
                if (ReferenceEquals(_setup, setup))
                {
                    _setupResult = result;
                    _setupMessage = null;
                }
            }
            else await _controller.RefreshAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_closing) { }
        finally
        {
            _backgroundRefresh = null;
            completion.TrySetResult();
            if (!_closing) Render();
        }
    }

    private Task WaitForBackgroundRefreshAsync() => _backgroundRefresh?.Task ?? Task.CompletedTask;

    private async Task RunActionAsync(SwitchAction action)
    {
        if (_busy || _refreshing || _editingSettings || _closing) return;
        _busy = true;
        _lastError = null;
        Render();
        try
        {
            await WaitForBackgroundRefreshAsync();
            if (_closing) return;
            await _controller.ExecuteAsync(action, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (ManualSetupRequiredException exception) { OpenSetup(exception.Setup); }
        catch (Exception exception)
        {
            _lastError = exception;
            if (!_closing)
            {
                var detail = exception is SwitchVerificationTimeoutException
                    ? "Change not confirmed yet. Status checks will continue automatically."
                    : $"The operation could not be completed.\nCurrent state: {StatePresentation.Title(_controller.Current.State)}.";
                Notify(exception is SwitchVerificationTimeoutException ? NotificationKind.ChangeNotConfirmed : NotificationKind.ActionFailed,
                    6000, detail, ToolTipIcon.Warning);
                ShowStatus();
            }
        }
        finally
        {
            _busy = false;
            if (!_closing) { Render(); NotifyState(show: _lastError is null); }
        }
    }

    private void OpenSetup(ManualSetup setup)
    {
        _lastError = null;
        _setup = setup;
        _setupMessage = null;
        _setupResult = setup.Inspect(_controller.Current);
        _form.ShowSetup(setup);
        Render();
        ShowStatus();
    }

    private void CloseSetup()
    {
        _setup = null;
        _setupMessage = null;
        _form.ShowMenu();
    }

    private async Task CopySetupCommandAsync()
    {
        if (_setup is not { } setup || _busy || _refreshing || _editingSettings || _closing) return;
        _refreshing = true;
        _setupMessage = null;
        Render();
        try
        {
            await WaitForBackgroundRefreshAsync();
            if (_closing) return;
            // Never copy a command for a stale bus ID or a replacement device.
            _setupResult = await _controller.CheckSetupAsync(setup, _lifetime.Token);
            if (_closing) return;
            if (_setupResult.CanCopy && !_setupResult.IsComplete)
            {
                Clipboard.SetText(setup.Command);
                _form.MarkCommandCopied();
            }
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (ExternalException) { _setupMessage = "Clipboard is busy. Try copying again."; }
        catch (Exception) { _setupMessage = "Unable to read device status. Retrying automatically."; }
        finally
        {
            _refreshing = false;
            if (!_closing)
            {
                Render();
            }
        }
    }

    private async Task ShowSettingsAsync()
    {
        if (_busy || _refreshing || _editingSettings || _closing) return;
        _editingSettings = true;
        _form.KeepOpen = true;
        DialogResult result;
        UsbDevice? device;
        try
        {
            await WaitForBackgroundRefreshAsync();
            if (_closing) return;
            device = _controller.Current.Device;
            using var dialog = new SettingsForm(_settings,
                device is not null && (device.IsShared || device.IsForced), device?.IsAttached == true, ApplySettingsAsync) { Icon = _tray.Icon };
            result = dialog.ShowDialog(_form);
        }
        finally
        {
            _editingSettings = false;
            _form.KeepOpen = false;
        }
        if (_closing) return;
        Render();
        ShowStatus();
        if (result == DialogResult.Retry && device is not null)
        {
            OpenSetup(new(SetupKind.Unshare, device));
            await RefreshAsync();
        }
    }

    internal async Task ApplySettingsAsync(AppSettings settings)
    {
        settings = settings.Normalize();
        // Saving must succeed before the running configuration changes.
        _store.Save(settings);
        _settings = settings;
        _lastError = null;
        CloseSetup();
        var filter = settings.ToFilter();
        if (filter != _controller.Filter) await _controller.UpdateFilterAsync(filter, _lifetime.Token);
        // Selecting a different key is not a USB connection transition.
        NotifyState(show: false);
        Render();
    }

    private void ShowDetails()
    {
        var detail = _lastError?.Message ?? _controller.Current.Error;
        if (string.IsNullOrWhiteSpace(detail)) return;
        _form.KeepOpen = true;
        try { MessageBox.Show(_form, detail, "Details — Keypivo", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        finally { _form.KeepOpen = false; }
    }

    private void OnSnapshot(DeviceSnapshot snapshot)
    {
        if (_closing) return;
        Render();
        if (!_busy && !_editingSettings && _notificationsEnabled && !_controller.IsConfirmingCurrentState)
            NotifyState(show: true);
    }

    private void OnVerificationRecovered(SwitchVerificationTimeoutException timeout, DeviceSnapshot confirmed)
    {
        if (_closing) return;
        // Only this verification warning can be cleared by a later successful observation.
        if (ReferenceEquals(_lastError, timeout)) _lastError = null;
        // The first matching key may now differ from the key whose switch just completed.
        // Report that operation once, then baseline the current selection to avoid a duplicate balloon.
        NotifyState(show: false);
        if (_notificationsEnabled)
            NotifyStateMessage(confirmed.State);
    }

    private void NotifyState(bool show)
    {
        var snapshot = _controller.Current;
        var identity = (snapshot.State, snapshot.Device?.InstanceId, snapshot.Device?.BusId);
        if (_lastNotification == identity) return;
        _lastNotification = identity;
        if (!show) return;
        NotifyStateMessage(snapshot.State);
    }

    private void NotifyStateMessage(ConnectionState state, NotificationKind? kind = null)
    {
        if ((kind ?? NotificationSettings.ForState(state)) is not { } notification) return;
        var icon = state is ConnectionState.Error or ConnectionState.ToolMissing ? ToolTipIcon.Warning : ToolTipIcon.Info;
        Notify(notification, 4000, $"Security key · {StatePresentation.Title(state)}", icon);
    }

    private void Notify(NotificationKind kind, int timeout, string message, ToolTipIcon icon)
    {
        if (_settings.Notifications.Allows(kind)) _showBalloon(timeout, message, icon);
    }

    private void Render()
    {
        var snapshot = _controller.Current;
        var icon = _icons.For(_busy ? ConnectionState.Checking : snapshot.State);
        if (!ReferenceEquals(_tray.Icon, icon)) _tray.Icon = icon;
        var trayText = $"Keypivo — {(_busy ? "Switching…" : StatePresentation.Title(snapshot.State))}";
        if (_tray.Text != trayText) _tray.Text = trayText;
        _form.Render(snapshot, _busy, _refreshing, _lastError?.Message, _tray.Icon,
            unconfirmed: _lastError is SwitchVerificationTimeoutException);
        if (_setup is not null)
        {
            _form.RenderSetup(_setupResult, _setupMessage, _busy || _refreshing);
        }
    }

    protected override void ExitThreadCore()
    {
        _closing = true;
        _timer.Stop();
        _lifetime.Cancel();
        _tray.Visible = false;
        _form.AllowClose = true;
        _form.Close();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing = true;
            _lifetime.Cancel();
            _controller.SnapshotChanged -= OnSnapshot;
            _controller.VerificationRecovered -= OnVerificationRecovered;
            _timer.Dispose();
            _tray.Dispose();
            _form.Dispose();
            _icons.Dispose();
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
