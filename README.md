# VGN F1 Pro Max 托盘电量与 DPI 工具

这是一个 Windows 托盘程序：它会枚举 VGN Dragonfly F1 系列的 HID 设备。当前这台电脑实际枚举到的是 `VID_3554&PID_F503`；其他 F1/接收器版本也常见 `391D:1005` 或 `391D:1A05`，程序已同时兼容这些标识。

当前 UI 已按 Windows 10 风格完成：

- 固定白色矩形电池图标，电量区域按实时电量从左向右填充。
- 鼠标悬停显示“电量：百分比”，右键菜单显示百分比电量条。
- 右键菜单顶部是独立电量条；下面是 DPI 子菜单和“移动同步”勾选/叉选项。
- 菜单有刷新、日志目录和退出项。

## 需要安装什么

你已经安装了 **.NET 8 SDK（Windows x64）**，不需要再安装 VGN HUB 客户端。官方 Web Hub 只需在首次连接/抓取报文时打开；日常运行托盘程序不依赖网页。

官方入口：

- Visual Studio：https://visualstudio.microsoft.com/zh-hans/vs/community/
- .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0
- VGN 官方 Web Hub：https://hub.vgnlab.com/

## 构建和运行

在本目录打开 PowerShell：

```powershell
dotnet restore
dotnet run
```

构建 Windows Release 版本（需要 .NET 8 Desktop Runtime）：

```powershell
dotnet build -c Release
```

本项目不依赖第三方 NuGet 包。Release 输出中的 `VgnTrayBattery.exe` 可双击启动，需与同目录的 DLL 和 JSON 文件放在一起，并要求安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。

## 当前状态

F1 Pro Max 的普通鼠标输入是标准 HID。你的接收器为 `3554:F503`；网页连接后暴露 Nordic52 电量接口 `FF02:0002`（17 字节报告）。程序带有 HID 读写超时保护。

当前已适配你的 2.4G 接收器：电量读取、400/800/1600 DPI 和移动同步均已接通。不同型号或不同接收器可能需要重新抓取 HID 报文。

程序启动后会把日志写入 `%LOCALAPPDATA%\VgnTrayBattery\logs\vgn-tray.log`。
