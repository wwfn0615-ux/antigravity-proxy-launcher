using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal sealed class BrowserProxySettings
{
    internal int Flags;
    internal string Server;
    internal string Bypass;
    internal string PacUrl;
    internal bool HasManualProxy { get { return (Flags & 2) != 0; } }

    internal bool Uses(ProxyAddress proxy)
    {
        ProxyAddress current;
        return HasManualProxy && (Flags & (4 | 8)) == 0 &&
            !Bypass.StartsWith("*", StringComparison.Ordinal) &&
            Bypass.IndexOf("google", StringComparison.OrdinalIgnoreCase) < 0 &&
            Bypass.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) < 0 &&
            ProxyAddress.TryParse(ProxyDiscovery.FromWindowsServer(Server), out current) &&
            current.Value == proxy.Value;
    }

    internal bool OwnsPac(string url)
    {
        return (Flags & (2 | 4 | 8)) == 4 && string.Equals(PacUrl, url, StringComparison.Ordinal);
    }
}

internal static class WindowsProxy
{
    private const int PerConnectionOption = 75;
    private const int ProxySettingsChanged = 95;
    private const int Refresh = 37;
    private const int FlagsOption = 1;
    private const int FlagsUiOption = 10;
    private const int ServerOption = 2;
    private const int BypassOption = 3;
    private const int AutoConfigUrlOption = 4;

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private struct OptionValue
    {
        [FieldOffset(0)] internal int Number;
        [FieldOffset(0)] internal IntPtr Text;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Option
    {
        internal int Id;
        internal OptionValue Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OptionList
    {
        internal int Size;
        internal IntPtr Connection;
        internal int Count;
        internal int Error;
        internal IntPtr Values;
    }

    [DllImport("wininet.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetQueryOption(IntPtr handle, int option, ref OptionList list, ref int size);

    [DllImport("wininet.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr handle, int option, ref OptionList list, int size);

    [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Notify(IntPtr handle, int option, IntPtr data, int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    internal static BrowserProxySettings Read()
    {
        try { return Read(FlagsUiOption); }
        catch (Win32Exception) { return Read(FlagsOption); }
    }

    private static BrowserProxySettings Read(int flagsOption)
    {
        var options = new[]
        {
            new Option { Id = flagsOption }, new Option { Id = ServerOption },
            new Option { Id = BypassOption }, new Option { Id = AutoConfigUrlOption }
        };

        Transfer(options, false);
        try
        {
            return new BrowserProxySettings
            {
                Flags = options[0].Value.Number,
                Server = Marshal.PtrToStringUni(options[1].Value.Text) ?? "",
                Bypass = Marshal.PtrToStringUni(options[2].Value.Text) ?? "",
                PacUrl = Marshal.PtrToStringUni(options[3].Value.Text) ?? ""
            };
        }
        finally
        {
            for (int index = 1; index < options.Length; index++)
            {
                if (options[index].Value.Text != IntPtr.Zero)
                    GlobalFree(options[index].Value.Text);
            }
        }
    }

    internal static void Write(BrowserProxySettings settings)
    {
        var options = new[]
        {
            new Option { Id = FlagsOption, Value = new OptionValue { Number = settings.Flags } },
            new Option { Id = ServerOption, Value = new OptionValue { Text = Marshal.StringToHGlobalUni(settings.Server) } },
            new Option { Id = BypassOption, Value = new OptionValue { Text = Marshal.StringToHGlobalUni(settings.Bypass) } },
            new Option { Id = AutoConfigUrlOption, Value = new OptionValue { Text = Marshal.StringToHGlobalUni(settings.PacUrl) } }
        };
        try { Transfer(options, true); }
        finally
        {
            for (int index = 1; index < options.Length; index++)
                Marshal.FreeHGlobal(options[index].Value.Text);
        }

        Notify(IntPtr.Zero, ProxySettingsChanged, IntPtr.Zero, 0);
        Notify(IntPtr.Zero, Refresh, IntPtr.Zero, 0);
    }

    private static void Transfer(Option[] options, bool write)
    {
        int itemSize = Marshal.SizeOf(typeof(Option));
        var list = new OptionList
        {
            Size = Marshal.SizeOf(typeof(OptionList)),
            Count = options.Length,
            Values = Marshal.AllocHGlobal(itemSize * options.Length)
        };
        try
        {
            for (int index = 0; index < options.Length; index++)
                Marshal.StructureToPtr(options[index], IntPtr.Add(list.Values, index * itemSize), false);

            int size = list.Size;
            bool success = write
                ? InternetSetOption(IntPtr.Zero, PerConnectionOption, ref list, size)
                : InternetQueryOption(IntPtr.Zero, PerConnectionOption, ref list, ref size);
            int error = Marshal.GetLastWin32Error();

            if (!write)
            {
                for (int index = 0; index < options.Length; index++)
                    options[index] = (Option)Marshal.PtrToStructure(
                        IntPtr.Add(list.Values, index * itemSize), typeof(Option));
            }

            if (!success)
            {
                if (!write)
                {
                    for (int index = 1; index < options.Length; index++)
                    {
                        if (options[index].Value.Text != IntPtr.Zero)
                            GlobalFree(options[index].Value.Text);
                    }
                }
                throw new Win32Exception(error);
            }
        }
        finally { Marshal.FreeHGlobal(list.Values); }
    }
}

internal sealed class BrowserProxySession : IDisposable
{
    private const int RecordVersion = 1;
    internal static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AntigravityProxyLauncher");
    private static readonly string Record = Path.Combine(DataDirectory, "browser-proxy-recovery.bin");
    private readonly ProxyPacServer pacServer;

    private BrowserProxySession(ProxyPacServer server) { pacServer = server; }

    internal static BrowserProxySession Start(ProxyAddress proxy)
    {
        BrowserProxySettings current = WindowsProxy.Read();
        if (current.Uses(proxy))
            return null;

        Recover();
        current = WindowsProxy.Read();
        var server = new ProxyPacServer(proxy);
        try
        {
            SaveRecord(current, server.Url);
            WindowsProxy.Write(new BrowserProxySettings
            {
                Flags = 1 | 4,
                Server = current.Server,
                Bypass = current.Bypass,
                PacUrl = server.Url
            });

            if (!WindowsProxy.Read().OwnsPac(server.Url))
                throw new InvalidOperationException("Windows 未启用本次浏览器代理，请检查系统代理策略。");

            return new BrowserProxySession(server);
        }
        catch
        {
            try { Recover(); }
            finally { server.Dispose(); }
            throw;
        }
    }

    internal static void Recover()
    {
        if (!File.Exists(Record))
            return;

        string url;
        BrowserProxySettings previous;
        using (var reader = new BinaryReader(File.OpenRead(Record), Encoding.UTF8))
        {
            if (reader.ReadInt32() != RecordVersion)
                throw new InvalidDataException("无法读取上次的浏览器代理记录。");
            url = reader.ReadString();
            previous = new BrowserProxySettings
            {
                Flags = reader.ReadInt32(),
                Server = reader.ReadString(),
                Bypass = reader.ReadString(),
                PacUrl = reader.ReadString()
            };
        }

        // Respect changes made by another proxy client while this launcher was open.
        if (WindowsProxy.Read().OwnsPac(url))
        {
            WindowsProxy.Write(previous);
            if (WindowsProxy.Read().OwnsPac(url))
                throw new InvalidOperationException("Windows 未恢复原浏览器代理，可以再次点「恢复浏览器代理」。");
        }

        File.Delete(Record);
    }

    public void Dispose()
    {
        Recover();
        pacServer.Dispose();
    }

    private static void SaveRecord(BrowserProxySettings current, string url)
    {
        Directory.CreateDirectory(DataDirectory);
        string pending = Record + ".pending";
        using (var writer = new BinaryWriter(File.Create(pending), Encoding.UTF8))
        {
            writer.Write(RecordVersion);
            writer.Write(url);
            writer.Write(current.Flags);
            writer.Write(current.Server);
            writer.Write(current.Bypass);
            writer.Write(current.PacUrl);
        }
        File.Move(pending, Record);
    }
}

internal sealed class ProxyPacServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly string requestPath = "/" + Guid.NewGuid().ToString("N") + ".pac";
    private readonly byte[] body;
    private volatile bool stopping;

    internal string Url { get; private set; }

    internal ProxyPacServer(ProxyAddress proxy)
    {
        string script = "function FindProxyForURL(url, host) {\n" +
            "  var name = host.toLowerCase();\n" +
            "  if (name === 'localhost' || dnsDomainIs(name, '.localhost') ||\n" +
            "      /^127\\.[0-9]{1,3}\\.[0-9]{1,3}\\.[0-9]{1,3}$/.test(name) ||\n" +
            "      name === '::1' || name === '[::1]') return 'DIRECT';\n" +
            "  return '" + proxy.PacValue + "';\n" +
            "}\n";
        body = Encoding.ASCII.GetBytes(script);
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + requestPath;
        new Thread(Serve) { IsBackground = true, Name = "Loopback proxy configuration" }.Start();
    }

    private void Serve()
    {
        while (!stopping)
        {
            try
            {
                using (TcpClient client = listener.AcceptTcpClient())
                {
                    client.ReceiveTimeout = 1500;
                    client.SendTimeout = 1500;
                    using (NetworkStream stream = client.GetStream())
                        Reply(stream);
                }
            }
            catch (SocketException) { if (stopping) return; }
            catch (IOException) { if (stopping) return; }
            catch (ObjectDisposedException) { return; }
        }
    }

    private void Reply(NetworkStream stream)
    {
        var buffer = new byte[8192];
        int size = 0;
        string request = "";
        while (size < buffer.Length && !request.Contains("\r\n\r\n"))
        {
            int read = stream.Read(buffer, size, buffer.Length - size);
            if (read == 0)
                return;
            size += read;
            request = Encoding.ASCII.GetString(buffer, 0, size);
        }

        int end = request.IndexOf("\r\n", StringComparison.Ordinal);
        string firstLine = end >= 0 ? request.Substring(0, end) : "";
        bool get = firstLine.StartsWith("GET " + requestPath + " HTTP/", StringComparison.Ordinal);
        bool head = firstLine.StartsWith("HEAD " + requestPath + " HTTP/", StringComparison.Ordinal);
        string response = get || head
            ? "HTTP/1.1 200 OK\r\nContent-Type: application/x-ns-proxy-autoconfig\r\n" +
              "Cache-Control: no-store\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n"
            : "HTTP/1.1 404 Not Found\r\nConnection: close\r\nContent-Length: 0\r\n\r\n";
        byte[] header = Encoding.ASCII.GetBytes(response);
        stream.Write(header, 0, header.Length);
        if (get)
            stream.Write(body, 0, body.Length);
    }

    public void Dispose()
    {
        stopping = true;
        listener.Stop();
    }
}
