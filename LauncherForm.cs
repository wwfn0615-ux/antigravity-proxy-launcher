using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class LauncherForm : Form
{
    private readonly ComboBox proxyBox = new ComboBox { Dock = DockStyle.Fill };
    private readonly TextBox executableBox = new TextBox { Dock = DockStyle.Fill };
    private readonly Button detect = new Button { Text = "检测代理", AutoSize = true };
    private readonly Button browse = new Button { Text = "浏览", AutoSize = true };
    private readonly Button start = new Button { Text = "启动 Antigravity", AutoSize = true };
    private readonly Button restore = new Button { Text = "恢复浏览器代理", AutoSize = true, Enabled = false };
    private readonly CheckBox browser = new CheckBox
    {
        Text = "登录期间让默认浏览器使用同一个代理",
        AutoSize = true,
        Checked = true
    };
    private readonly TextBox status = new TextBox
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical
    };
    private readonly bool updated;
    private readonly string recoveryError;
    private BrowserProxySession browserSession;
    private bool busy;
    private string lastProxy;
    private static readonly string SavedProxy = Path.Combine(BrowserProxySession.DataDirectory, "last-proxy.txt");

    internal LauncherForm(string executable, bool updated, string recoveryError)
    {
        this.updated = updated;
        this.recoveryError = recoveryError;
        Text = AntigravityProxyLauncher.Title;
        Font = new Font("Microsoft YaHei UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(740, 390);
        MinimumSize = new Size(650, 385);
        AutoScaleMode = AutoScaleMode.Dpi;
        executableBox.Text = executable ?? "";
        lastProxy = LoadLastProxy();
        browser.Enabled = recoveryError == null;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 3,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int row = 0; row < 7; row++)
            layout.RowStyles.Add(new RowStyle(row == 5 ? SizeType.Percent : SizeType.AutoSize, 100));

        AddFull(layout, new Label
        {
            Text = "通过现有代理登录 Antigravity",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 12)
        }, 0);
        layout.Controls.Add(new Label { Text = "代理地址", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(proxyBox, 1, 1);
        layout.Controls.Add(detect, 2, 1);
        layout.Controls.Add(new Label { Text = "安装路径", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(executableBox, 1, 2);
        layout.Controls.Add(browse, 2, 2);
        AddFull(layout, browser, 3);
        AddFull(layout, new Label
        {
            Text = "需要临时切换 Windows 代理时，请保留此窗口。登录完成后点恢复，也可关闭窗口。",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 12)
        }, 4);
        AddFull(layout, status, 5);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        actions.Controls.Add(start);
        actions.Controls.Add(restore);
        AddFull(layout, actions, 6);
        Controls.Add(layout);
        AcceptButton = start;

        detect.Click += async (sender, eventArgs) => await Detect();
        start.Click += async (sender, eventArgs) => await StartApp();
        restore.Click += (sender, eventArgs) => RestoreBrowser();
        browse.Click += (sender, eventArgs) => Browse();
        Shown += async (sender, eventArgs) => await Detect();
        FormClosing += CloseForm;
        if (recoveryError != null)
            browser.Checked = false;
    }

    private static void AddFull(TableLayoutPanel layout, Control control, int row)
    {
        layout.Controls.Add(control, 0, row);
        layout.SetColumnSpan(control, 3);
    }

    private async Task Detect()
    {
        SetBusy(true);
        SetStatus("正在检测已有代理...");
        try
        {
            var candidates = await Task.Run(() => ProxyDiscovery.Find(lastProxy));
            ProxyAddress selected = await ProxyProbe.DetectAsync(candidates);
            if (IsDisposed)
                return;

            proxyBox.Items.Clear();
            proxyBox.Items.AddRange(candidates.Select(proxy => (object)proxy.Value).ToArray());
            if (selected != null)
            {
                proxyBox.Text = selected.Value;
                SetStatus("已找到可连接 Google 接口的代理。点击「启动 Antigravity」继续。");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(proxyBox.Text) && candidates.Count > 0)
                    proxyBox.Text = candidates[0].Value;

                SetStatus("尚未找到可用代理。请启动代理软件，或填入它显示的 HTTP、混合或 SOCKS5 端口。");
            }
        }
        catch (Exception error) { if (!IsDisposed) SetStatus("代理检测失败。 " + error.Message); }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private async Task StartApp()
    {
        ProxyAddress proxy;
        if (!ProxyAddress.TryParse(proxyBox.Text, out proxy))
        {
            SetStatus("请填入代理地址，例如 http://127.0.0.1:7890。");
            return;
        }

        string executable = executableBox.Text.Trim();
        SetBusy(true);
        SetStatus("正在检查登录页、令牌接口和模型接口...");
        try
        {
            AntigravityProxyLauncher.CheckReady(executable);
            ProbeResult[] results = await ProxyProbe.CheckAsync(proxy);
            if (IsDisposed)
                return;

            string checks = string.Join(Environment.NewLine, results.Select(probe => probe.Describe()));
            if (!results[0].Reachable || !results[1].Reachable)
            {
                SetStatus(checks + Environment.NewLine + "请确认代理已启动，或换一个可用节点后再试。");
                return;
            }

            if (browser.Checked)
                browserSession = BrowserProxySession.Start(proxy);

            AntigravityProxyLauncher.Launch(executable, proxy, updated);
            lastProxy = proxy.Value;
            SaveLastProxy(proxy.Value);
            SetStatus(checks + Environment.NewLine +
                (browserSession != null
                    ? "已启动。请在默认浏览器完成登录，然后恢复浏览器代理。"
                    : "已启动。请在 Antigravity 中点击登录。"));
        }
        catch (Exception error)
        {
            if (!IsDisposed)
            {
                bool restored = RestoreBrowser();
                SetStatus(error.Message + (restored ? "" : Environment.NewLine + "浏览器代理仍待恢复。"));
            }
        }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private bool RestoreBrowser()
    {
        if (browserSession == null)
            return true;

        try
        {
            browserSession.Dispose();
            browserSession = null;
            SetStatus("已结束本次临时浏览器代理。Antigravity 可以继续使用所选代理。");
            SetBusy(false);
            return true;
        }
        catch (Exception error)
        {
            SetStatus("恢复浏览器代理失败。 " + error.Message);
            return false;
        }
    }

    private void CloseForm(object sender, FormClosingEventArgs args)
    {
        if (!RestoreBrowser() && args.CloseReason == CloseReason.UserClosing)
            args.Cancel = true;
    }

    private void Browse()
    {
        using (var dialog = new OpenFileDialog
        {
            Filter = "Antigravity|Antigravity.exe",
            FileName = "Antigravity.exe",
            CheckFileExists = true
        })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                executableBox.Text = dialog.FileName;
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        bool adjustable = !busy && browserSession == null;
        proxyBox.Enabled = adjustable;
        executableBox.Enabled = adjustable;
        detect.Enabled = adjustable;
        browse.Enabled = adjustable;
        browser.Enabled = adjustable && recoveryError == null;
        start.Enabled = adjustable;
        restore.Enabled = !busy && browserSession != null;
        UseWaitCursor = busy;
    }

    private void SetStatus(string text)
    {
        status.Text = recoveryError == null ? text : text + Environment.NewLine +
            "上次浏览器代理恢复失败。 " + recoveryError;
    }

    private static string LoadLastProxy()
    {
        try { return File.Exists(SavedProxy) ? File.ReadAllText(SavedProxy).Trim() : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void SaveLastProxy(string proxy)
    {
        try
        {
            Directory.CreateDirectory(BrowserProxySession.DataDirectory);
            File.WriteAllText(SavedProxy, proxy);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
