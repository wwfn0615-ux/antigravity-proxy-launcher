# Antigravity Proxy Launcher

让 Windows 上的 Google Antigravity 和登录浏览器使用你已经有的代理，关闭 TUN 也能配置。适合浏览器打不开 Google 登录页、登录回到应用后一直转圈、或模型后台直连超时的情况。

**[下载 Windows 发行包](https://github.com/wwfn0615-ux/antigravity-proxy-launcher/releases/latest)**

## 下载后直接使用

1. 安装 [Google Antigravity App](https://antigravity.google/docs/quickstart/) 并运行你的代理软件。代理软件需要提供能访问 Google 的 HTTP、混合或 SOCKS5 端口。TUN 可以关着。
2. 在 Releases 下载 `Antigravity-Proxy-Launcher-Windows.zip`，解压后双击 `AntigravityProxyLauncher.exe`。启动器会寻找 Antigravity 和可用代理。检测不到时，在窗口里选择 `Antigravity.exe` 或填入代理软件显示的地址。
3. 先保存并退出已经打开的 Antigravity，再点击**启动 Antigravity**。进入应用后点击登录，在默认浏览器里完成 Google 认证。
4. 如果启动器临时切换了 Windows 代理，请保持它打开到登录完成。成功返回应用后，点击**恢复浏览器代理**或关掉启动器窗口。

适用环境为 Windows 10 和 Windows 11，需要系统自带的 `curl.exe` 和 .NET Framework。发行包已编译，常规使用无需装开发环境或以管理员身份启动。启动位置和现有代理节点均由你选择，代理软件需要保持运行。

## 代理地址怎么填

| 代理软件显示的类型 | 输入示例 |
| --- | --- |
| HTTP 或 Mixed / 混合端口 | `http://127.0.0.1:7890` |
| SOCKS5 端口 | `socks5://127.0.0.1:10808` |
| 使用 TLS 连接的 HTTP 代理 | `https://proxy.example.org:8443` |
| 使用 IPv6 回环的混合端口 | `http://[::1]:7890` |

示例端口仅供参考，以你自己的代理软件界面为准。Clash 系、v2rayN 和 sing-box 等工具都可以使用它们提供的本地代理端口。连接 HTTPS 网站通常也使用 `http://` 混合端口，由代理建立 HTTPS 隧道。只有代理入口本身使用 TLS 时才填 `https://`。当前支持无需单独输入用户名和密码的代理入口。

启动器优先检测 Windows 的手动代理、代理环境变量、上次成功启动使用的地址和常见的本机监听端口。自动结果可以手动改。你在代理软件里换节点时，通常只要保留同一个本机端口，Antigravity 的新连接就会跟随代理；已打开的连接有时需要重试或重新打开应用。

## 登录是两段连接

| 连接 | 启动器的处理 |
| --- | --- |
| Antigravity 窗口和后台 | 给 Electron 传入代理参数，给应用与它启动的后台进程传入 `HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY` 和 `NO_PROXY`。 |
| 外部默认浏览器 | 已配置为同一手动代理时直接复用。勾选窗口里的浏览器选项时，可在登录期间临时通过 Windows 代理配置使用同一入口。 |
| 本机登录回调 | `localhost`、`127.0.0.0/8` 和 `::1` 保持直连，Google 登录完成后可以返回应用。 |

临时浏览器代理会影响同一 Windows 用户下遵循系统代理的程序。关闭窗口或点恢复会还原先前的代理配置。启动器被强行终止时，再打开它可以恢复上次的临时设置。浏览器如果受企业策略或自定义代理扩展控制，请在浏览器里核对其代理。 [Google 登录流程](https://antigravity.google/docs/install) · [Electron 代理参数](https://www.electronjs.org/docs/latest/api/command-line-switches/#--proxy-serveraddressport)

## 如果依然登录失败

| 现象 | 检查方法 |
| --- | --- |
| 启动器识别不到代理 | 在代理软件里查看真实本机地址和 HTTP、混合或 SOCKS5 端口，再填入窗口。`127.0.0.1` 后面的端口因人而异。 |
| 浏览器里的 Google 登录页打不开 | 勾选「登录期间让默认浏览器使用同一个代理」，保留启动器窗口，并检查浏览器是否遵循 Windows 系统代理。 |
| 网页能登录，回到 Antigravity 后卡住 | 完全退出旧的 Antigravity，包括托盘进程，再从启动器打开。查看窗口中的 OAuth 令牌接口和模型接口探测结果。 |
| 提示 `User location is not supported`、`Detected location` 或 `PERMISSION_DENIED` | 查看 Google 对账号和地区的要求，并在你有权使用的代理线路中选合适的出口。普通查 IP 显示的地区和 Google 对具体服务请求的判定可能不同。 [Google 地区说明](https://antigravity.google/docs/faq/#what-is-google-antigravitys-geographical-availability) |
| 切换节点后突然失败 | 先在代理软件里验证新节点能访问 Google，再重试原请求。若旧连接仍在复用，退出应用并从启动器重开。 |

诊断窗口里出现 HTTP `400`、`401`、`404` 或 `405`，可能表示测试成功到达了对应服务器。连通性测试没有使用你的登录凭据，实际账号能否登录以浏览器认证和应用中的结果为准。

### 让 AI 按你的电脑配置

把下面这段交给能访问你电脑的 AI 助手。可以附上代理软件名称、Antigravity 版本和报错文字。发送日志前，请删掉账号、Cookie、OAuth 授权码、代理订阅地址和访问令牌。

```text
我在 Windows 上使用 Google Antigravity，关闭虚拟网卡（TUN）后登录或加载模型失败。
代理软件是【填写名称】，本机端口是【已知就填，不清楚请实际检查】，错误是【填写报错】。

请先只读检查 Antigravity.exe 的实际路径、运行中的旧进程，以及我的代理软件监听的
HTTP、混合或 SOCKS5 端口。分别验证 accounts.google.com、oauth2.googleapis.com
和 daily-cloudcode-pa.googleapis.com 能否经该代理完成 TLS 连接。

定位是浏览器登录、回环回调、OAuth 换令牌还是模型请求出错。然后用我已有的代理创建
一个 Antigravity 专用启动方式，给应用和后台传入正确的 HTTP_PROXY、HTTPS_PROXY
及本机 NO_PROXY，保留 localhost、127.0.0.0/8 和 ::1 的直连。

如果默认浏览器也需要代理，请使用可恢复的系统代理设置，说明变更范围。保持 TUN 关闭，
保留证书验证，让我自己在 Google 官方页面登录。最后检查模型列表并用一条简单消息验证。
```

## 命令行与源码

已有外部浏览器代理时，可以直接为应用进程指定地址。该命令适合写到自己的快捷方式里。

```powershell
.\AntigravityProxyLauncher.exe --proxy "http://127.0.0.1:10809"
.\AntigravityProxyLauncher.exe --proxy "socks5://127.0.0.1:10808" --antigravity "D:\Tools\Antigravity\Antigravity.exe"
.\AntigravityProxyLauncher.exe --restore-browser-proxy
```

从源码构建和跑冒烟测试。

```powershell
.\build.ps1 -Test -Package
```

输出在 `dist`。发行页同时提供 ZIP 的 SHA-256 校验文件，可用 `Get-FileHash -Algorithm SHA256` 核对；也可以自行从源码编译。

## 许可

本项目是由社区维护的独立代理启动器，采用 MIT 许可证。 [许可证全文](LICENSE)
