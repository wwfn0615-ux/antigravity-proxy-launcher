using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal static class AntigravityProxyLauncher
{
    internal const string Title = "Antigravity Proxy Launcher";
    private const string Usage =
        "双击启动器，检测代理后点击「启动 Antigravity」。\r\n\r\n" +
        "命令行方式（仅设置 Antigravity 进程代理）\r\n" +
        "AntigravityProxyLauncher.exe --proxy http://127.0.0.1:7890\r\n" +
        "可用 --antigravity 指定 Antigravity.exe 的路径。\r\n" +
        "可用 --restore-browser-proxy 恢复上次中断的浏览器代理。";

    [STAThread]
    private static int Main(string[] args)
    {
        var options = new LaunchOptions();
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            switch (option)
            {
                case "--help":
                case "-h":
                    MessageBox.Show(Usage, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                case "--updated":
                    options.Updated = true;
                    break;
                case "--restore-browser-proxy":
                    options.Restore = true;
                    break;
                case "--proxy":
                    if (++index < args.Length && ProxyAddress.TryParse(args[index], out options.Proxy))
                        break;
                    return ShowError("请输入有效的 HTTP、HTTPS 或 SOCKS5 代理地址。", 2);
                case "--antigravity":
                    if (++index < args.Length && Installation.IsExecutable(args[index]))
                    {
                        options.Executable = Path.GetFullPath(args[index]);
                        break;
                    }
                    return ShowError("请选择已安装的 Antigravity.exe。", 2);
                default:
                    return ShowError(Usage, 2);
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using (var gate = new Mutex(false, @"Local\AntigravityProxyLauncher.Windows"))
        {
            bool owner;
            try { owner = gate.WaitOne(0); }
            catch (AbandonedMutexException) { owner = true; }
            if (!owner)
                return ShowError("启动器已经打开。请回到原来的启动器窗口。", 1);

            try
            {
                string recoveryError = null;
                try { BrowserProxySession.Recover(); }
                catch (Exception error) { recoveryError = error.Message; }

                if (options.Restore)
                {
                    if (recoveryError != null)
                        return ShowError(recoveryError, 1);
                    MessageBox.Show("上次的浏览器代理恢复检查已完成。", Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                if (options.Proxy != null)
                {
                    string executable = options.Executable ?? Installation.Find();
                    Launch(executable, options.Proxy, options.Updated);
                    return 0;
                }

                Application.Run(new LauncherForm(options.Executable ?? Installation.Find(),
                    options.Updated, recoveryError));
                return 0;
            }
            catch (Exception error) { return ShowError(error.Message, 1); }
            finally { gate.ReleaseMutex(); }
        }
    }

    internal static void CheckReady(string executable)
    {
        if (!Installation.IsExecutable(executable))
            throw new InvalidOperationException("找不到 Antigravity.exe，请选择安装位置。");

        foreach (Process process in Process.GetProcessesByName("Antigravity"))
        {
            using (process)
            {
                if (!process.HasExited)
                    throw new InvalidOperationException(
                        "Antigravity 已在运行。请保存工作并完全退出应用（包括托盘），然后重新点击启动。");
            }
        }
    }

    internal static void Launch(string executable, ProxyAddress proxy, bool updated)
    {
        CheckReady(executable);
        using (Process.Start(CreateStartInfo(executable, proxy, updated))) { }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, ProxyAddress proxy, bool updated)
    {
        var args = new List<string>();
        if (updated)
            args.Add("--updated");
        args.Add("--proxy-server=" + proxy.Value);
        args.Add("--proxy-bypass-list=localhost;127.0.0.0/8;[::1]");

        var start = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executable),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable)),
            Arguments = CommandLine.QuoteAll(args),
            UseShellExecute = false
        };
        start.EnvironmentVariables["HTTP_PROXY"] = proxy.Value;
        start.EnvironmentVariables["HTTPS_PROXY"] = proxy.Value;
        start.EnvironmentVariables["ALL_PROXY"] = proxy.Value;
        start.EnvironmentVariables["NO_PROXY"] = "localhost,127.0.0.0/8,::1";
        return start;
    }

    private static int ShowError(string text, int code)
    {
        MessageBox.Show(text, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return code;
    }

    private sealed class LaunchOptions
    {
        internal ProxyAddress Proxy;
        internal string Executable;
        internal bool Updated;
        internal bool Restore;
    }
}
