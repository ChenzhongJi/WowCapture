# WowCapture

[![build](https://github.com/ChenzhongJi/WowCapture/actions/workflows/build.yml/badge.svg)](https://github.com/ChenzhongJi/WowCapture/actions/workflows/build.yml)

> 按一下热键，0.2 秒出图；截完就能改、就能贴、就能发。

一个给自己用的 Windows 截图工具，同时也希望它对别人有用。
轻量、离线优先、无 Electron、无账号、无遥测。

*WowCapture is a lightweight Windows screenshot tool built with .NET 10 + WPF. Documentation is Chinese-first for now.*

## 现在是什么状态

**M0 骨架已跑通**：托盘常驻、单实例、日志与配置目录就绪。截图功能从 M1 开始。

| 能力 | 状态 |
|---|---|
| 托盘常驻 / 单实例 / 日志 / 数据目录探测与降级 | ✅ M0 |
| 配置存储（原子写 + 损坏自愈） | ✅ T0.4 |
| 全局热键（`Ctrl+Alt+A`，收 `WM_HOTKEY`） | ✅ T1.1 |
| 显示器拓扑 / DPI / 物理↔DIP 换算 | ✅ T1.2 |
| 整屏冻结帧捕获（GDI，P50 38.6ms @ 5.18MP） | ✅ T1.3 |
| 框选截图（压暗遮罩 / 拖选 / 方向键微调 / 回车确认） | ✅ T1.4 · T1.5 |
| 窗口吸附（悬停高亮整窗 + 标题，单击选中，`Ctrl` 关闭） | ✅ T1.6 |
| 复制到剪贴板（PNG + CF_DIB，带占用重试） | ✅ T1.7 部分 |
| 自动保存 / 命名模板 / 同名自增 / 按年月分目录 | ✅ T1.8 |
| 资源压测（60 次连拍，GDI/USER 零泄漏） | ✅ T1.9 |
| 标注：矩形 / 椭圆 / 直线 / 箭头 / 画笔 / 荧光笔 / 马赛克 / 文字 / 序号 | ✅ M2 完成 |
| 标注工具栏（工具 / 四色 / 撤销重做 / 贴图 / 确认 / 取消）与合成导出 | ✅ T2.8 · T2.9 |
| 贴图 Pin（拖动 / 滚轮缩放 / 透明度 / 点击穿透 / 右键菜单 / 关闭全部） | ✅ T3.1 · T3.2 |
| 截图后动作（复制 / 保存 / 贴图 / 不处理，`settings.json` 可配） | ✅ T3.3 |
| 开机自启、便携模式、首次运行引导 | ✅ T3.4 · T3.5 |
| 多应用粘贴矩阵（微信/Office/Chrome…） | ⬜ 需人工过一遍 |
| 设置窗口（热键 / 截图后动作 / 输出目录 / 自启） | ✅ 托盘菜单「设置…」，保存即生效 |
| 长截图 / OCR / 取色器 | ⬜ V1.1 |

**现在就能用**：`Ctrl+Alt+A` 打开遮罩 → 鼠标悬停可吸附整个窗口（标题显示在左上角），或直接拖动框选；
框好后选区内拖动画标注，选区上方会出现工具栏。`Enter` 复制到剪贴板，`Esc` 取消。

工具栏：6 个工具 + 4 种颜色 + 撤销 / 重做 / 确认 / 取消。键盘也都能用：

| 按键 | 作用 |
|---|---|
| `A` / `R` / `E` / `L` / `P` / `H` / `M` / `T` / `N`（或 `1`–`9`） | 箭头 / 矩形 / 椭圆 / 直线 / 画笔 / 荧光笔 / 马赛克 / 文字 / 序号 |
| `Ctrl+Z` / `Ctrl+Y`（或 `Ctrl+Shift+Z`） | 撤销 / 重做 |
| `F3` 或工具栏图钉 | 把选区贴到屏幕上（不进剪贴板） |
| 方向键（`Shift` ×10） | 微调选区 |
| `Enter` / 双击 | 确认并复制 |
| `Esc` / 右键 | 取消 |

贴图窗口：拖动移动 · 滚轮缩放（以光标为锚点）· `Alt+滚轮` 调透明度 · 双击复位 100% · 右键复制/另存/关闭 · `Esc` 关闭。
`Alt+单击` 开启点击穿透（贴图变成纯对照板，鼠标直接落到下方窗口），再用托盘菜单"取消贴图穿透"恢复。
托盘菜单里有"关闭全部贴图"。

## 跑起来

要求：Windows 10 2004 或更高、.NET SDK 10.0.301+

```powershell
dotnet build
dotnet run --project src/WowCapture.App
```

托盘出现图标；右键菜单的「截图 / 设置… / 打开截图目录 / 打开数据目录」全部可用。
「设置…」里可以改热键（当场重注册）、截图后动作、输出目录与开机自启，保存即生效。

无界面自检（完整走一遍启动流程后立刻退出，便于脚本验证）：

```powershell
dotnet run --project src/WowCapture.App -- --selftest
```

抓屏性能基准（S1）：跑两种 `BitBlt` 模式各 N 次，输出 P50/P95、BitBlt 与拷贝耗时拆分、
外推双 4K 的估算，并落一张样张 `bench-sample.png` 供肉眼校验。

```powershell
dotnet run --project src/WowCapture.App -c Release -- --bench-capture 40
```

环境诊断（S2）：打印显示器拓扑、DPI、每屏的遮罩窗口摆放与物理↔DIP 往返自检、热键注册结果，
并用合成 `WM_HOTKEY` 验证消息通道。有问题时把它贴进 issue。

```powershell
dotnet run --project src/WowCapture.App -c Release -- --diagnose
```

遮罩自检：无人值守跑一遍"冻结帧 → 遮罩 → 选区 → 裁剪 → 剪贴板"，并用像素断言校验
边框色与压暗是否真的画在屏幕上，产出 `overlay-shot.png`（含遮罩的整屏）与 `overlay-result.png`（裁剪结果）。

```powershell
dotnet run --project src/WowCapture.App -c Release -- --overlay-test
```

资源压测：连续抓屏 N 次，报告 GDI/USER 对象与内存增量，用来确认没有句柄泄漏。

```powershell
dotnet run --project src/WowCapture.App -c Release -- --stress 60
```

吸附自检：造一个已知位置与标题的探针窗口，验证"找得到、排得掉、画得准"（寻找器、进程排除、高亮边框像素）。

```powershell
dotnet run --project src/WowCapture.App -c Release -- --snap-test
```

标注自检：程序化加矩形/箭头/画笔，验证撤销、屏幕渲染、以及**导出合成后的像素**（尺寸与颜色都断言）。

```powershell
dotnet run --project src/WowCapture.App -c Release -- --annotate-test
```

## 三条设计主线

1. **冻结帧**：按下热键先把整屏抓成一张静态图，遮罩窗口显示的是这张图而不是实时桌面。
   遮罩因此可以完全不透明，绕开 WPF 透明窗口的软件渲染、闪烁和"抓到自己"的问题。
2. **每屏一个遮罩窗口**：跨 DPI 的超大窗口会被系统在合成层缩放，导致模糊与选区偏移；
   每屏一个窗口各自 1:1 渲染，坐标只做平移。
3. **标注不可变、马赛克非破坏**：撤销只是换一个标注列表快照，不复制位图；
   马赛克在合成时采样底图，随时可撤销、导出不受二次损失。

## 文档

| 文档 | 内容 |
|---|---|
| [docs/architecture.md](docs/architecture.md) | 技术选型、8 条关键决策、模块依赖、DPI 铁律、性能预算、已知风险 |
| [docs/roadmap.md](docs/roadmap.md) | 版本规划、完整功能矩阵、明确的非目标 |
| [docs/mvp-tasks.md](docs/mvp-tasks.md) | 21 人日的任务拆分、可演示的验收标准、15 条陷阱清单 |

## 目录

```
src/
  WowCapture.App/        WPF 宿主：托盘、单实例、日志，后续的遮罩与设置窗口
  WowCapture.Core/       纯领域层：几何、帧模型、接口（不依赖 WPF / Win32）
  WowCapture.Platform/   Win32 互操作（RECT/POINT、DPI、热键、捕获相关）
  WowCapture.Capture/    抓屏实现：GDI → PrintWindow → WGC 分级回退
  WowCapture.Editor/     标注画布与工具（WPF 类库，可单独单测）
docs/
```

依赖方向严格单向：`App → {Capture, Editor, Platform} → Core`。Core 只依赖 BCL，因此能在任何机器上跑单元测试。

## 文件都在哪

数据目录按顺序探测，**第一个可写的生效**：

1. 便携模式（exe 同级放了 `portable.flag`）：`<exe 目录>\data\`
2. 默认：`%LOCALAPPDATA%\WowCapture\`（资源管理器地址栏可直接粘贴这串）
3. 受限环境回退：`%TEMP%\WowCapture\` → `<exe 目录>\data\`

| 文件 | 位置 |
|---|---|
| 配置 | `settings.json`（日常项用托盘「设置…」改，保存即生效；高级项手改后需重启） |
| 日志 | `logs\app-yyyyMMdd.log` |
| 截图输出 | 默认 `图片\WowCapture\yyyy-MM\`，可用 `output.directory` 改 |

最快的定位方式：`WowCapture.exe --open-data` 直接打开数据目录；托盘右键菜单里也有"打开数据目录"。
日志第一行会写明本次实际使用的数据目录。

> **Windows 11 的托盘图标默认收在"^"溢出区里**，看起来就像"没有图标"。
> 想让它常驻：设置 → 个性化 → 任务栏 → 其他系统托盘图标，把 WowCapture 打开；
> 或者从"^"溢出区把它拖到任务栏上。

### 托盘图标不显示？

按顺序排查：

1. **确认进程在跑**：任务管理器里找 `WowCapture.exe`。
2. **先看溢出区**：Win11 默认把新图标收进 `^`（见上一条）。
3. **不要从沙箱/服务会话启动**。受限令牌下 `Shell_NotifyIcon` 会静默失败（图标根本不注册），
   同时 `%LOCALAPPDATA%` 也不可写，数据目录会回退到 exe 同级 `data\`。
   日志首行会写明：
   `运行环境：session=1，任务栏=True，数据目录回退=True，首选目录=...` 并跟一条 `[WRN]`。
   正确做法：在**资源管理器里双击 exe**，或从普通终端启动。
4. **跑诊断**：`WowCapture.exe --diagnose`，看输出里的 `任务栏=`、`数据目录回退=` 两行，
   报告文件在数据目录下的 `diagnose.txt`。
5. **别把程序放在被沙箱/安全软件标记的目录里运行**。实测同一份 exe：
   放在某些受控目录（例如一键沙箱的工作区）里启动时令牌受限 —— `%LOCALAPPDATA%` 写不进去、
   `Shell_NotifyIcon` 注册失败，表现为"没有托盘图标、设置不生效"；
   复制到普通目录（如 `%LOCALAPPDATA%\Apps\WowCapture`）后立即恢复。
   发布用 `scripts\publish.ps1`，它会发布到普通目录并刷新桌面快捷方式。

## 技术栈

.NET 10（LTS，支持至 2028）/ WPF / Win32 P/Invoke / xUnit / MIT

## 参与

MVP（M0–M3）已完成，V1.0（截图历史、深色主题、安装包与首发）进行中。
想练手可以从 [docs/mvp-tasks.md](docs/mvp-tasks.md) 的 good first issue 清单挑：应用图标、深色主题配色、英文翻译、README 演示 GIF。

## 许可

[MIT](LICENSE)
