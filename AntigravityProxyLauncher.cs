using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class AntigravityProxyLauncher
{
    private const string Title = "Antigravity Proxy Launcher";
    private const string DefaultProxy = "http://127.0.0.1:7892";
    private const string Usage =
        "AntigravityProxyLauncher.exe [--proxy <代理地址>]\r\n\r\n" +
        "可使用 HTTP 或 HTTPS 代理地址。省略 --proxy 时使用 " + DefaultProxy + "。\r\n\r\n" +
        "示例\r\nAntigravityProxyLauncher.exe --proxy http://127.0.0.1:10809";

    [STAThread]
    private static int Main(string[] args)
    {
        // The Antigravity installer can pass this flag when it restarts via a shortcut.
        bool restartedAfterUpdate = Array.IndexOf(args, "--updated") >= 0;
        string[] options = Array.FindAll(args, arg => arg != "--updated");

        if (options.Length == 1 && (options[0] == "--help" || options[0] == "-h"))
        {
            MessageBox.Show(Usage, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        string proxy;
        if (!TryReadProxy(options, out proxy))
        {
            MessageBox.Show("请使用 --proxy 指定有效的 HTTP 或 HTTPS 代理地址。\r\n\r\n" + Usage,
                Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        try
        {
            using (Process.Start(CreateStartInfo(proxy, restartedAfterUpdate))) { }
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show("无法启动 Google Antigravity。" + Environment.NewLine + error.Message,
                Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool TryReadProxy(string[] options, out string proxy)
    {
        proxy = DefaultProxy;
        if (options.Length == 0)
            return true;

        if (options.Length != 2 || options[0] != "--proxy")
            return false;

        string candidate = options[1].Trim();
        Uri address;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(address.Host) || address.Port <= 0
            || address.AbsolutePath != "/" || address.Query.Length != 0 || address.Fragment.Length != 0)
            return false;

        proxy = candidate;
        return true;
    }

    private static ProcessStartInfo CreateStartInfo(string proxy, bool restartedAfterUpdate)
    {
        string installDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "antigravity");
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(installDirectory, "Antigravity.exe"),
            WorkingDirectory = installDirectory,
            Arguments = restartedAfterUpdate ? "--updated" : "",
            UseShellExecute = false
        };
        startInfo.EnvironmentVariables["HTTP_PROXY"] = proxy;
        startInfo.EnvironmentVariables["HTTPS_PROXY"] = proxy;
        startInfo.EnvironmentVariables["NO_PROXY"] = "localhost,127.0.0.1,::1";
        return startInfo;
    }
}
