using System;
using System.IO;
using System.Net;

internal static class SmokeTests
{
    private static int done;

    private static void Expect(bool condition, string detail)
    {
        if (!condition)
            throw new InvalidOperationException("Smoke test failed: " + detail);
        done++;
    }

    private static ProxyAddress Parse(string text, string normalized)
    {
        ProxyAddress proxy;
        Expect(ProxyAddress.TryParse(text, out proxy) && proxy.Value == normalized, text + " (actual " + (proxy == null ? "invalid" : proxy.Value) + ")");
        return proxy;
    }

    private static int Main(string[] args)
    {
        try
        {
            Run();
            if (args.Length != 0)
                RunLiveProxyCheck(args);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 1;
        }
    }

    private static void Run()
    {
        ProxyAddress http = Parse("127.0.0.1:7890", "http://127.0.0.1:7890");
        ProxyAddress https = Parse("https://trusted.example:8443", "https://trusted.example:8443");
        ProxyAddress socks = Parse("socks5h://[::1]:10808", "socks5://[::1]:10808");

        foreach (string invalid in new[]
        {
            "", "ftp://127.0.0.1:21", "socks5://localhost", "http://localhost:70000",
            "http://user:secret@localhost:7890", "http://localhost:7890/path",
            "http://host:9000?x=y", "http://host:9000#x", "http://host:0",
            "http://localhost:7890\r\nX-Line: nope"
        })
        {
            ProxyAddress discarded;
            Expect(!ProxyAddress.TryParse(invalid, out discarded), "reject invalid proxy");
        }

        Expect(socks.CurlValue == "socks5h://[::1]:10808", "SOCKS5 DNS is resolved by the proxy");
        Expect(https.PacValue == "HTTPS trusted.example:8443", "HTTPS PAC scheme");
        Expect(ProxyDiscovery.FromWindowsServer("http=localhost:7890;https=localhost:10809") ==
            "localhost:10809", "choose HTTPS-destination rule");
        Expect(ProxyDiscovery.FromWindowsServer("socks=localhost:1080") == null, "ambiguous SOCKS version");
        Expect(ProxyDiscovery.FromWindowsServer("socks=socks5://localhost:1080") ==
            "socks5://localhost:1080", "explicit SOCKS5 rule");

        string application = Path.Combine(Path.GetTempPath(), "Test folder", "Antigravity.exe");
        var launch = AntigravityProxyLauncher.CreateStartInfo(application, socks, true);
        Expect(launch.FileName == application, "preserve a path containing spaces");
        Expect(launch.Arguments.Contains("\"--updated\"") &&
            launch.Arguments.Contains("\"--proxy-server=" + socks.Value + "\""), "Electron switches");
        Expect(launch.EnvironmentVariables["HTTP_PROXY"] == socks.Value &&
            launch.EnvironmentVariables["HTTPS_PROXY"] == socks.Value &&
            launch.EnvironmentVariables["ALL_PROXY"] == socks.Value, "child process environment");
        Expect(launch.EnvironmentVariables["NO_PROXY"].Contains("127.0.0.0/8") &&
            launch.Arguments.Contains("[::1]"), "OAuth callback bypass");
        Expect(CommandLine.QuoteAll(new[] { @"C:\Dir with spaces\" }) ==
            "\"C:\\Dir with spaces\\\\\"", "Windows argument quoting");

        var manual = new BrowserProxySettings { Flags = 3, Server = "127.0.0.1:7890", Bypass = "<local>", PacUrl = "" };
        Expect(manual.Uses(http), "reuse matching manual browser proxy");
        manual.Flags = 11;
        Expect(!manual.Uses(http), "respect WPAD priority");
        manual.Flags = 3;
        manual.Bypass = "*.google.com";
        Expect(!manual.Uses(http), "detect Google browser bypass");
        var owned = new BrowserProxySettings { Flags = 5, PacUrl = "http://127.0.0.1:45000/owned.pac" };
        Expect(owned.OwnsPac(owned.PacUrl) && !owned.OwnsPac("http://another.test/browser.pac"), "journal ownership");
        owned.Flags |= 2;
        Expect(!owned.OwnsPac(owned.PacUrl), "preserve another proxy's changes");

        CheckPac(http, "PROXY 127.0.0.1:7890");
        CheckPac(https, "HTTPS trusted.example:8443");
        CheckPac(socks, "SOCKS5 [::1]:10808");
        BrowserProxySettings state = WindowsProxy.Read();
        Expect(state.Server != null && state.Bypass != null && state.PacUrl != null, "read native Windows options");
        Console.WriteLine("{0} smoke checks passed.", done);
    }

    private static void RunLiveProxyCheck(string[] args)
    {
        ProxyAddress proxy;
        if (args.Length != 2 || args[0] != "--live-proxy" || !ProxyAddress.TryParse(args[1], out proxy))
            throw new ArgumentException("Use --live-proxy with a valid proxy address.");

        ProbeResult[] results = ProxyProbe.CheckAsync(proxy).GetAwaiter().GetResult();
        foreach (ProbeResult result in results)
        {
            Console.WriteLine("{0} HTTP={1} curl={2}", result.Host, result.Status, result.ExitCode);
            Expect(result.Reachable, result.Host + " returned a valid HTTPS response");
        }
    }

    private static void CheckPac(ProxyAddress proxy, string expected)
    {
        using (var server = new ProxyPacServer(proxy))
        {
            var request = (HttpWebRequest)WebRequest.Create(server.Url);
            request.Proxy = null;
            request.Timeout = 4000;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                string script = reader.ReadToEnd();
                Expect(response.ContentType.StartsWith("application/x-ns-proxy-autoconfig") &&
                    script.Contains("return '" + expected + "';") &&
                    script.Contains("'localhost'") && script.Contains("return 'DIRECT';"),
                    "PAC file serves expected route and excludes loopback");
            }
        }
    }
}
