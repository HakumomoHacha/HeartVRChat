## 心中的VRChat 1.3.0

Windows 心率聊天框工具：手机 Pulsoid 上传心率后，在电脑后台读取组件网页并通过 OSC 推送到 VRChat。

- 无需 Pulsoid API Token 的网页读取模式；开始推送不弹出浏览器窗口。
- 蓝粉白渐变、半透明卡片和轻量动画界面。
- 自定义聊天内容、发送间隔、数字定位与托盘运行。
- GPL-3.0-only 开源许可；附带对应源码和第三方许可。

### 下载

| 文件 | 用途 |
|---|---|
| `HeartVRChat-1.3.0-Windows-x64.zip` | 普通用户下载；解压后运行 `PulsoidVRC.exe` |
| `HeartVRChat-1.3.0-Source.zip` | 与该版本对应的完整源码 |
| `HeartVRChat.Core.1.3.0.nupkg` | 开发者使用的 .NET 8 核心库，同时发布到 GitHub Packages |
| `SHA256SUMS.txt` | 下载文件的 SHA-256 校验值 |

网页模式需要 Windows 10/11 x64、Edge 或 Chrome，以及可直接访问的文字式心率组件链接。数字连续 15 秒不变时暂停发送，变化后恢复。使用前请在 VRChat 开启 OSC 和聊天框 OSC 消息。

核心库的 GitHub NuGet 源：`https://nuget.pkg.github.com/HakumomoHacha/index.json`。使用方法见仓库 README。
