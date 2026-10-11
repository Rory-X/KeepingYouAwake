# KeepingYouAwake

Windows 11 中文托盘工具：防自动休眠、定时关机，以及供 AI / 脚本使用的本机 CLI。

当前版本：**0.1.0**。[下载 GitHub Release](https://github.com/Rory-X/KeepingYouAwake/releases/tag/v0.1.0)。发布包：`KeepingYouAwake-0.1.0-win-x64.zip`（Windows x64，自包含，无需安装 .NET）。

## 功能说明

从开始菜单打开 **KeepingYouAwake**，通过咖啡托盘图标控制电脑电源。

| 功能 | 说明 | 使用方式 |
|---|---|---|
| 咖啡托盘图标 | 防休眠生效时实心，否则镂空；悬停查看原因和剩余时间 | 左键打开控制面板，右键打开快捷菜单 |
| 持续防休眠 | 保持电脑运行，直到手动关闭；默认允许屏幕熄灭 | 控制面板选择“持续开启”，或使用 CLI |
| 限时防休眠 | 支持预设和自定义时长，到期自动恢复 Windows 电源策略 | 选择持续时长后开启，或使用 CLI |
| 保持屏幕亮起 | 防休眠时可选保持亮屏 | 勾选“同时保持屏幕亮起”，或使用 CLI |
| 定时关机 | 支持倒计时和指定日期时间；最后 60 秒可取消 | 在控制面板安排，或使用 CLI |
| AI / 脚本控制 | 支持防休眠、关机、取消、状态查询和 JSON 输出 | 使用 `kya`；后台未运行时自动启动 |
| 计划冲突处理 | 同时最多一个关机计划；新请求默认拒绝，支持显式替换 | 界面确认替换，或 CLI 加 `--replace` |
| 计划恢复 | 重开软件时恢复未到期计划；过期计划和中断的倒计时取消 | 自动处理；手动防休眠状态不跨进程重启恢复 |
| 后台常驻 | 关闭窗口后继续运行；主动退出会提示并取消关机计划 | 托盘菜单“退出软件”结束后台 |
| 登录自启动 | 登录 Windows 后启动，默认关闭 | 在“设置”中开启 |

关机计划支持倒计时或指定日期时间，同时最多一个。**设置的计划时间表示开始最终 60 秒关机倒计时的时间**，所以安排 30 分钟后关机时，实际请求发生在约 31 分钟后。倒计时期间可取消；默认不强制关闭程序，有未保存内容的程序仍可阻止 Windows 关机。CLI 成功表示请求已被接受，不代表电脑已经关机。

等待关机时自动防休眠。取消计划会释放该请求，但保留独立的手动防休眠。未到期计划保存在本机，软件重开时恢复。过期计划、重启时中断的倒计时，以及睡眠恢复时已经错过的计划都会取消，不补执行。主动退出会提示并取消计划。

Windows 的手动睡眠、合盖和临界电量保护仍生效。电池供电的 Modern Standby 设备可能限制防休眠请求；本工具不修改全局电源计划，也不承诺从睡眠中唤醒或完全关机后开机。

## CLI

### npm 安装与调用（Windows x64）

npm 包：[keeping-you-awake](https://www.npmjs.com/package/keeping-you-awake)，当前版本 `0.1.0`，命令为 `kya`。包内包含 CLI 和托盘程序，不需要安装 .NET；需要 Node.js 18 或更新版本及 Windows x64。

从 npm registry 安装或临时调用：

```powershell
npm install -g keeping-you-awake
kya status --json
# 不全局安装，临时调用：
npx --yes --package keeping-you-awake kya status --json
```

也可从 [GitHub Release 下载 npm 安装包](https://github.com/Rory-X/KeepingYouAwake/releases/download/v0.1.0/keeping-you-awake-0.1.0.tgz)，运行 `npm install -g ./keeping-you-awake-0.1.0.tgz`。从源码打包后的本地安装命令为 `npm install -g ./artifacts/npm/keeping-you-awake-0.1.0.tgz`。

npm 安装仅提供命令入口，不创建开始菜单入口或 Windows 卸载条目。首次控制命令会按需启动包内托盘程序；设置和日志仍保存在当前用户的本机目录。更新或卸载前，请先从托盘退出软件；卸载命令为 `npm uninstall -g keeping-you-awake`。如已安装桌面版，CLI 会连接同一用户下已运行的默认后台。

### GitHub Packages

已发布到 [GitHub Packages](https://github.com/Rory-X/KeepingYouAwake/pkgs/npm/keeping-you-awake)，包名为 `@rory-x/keeping-you-awake`，当前版本 `0.1.0`，关联到本仓库。安装时需先配置该作用域的 registry，并通过具有 `read:packages` 权限的 GitHub 个人访问令牌完成认证（GitHub Packages 的 npm registry 安装需要认证）：

```powershell
npm login --scope=@rory-x --auth-type=legacy --registry=https://npm.pkg.github.com
npm install -g @rory-x/keeping-you-awake@0.1.0 --registry=https://npm.pkg.github.com
kya --version
```

登录时 Username 为 GitHub 用户名，Password 为 GitHub 令牌。

生成 GitHub Packages 包：`./scripts/Build-Npm.ps1 -GitHubPackages`；已有发布程序时可加 `-SkipBuild`。产物为 `artifacts/npm/rory-x-keeping-you-awake-0.1.0.tgz`。使用 `./scripts/Test-Npm.ps1 -PackageName '@rory-x/keeping-you-awake'` 验证。仓库的 **Publish GitHub Packages** Actions 工作流会校验现有 Release 的 Windows 发布包、构建并测试作用域包，使用内置 `GITHUB_TOKEN` 发布到 GitHub Packages，再验证线上包的 SHA512 和 `npx` 调用。

### 桌面安装后的 CLI

安装会将 CLI 目录加入当前用户 PATH。请打开新的终端；当前终端也可调用 `%LOCALAPPDATA%\Programs\KeepingYouAwake\kya.exe`。

| 功能 | 说明 | CLI |
|---|---|---|
| 查看状态 | 查看防休眠、关机计划和最近事件 | `kya status` |
| AI 查询状态 | 返回 JSON，便于程序读取 | `kya status --json` |
| 持续防休眠 | 保持电脑运行，直到手动关闭 | `kya awake on` |
| 限时防休眠 | 示例：保持运行 2 小时，到期自动恢复 | `kya awake on --for 2h` |
| 同时保持亮屏 | 开启防休眠，并保持屏幕亮起 | `kya awake on --screen-on` |
| 允许屏幕熄灭 | 开启防休眠，但允许屏幕熄灭 | `kya awake on --screen-off` |
| 关闭手动防休眠 | 若还有关机计划，计划仍会保持电脑运行 | `kya awake off` |
| 发起关机 | 立即开始 60 秒可取消倒计时 | `kya shutdown` |
| AI 发起关机 | 同上，返回 JSON 结果 | `kya shutdown --json` |
| 倒计时关机 | 30 分钟后开始最后 60 秒倒计时 | `kya shutdown --after 30m` |
| 指定钟点关机 | 下一次本地时间 23:00 开始倒计时 | `kya shutdown --at "23:00"` |
| 指定日期关机 | 指定日期时间开始倒计时 | `kya shutdown --at "2026-10-12 23:00"` |
| 替换已有计划 | 显式覆盖当前关机计划 | `kya shutdown --after 1h --replace` |
| 取消关机 | 取消计划或正在进行的 60 秒倒计时 | `kya cancel` |
| 打开控制面板 | 显示桌面窗口 | `kya show` |
| 查看帮助 | 列出命令、参数和退出码 | `kya --help` |
| 查看版本 | 显示软件版本 | `kya --version` |

所有控制命令均可加 `--json`。若新终端尚未识别 `kya`，PowerShell 可使用完整路径：

```powershell
& "$env:LOCALAPPDATA\Programs\KeepingYouAwake\kya.exe" status --json
```

时长单位为 `s`、`m`、`h`、`d`；不带单位按分钟。`--at HH:mm` 指下一次该本地时间，完整日期可使用 ISO 8601 时区。CLI 未找到后台时会启动同目录托盘程序，并返回结构化结果和退出码。状态通过仅当前用户可访问的本机命名管道共享；CLI 与后台需使用同一权限级别。

AI 指令示例：

> 只有任务成功完成且无需我继续操作时，运行 `kya shutdown --json`。若返回冲突或失败，请报告并停止。需要取消时运行 `kya cancel --json`。不要自行覆盖已有计划。

`kya shutdown` 不带时间会立即开始 60 秒倒计时，不会立刻关闭电脑。没有强制关机选项。

JSON 顶层字段为 `success`、`code`、`message`、`state`。状态包含 `awake`、`manualAwake`、`scheduleAwake`、`awakeUntilUtc`、`shutdown`、`countdownSeconds`、`preferences`、`lastEvent`、`simulation`。时间使用带偏移的 ISO 8601 UTC 格式。查看 `state.lastEvent.kind` 可识别 `shutdown-failed` 或 `shutdown-submitted`；后者仅表示已交给 Windows。

| 退出码 | 含义 |
|---|---|
| `0` | 请求成功 |
| `2` | 参数错误 |
| `4` | 后台不可用 |
| `5` | 计划冲突或请求被拒绝 |
| `6` | 电源或持久化操作失败 |

## 安装与卸载

发布目录中的两个 EXE 为 Windows x64 自包含程序，无需用户安装 .NET。安装目标为 `%LOCALAPPDATA%\Programs\KeepingYouAwake`，设置和轮转日志在 `%LOCALAPPDATA%\KeepingYouAwake\data`。

使用 release 发布包：解压 `KeepingYouAwake-0.1.0-win-x64.zip`，在解压目录打开 PowerShell，运行以下命令安装（无需管理员权限）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

也可以直接运行解压目录中的 `KeepingYouAwake.exe`，并通过 `./kya.exe` 使用 CLI。便携运行不会创建开始菜单入口或添加 PATH。两个 EXE 需放在同一目录。可用 `Get-FileHash .\KeepingYouAwake-0.1.0-win-x64.zip -Algorithm SHA256` 与发布包旁的 `.zip.sha256` 文件核对。

从源码构建并安装：

```powershell
./scripts/Build.ps1
./scripts/Install.ps1
```

安装创建开始菜单入口、当前用户 CLI PATH，以及 Windows“已安装的应用”条目；自启动默认关闭。卸载可使用开始菜单的 Uninstall 或 Windows 设置；会取消关机计划并保留设置与日志。更新前先从托盘退出。

## 开发与验证

使用 .NET 10 SDK。`Build.ps1` 优先使用项目 `.tools/dotnet/dotnet.exe`，否则使用 PATH 中的 SDK。应用为 WPF + WinForms NotifyIcon，CLI 为独立控制台程序，核心状态机与平台接口分离。

运行 `./scripts/Build.ps1` 会执行核心测试、Release 构建和自包含发布，并生成 `artifacts/release/KeepingYouAwake-0.1.0-win-x64.zip` 及 SHA256 校验文件。ZIP 包含托盘程序、CLI、README、安装和卸载脚本；`artifacts/publish` 保留可直接运行的发布目录。`-SkipPublish` 仅执行测试和构建，不生成发布包。

生成 npm 包：`./scripts/Build-Npm.ps1`（包含完整构建），或在已有同版本发布程序时运行 `./scripts/Build-Npm.ps1 -SkipBuild`。产物为 `artifacts/npm/keeping-you-awake-0.1.0.tgz` 及 SHA256 校验文件。脚本从 `Directory.Build.props` 读取版本，并将两个自包含 EXE 打包；安装时无需额外下载程序。

验证 npm 包：`./scripts/Test-Npm.ps1`。该脚本在隔离目录安装 `.tgz`，验证 `kya` 命令、参数与退出码、模拟后台通信及 `npx` 调用，不操作真实电源。

发布到 npm registry：先运行 `npm login --registry=https://registry.npmjs.org`，再执行 `npm publish ./artifacts/npm/keeping-you-awake-0.1.0.tgz --access public --registry=https://registry.npmjs.org`。不要直接发布 `npm` 源码目录，该目录尚未包含程序文件。

```powershell
./scripts/Build.ps1 -SkipPublish
./.tools/dotnet/dotnet.exe run --project tests/KeepingYouAwake.Tests -c Release
```

| 验证方式 | 命令 / 参数 | 范围 |
|---|---|---|
| 核心测试 | `dotnet run --project tests/KeepingYouAwake.Tests -c Release` | 假时钟、内存仓库和模拟电源；覆盖倒计时、取消、冲突、重启、恢复和错误路径 |
| 实际进程测试 | `./scripts/Smoke-Test.ps1` | 隔离模拟实例，验证 CLI 通信和完整 60 秒倒计时，不调用真实电源接口 |
| 已安装程序验证 | `./scripts/Test-Installed.ps1` | 验证 CLI 自动启动及输出结束、真实的 2 秒防休眠请求、面板打开；不执行真实关机 |
| 界面渲染 | `--capture-preview <PNG绝对路径>` | 在隔离模拟实例渲染实际 WPF 界面并退出 |

手动调试模拟后台可使用 `KeepingYouAwake.exe --simulate --instance smoke --data-dir <隔离目录>`，CLI 使用 `--instance smoke`。模拟测试不调用真实电源接口。

真实关机通过 `InitiateSystemShutdownExW`，不强制关闭应用。60 秒取消窗口由后台管理，不使用会隐含强制关机的 `shutdown.exe /t`。防休眠调用线程始终由同一个 WPF dispatcher 持有；退出时释放请求。

本仓库不含云服务、远程控制入口或自动上传日志。当前发布包未做代码签名，提供本机安装脚本和便携运行方式。
