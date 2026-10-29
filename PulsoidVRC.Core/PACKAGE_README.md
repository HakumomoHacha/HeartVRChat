# HeartVRChat.Core

「心中的VRChat」的 .NET 8 核心库，许可为 **GPL-3.0-only**。完整桌面软件请从 [GitHub Releases](https://github.com/HakumomoHacha/HeartVRChat/releases/latest) 下载。

## 功能

| API | 用途 |
|---|---|
| `BrowserReader` | 在 Windows 的 Edge／Chrome 中后台读取 Pulsoid 心率组件文字 |
| `PulsoidClient` | 使用具备心率读取权限的 Token 连接 Pulsoid 官方 API |
| `SampleBuffer` | 过滤旧数据、重复与乱序时间戳 |
| `ChatPump` | 限制聊天消息频率，每次取最新心率 |
| `OscSender` / `OscPacket` | 编码、发送 VRChat `/chatbox/input` 消息 |

命名空间为 `PulsoidVRC.Core`。网页读取需要另装 Edge 或 Chrome；库本身不包含浏览器或桌面界面。

## 安装

GitHub NuGet 源：`https://nuget.pkg.github.com/HakumomoHacha/index.json`。GitHub Packages 的 NuGet 下载需要 GitHub 身份验证，个人访问令牌使用 `read:packages` 权限；请通过自己的 NuGet 配置或 CI 密钥设置凭据，不要提交令牌。

配置好包源后：

```powershell
dotnet add package HeartVRChat.Core --version 1.3.0
```

## 示例

以下代码仅构造 OSC 数据包，不会发送网络消息：

```csharp
using PulsoidVRC.Core;

var template = "♥ 心率 {bpm} BPM";
ChatText.Validate(template);
var text = ChatText.Format(template, 82);
byte[] osc = OscPacket.Chat(text, sound: false);
```

源码、完整示例和测试：[HakumomoHacha/HeartVRChat](https://github.com/HakumomoHacha/HeartVRChat)。使用或分发本库请遵守随附 `LICENSE` 和 `NOTICE`。
