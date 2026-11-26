using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

internal sealed class ProxyAddress
{
    internal string Value { get; private set; }
    internal string Scheme { get; private set; }
    internal string Authority { get; private set; }
    internal string CurlValue { get { return Scheme == "socks5" ? "socks5h://" + Authority : Value; } }
    internal string PacValue
    {
        get { return (Scheme == "https" ? "HTTPS" : Scheme == "socks5" ? "SOCKS5" : "PROXY") + " " + Authority; }
    }

    private ProxyAddress() { }

    internal static bool TryParse(string input, out ProxyAddress proxy)
    {
        proxy = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string text = input.Trim();
        if (text.Any(char.IsWhiteSpace) || text.Any(char.IsControl) ||
            text.IndexOfAny(new[] { '\\', '"', '\'', '@', ',', ';', '%', '=' }) >= 0)
            return false;
        int separator = text.IndexOf("://", StringComparison.Ordinal);
        string scheme = separator < 0 ? "http" : text.Substring(0, separator).ToLowerInvariant();
        if (scheme == "socks5h")
            scheme = "socks5";
        if (scheme != "http" && scheme != "https" && scheme != "socks5")
            return false;

        string address = separator < 0 ? text : text.Substring(separator + 3);
        if (scheme == "socks5" && !System.Text.RegularExpressions.Regex.IsMatch(address, @":[0-9]+/?$"))
            return false;

        Uri url;
        string parserScheme = scheme == "https" ? "https" : "http";
        if (!Uri.TryCreate(parserScheme + "://" + address, UriKind.Absolute, out url) ||
            (url.AbsolutePath != "/" && url.AbsolutePath.Length != 0) ||
            url.Query.Length != 0 || url.Fragment.Length != 0 ||
            url.HostNameType == UriHostNameType.Unknown || url.HostNameType == UriHostNameType.Basic ||
            url.Port <= 0)
            return false;

        string host = url.IdnHost.Trim('[', ']');
        string authority = (url.HostNameType == UriHostNameType.IPv6 ? "[" + IPAddress.Parse(host).ToString() + "]" : host)
            + ":" + url.Port.ToString(CultureInfo.InvariantCulture);
        proxy = new ProxyAddress { Value = scheme + "://" + authority, Scheme = scheme, Authority = authority };
        return true;
    }
}

internal static class CommandLine
{
    internal static string QuoteAll(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(Quote));
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }

            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }

        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }
}

internal static class Installation
{
    internal static bool IsExecutable(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            string.Equals(Path.GetFileName(path), "Antigravity.exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(path);
    }

    internal static string Find()
    {
        var paths = new List<string>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        paths.Add(Path.Combine(local, "Programs", "antigravity", "Antigravity.exe"));
        foreach (Environment.SpecialFolder folder in new[]
        {
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86
        })
        {
            string root = Environment.GetFolderPath(folder);
            if (root.Length == 0)
                continue;
            paths.Add(Path.Combine(root, "Antigravity", "Antigravity.exe"));
            paths.Add(Path.Combine(root, "Google", "Antigravity", "Antigravity.exe"));
        }

        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\App Paths\Antigravity.exe"))
                {
                    string value = key == null ? null : key.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        paths.Add(value.Trim('"'));
                }
                using (RegistryKey uninstall = root.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null)
                        continue;
                    foreach (string name in uninstall.GetSubKeyNames())
                    {
                        using (RegistryKey app = uninstall.OpenSubKey(name))
                        {
                            string title = app == null ? null : app.GetValue("DisplayName") as string;
                            if (!string.Equals(title, "Antigravity", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(title, "Google Antigravity", StringComparison.OrdinalIgnoreCase))
                                continue;
                            string path = app.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrWhiteSpace(path))
                                paths.Add(Path.Combine(path.Trim('"'), "Antigravity.exe"));
                        }
                    }
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return paths.FirstOrDefault(IsExecutable);
    }
}

internal static class ProxyDiscovery
{
    private static readonly int[] CommonPorts = { 7890, 7897, 7892, 10809, 10808, 7891, 1080, 2080, 2081, 20171, 20172, 2334 };

    internal static List<ProxyAddress> Find(string lastProxy)
    {
        var choices = new List<ProxyAddress>();
        try
        {
            BrowserProxySettings browser = WindowsProxy.Read();
            if (browser.HasManualProxy)
                Add(choices, FromWindowsServer(browser.Server));
        }
        catch (System.ComponentModel.Win32Exception) { }

        foreach (string name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY" })
            Add(choices, Environment.GetEnvironmentVariable(name));
        Add(choices, lastProxy);

        try
        {
            var active = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            foreach (int port in CommonPorts)
            {
                if (active.Any(listener => listener.Port == port &&
                    (listener.Address.Equals(IPAddress.Any) ||
                     listener.Address.Equals(IPAddress.IPv6Any) ||
                     listener.Address.Equals(IPAddress.Loopback))))
                {
                    Add(choices, "http://127.0.0.1:" + port);
                    Add(choices, "socks5://127.0.0.1:" + port);
                }

                if (active.Any(listener => listener.Port == port &&
                    (listener.Address.Equals(IPAddress.IPv6Any) ||
                     listener.Address.Equals(IPAddress.IPv6Loopback))))
                {
                    Add(choices, "http://[::1]:" + port);
                    Add(choices, "socks5://[::1]:" + port);
                }
            }
        }
        catch (NetworkInformationException) { }

        return choices;
    }

    internal static string FromWindowsServer(string server)
    {
        if (string.IsNullOrWhiteSpace(server))
            return null;
        if (!server.Contains("="))
            return server.Trim();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in server.Split(';'))
        {
            int equal = entry.IndexOf('=');
            if (equal > 0)
                map[entry.Substring(0, equal).Trim()] = entry.Substring(equal + 1).Trim();
        }

        string value;
        if (map.TryGetValue("https", out value))
            return value;
        if (map.TryGetValue("socks", out value) && value.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
            return value;
        return null;
    }

    private static void Add(List<ProxyAddress> choices, string input)
    {
        ProxyAddress proxy;
        if (ProxyAddress.TryParse(input, out proxy) && !choices.Any(x => x.Value == proxy.Value))
            choices.Add(proxy);
    }
}

internal sealed class ProbeResult
{
    internal string Host;
    internal int Status;
    internal int ExitCode;
    internal bool Reachable { get { return ExitCode == 0 && Status >= 200 && Status < 600 && Status != 407; } }

    internal string Describe()
    {
        if (Reachable)
            return Host + "  已收到 HTTPS 响应 (HTTP " + Status + ")";
        if (ExitCode == 28)
            return Host + "  连接超时";
        if (ExitCode == 60 || ExitCode == 35)
            return Host + "  TLS 验证或握手失败 (curl " + ExitCode + ")";
        if (Status == 407)
            return Host + "  代理要求认证 (HTTP 407)";
        return Host + "  连接失败 (curl " + ExitCode + ")";
    }
}

internal static class ProxyProbe
{
    internal static readonly string[] Targets =
    {
        "https://accounts.google.com/o/oauth2/v2/auth",
        "https://oauth2.googleapis.com/token",
        "https://daily-cloudcode-pa.googleapis.com/"
    };

    internal static Task<ProbeResult[]> CheckAsync(ProxyAddress proxy)
    {
        return Task.WhenAll(Targets.Select(target => Task.Run(() => Check(proxy, target))));
    }

    internal static async Task<ProxyAddress> DetectAsync(List<ProxyAddress> candidates)
    {
        // Test candidates in small batches while retaining discovery priority.
        for (int index = 0; index < candidates.Count; index += 4)
        {
            ProxyAddress[] batch = candidates.Skip(index).Take(4).ToArray();
            ProbeResult[] results = await Task.WhenAll(batch.Select(
                proxy => Task.Run(() => Check(proxy, Targets[1]))));
            for (int test = 0; test < results.Length; test++)
            {
                if (results[test].Reachable)
                    return batch[test];
            }
        }
        return null;
    }

    internal static ProbeResult Check(ProxyAddress proxy, string url)
    {
        string curl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
        if (!File.Exists(curl))
            throw new FileNotFoundException("找不到 Windows 自带的 curl.exe，请使用较新的 Windows 10 或 Windows 11。");

        var start = new ProcessStartInfo
        {
            FileName = curl,
            Arguments = CommandLine.QuoteAll(new[]
            {
                "-q", "--silent", "--proxy", proxy.CurlValue, "--noproxy", "",
                "--connect-timeout", "3", "--max-time", "6", "--proto", "=https",
                "--output", "NUL", "--write-out", "%{http_code} %{http_connect}", url
            }),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        var result = new ProbeResult { Host = new Uri(url).Host };
        using (Process process = Process.Start(start))
        {
            if (!process.WaitForExit(7500))
            {
                process.Kill();
                result.ExitCode = 28;
                return result;
            }

            result.ExitCode = process.ExitCode;
            string[] response = process.StandardOutput.ReadToEnd().Trim().Split(' ');
            if (response.Length == 2)
            {
                int.TryParse(response[0], out result.Status);
                if (response[1] == "407")
                    result.Status = 407;
            }
            return result;
        }
    }
}
