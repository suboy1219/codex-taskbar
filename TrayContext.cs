using System.Windows.Forms;

namespace CodexTaskbar;

internal sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly UsageService _service = new();
    private readonly UsagePanel _panel;
    private readonly UsageStrip _strip;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _stripMenuItem;
    private Icon? _currentIcon;
    private UsageSnapshot? _lastSnapshot;
    private bool _busy;

    public TrayContext(bool silent)
    {
        _settings = AppSettings.Load();

        _panel = new UsagePanel();
        _panel.RefreshRequested += async (_, _) => await RefreshAsync(showPanelAfter: false);

        _strip = new UsageStrip(_settings);
        _strip.Clicked += (_, _) => ShowPanel();

        _stripMenuItem = new ToolStripMenuItem("显示常驻状态条")
        {
            CheckOnClick = true,
            Checked = _settings.ShowStrip,
        };
        _stripMenuItem.CheckedChanged += (_, _) =>
        {
            _settings.ShowStrip = _stripMenuItem.Checked;
            _settings.Save();
            if (_stripMenuItem.Checked)
            {
                _strip.Show();
            }
            else
            {
                _strip.Hide();
            }
        };
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Codex 用量",
            ContextMenuStrip = BuildMenu(),
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                TogglePanel();
            }
        };

        UpdateIcon(null, error: false);

        if (_settings.ShowStrip)
        {
            _strip.Show();
        }

        _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _timer.Tick += async (_, _) => await RefreshAsync(showPanelAfter: false);
        _timer.Start();

        _ = RefreshAsync(showPanelAfter: false);

        if (!silent)
        {
            ShowPanel();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示用量面板", null, (_, _) => ShowPanel());
        menu.Items.Add("立即刷新", null, async (_, _) => await RefreshAsync(showPanelAfter: false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_stripMenuItem);
        menu.Items.Add(new ToolStripMenuItem("重置状态条位置", null, (_, _) => _strip.ResetPosition()));

        var autoStartItem = new ToolStripMenuItem("开机自动启动")
        {
            CheckOnClick = true,
            Checked = AutoStart.IsEnabled(),
        };
        autoStartItem.CheckedChanged += (_, _) =>
        {
            try
            {
                AutoStart.Set(autoStartItem.Checked);
            }
            catch (Exception ex)
            {
                Log.Write(ex);
            }
        };
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        return menu;
    }

    private void TogglePanel()
    {
        if (_panel.Visible)
        {
            _panel.Hide();
            return;
        }

        if ((DateTime.UtcNow - _panel.LastHiddenAt).TotalMilliseconds < 300)
        {
            return;
        }

        ShowPanel();
    }

    private void ShowPanel()
    {
        var anchor = _strip.Visible ? _strip.RectangleToScreen(_strip.ClientRectangle) : (Rectangle?)null;
        _panel.ShowNearStrip(anchor);
    }

    private async Task RefreshAsync(bool showPanelAfter)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _panel.SetLoading(true);

        try
        {
            var snapshot = await _service.GetUsageAsync();
            _lastSnapshot = snapshot;
            _panel.ShowSnapshot(snapshot);
            _strip.ShowSnapshot(snapshot);
            UpdateIcon(snapshot.HasPrimary ? snapshot.PrimaryRemaining : (int?)null, error: false);
            _tray.Text = BuildTrayText(snapshot);
        }
        catch (Exception ex)
        {
            _panel.ShowError(ex.Message);
            _strip.ShowError();
            UpdateIcon(_lastSnapshot?.HasPrimary == true ? _lastSnapshot.PrimaryRemaining : null, error: true);
            _tray.Text = "Codex 用量 · 获取失败，点击查看";
            Log.Write(ex);
        }
        finally
        {
            _busy = false;
            _panel.SetLoading(false);
        }

        if (showPanelAfter)
        {
            ShowPanel();
        }
    }

    private static string BuildTrayText(UsageSnapshot snapshot)
    {
        var parts = new List<string>();
        if (snapshot.HasPrimary)
        {
            parts.Add($"{snapshot.PrimaryLabel} 剩余 {snapshot.PrimaryRemaining}%");
        }

        if (snapshot.HasSecondary)
        {
            parts.Add($"{snapshot.SecondaryLabel} 剩余 {snapshot.SecondaryRemaining}%");
        }

        var text = parts.Count > 0 ? "Codex · " + string.Join(" · ", parts) : "Codex 用量";
        return text.Length <= 63 ? text : text[..63];
    }

    private void UpdateIcon(int? remaining, bool error)
    {
        var next = TrayIconRenderer.Create(remaining, error);
        var previous = _currentIcon;
        _currentIcon = next;
        _tray.Icon = next;
        previous?.Dispose();
    }

    private void ExitApp()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _strip.Hide();
        _strip.Dispose();
        _panel.Hide();
        _panel.Dispose();
        _currentIcon?.Dispose();
        ExitThread();
    }
}
