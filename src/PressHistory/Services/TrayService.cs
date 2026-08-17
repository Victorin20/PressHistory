using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PressHistory.Services;

public sealed class TrayService : IDisposable
{
    private readonly Drawing.Icon _icon;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _captureItem;
    private readonly Forms.ToolStripMenuItem _startupItem;

    public TrayService()
    {
        _icon = TrayIconFactory.Create();
        _captureItem = new Forms.ToolStripMenuItem("Capture active");
        _startupItem = new Forms.ToolStripMenuItem("Démarrer avec Windows")
        {
            CheckOnClick = false
        };

        var menu = new Forms.ContextMenuStrip();
        var openItem = new Forms.ToolStripMenuItem("Ouvrir PressHistory")
        {
            Font = new Drawing.Font(Forms.Control.DefaultFont, Drawing.FontStyle.Bold)
        };
        var clearItem = new Forms.ToolStripMenuItem("Effacer l’historique");
        var exitItem = new Forms.ToolStripMenuItem("Quitter");

        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _captureItem.Click += (_, _) => CaptureToggleRequested?.Invoke(this, EventArgs.Empty);
        _startupItem.Click += (_, _) => StartupToggleRequested?.Invoke(this, EventArgs.Empty);
        clearItem.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_captureItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(clearItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "PressHistory — historique du presse-papiers",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? CaptureToggleRequested;

    public event EventHandler? StartupToggleRequested;

    public event EventHandler? ClearRequested;

    public event EventHandler? ExitRequested;

    public void UpdateState(bool captureEnabled, bool startupEnabled)
    {
        _captureItem.Text = "Capture active";
        _captureItem.Checked = captureEnabled;
        _startupItem.Checked = startupEnabled;
        _notifyIcon.Text = captureEnabled
            ? "PressHistory — capture active"
            : "PressHistory — capture en pause";
    }

    public void ShowStillRunningHint()
    {
        _notifyIcon.BalloonTipTitle = "PressHistory reste actif";
        _notifyIcon.BalloonTipText =
            "L’historique continue d’être enregistré. Double-cliquez sur l’icône pour rouvrir la fenêtre.";
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4_000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
