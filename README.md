# Antigravity Proxy Launcher

这是一个 Windows 启动器。它把 HTTP 代理设置传给 Google Antigravity 及其子进程，适用于 Antigravity 的模型后台直连超时、现有代理可以访问 Google 的情况。

## 准备

- Antigravity 安装在默认用户目录 `%LOCALAPPDATA%\Programs\antigravity`。
- 代理服务已运行。准备好它的 HTTP 或 HTTPS 代理地址。

## 编译

在源码目录打开 PowerShell，使用 Windows 上的 .NET Framework C# 编译器生成启动器。

```powershell
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu /out:AntigravityProxyLauncher.exe /reference:System.Windows.Forms.dll .\AntigravityProxyLauncher.cs
```

## 使用

先完全退出已打开的 Antigravity，再启动。将示例中的地址换成你自己的代理地址。

```powershell
.\AntigravityProxyLauncher.exe --proxy "http://127.0.0.1:10809"
```

省略 `--proxy` 时使用 `http://127.0.0.1:7892`。运行 `.\AntigravityProxyLauncher.exe --help` 可以查看参数帮助。

常用地址可以写在 Windows 快捷方式的「目标」里。将示例中的路径换成启动器的实际位置。

```text
"C:\Tools\AntigravityProxyLauncher.exe" --proxy "http://127.0.0.1:10809"
```

启动器通过 `HTTP_PROXY` 和 `HTTPS_PROXY` 传递地址，通过 `NO_PROXY` 保持本机回环连接直连。

## 许可

源码采用 MIT 许可证。由 Codex 协助编写。 [许可证全文](LICENSE)
