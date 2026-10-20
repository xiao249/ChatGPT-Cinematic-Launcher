# Codex 电影感启动动画与应用界面皮肤衔接

**ChatGPT-Cinematic-Launcher** 是适用于 Windows Codex 桌面客户端的社区启动器，提供电影感启动动画，以及从动画进入已设置皮肤的聊天页面的过渡。本项目不属于 OpenAI 官方产品。

- **启动动画：** 红蓝扫光、蛛网、雨滴和粒子效果，保留跳过按钮与 Esc。
- **进入页面：** 动画与实际聊天窗口对齐，等窗口准备好后，通过 DWM 实时画面融合接到应用界面。
- **页面皮肤：** 由另行安装的 [Codex Dream Skin](https://github.com/Fei-Away/Codex-Dream-Skin) 加载。本仓库提供启动和衔接代码，未捆绑主题引擎或独立聊天客户端。

**公开仓库只提供源码，不包含角色图片、城市背景、照片或预览截图。** 首次构建会在本地生成抽象红蓝背景；生成图片也不会提交到仓库。应用内皮肤和背景由使用者通过 Codex Dream Skin 自行设置。

**兼容范围：** 目前针对包系列 `OpenAI.Codex_2p2nqsd0c76g0`、包内 `app/ChatGPT.exe` 的 Windows x64 桌面客户端。普通 ChatGPT 安装包、网页版和其他系统不在支持范围内；相同显示名称不代表相同发行版。

## 环境与依赖

1. 安装兼容的官方桌面客户端并登录。
2. 从 [Fei-Away/Codex-Dream-Skin](https://github.com/Fei-Away/Codex-Dream-Skin) 安装 Codex Dream Skin，先配置主题并确认其能正常启动。本项目基于 v1.5.21 的接口开发。
3. Windows 10/11 x64、.NET Framework 4.x（含 WPF 桌面组件）及 Windows PowerShell 5.1。

上游应安装在当前用户的 `%LOCALAPPDATA%\CodexDreamSkin\engine`。本项目不捆绑上游引擎、Node、主题或官方客户端，不修改官方应用文件或 WindowsApps 权限。

## 构建与安装

把源码放到自己可写且长期保留的目录，在根目录打开 Windows PowerShell：

```powershell
.\Build.ps1
.\Install-Shortcut.ps1
```

程序生成在 `dist/`，桌面新增 `ChatGPT Cinematic Launcher` 快捷方式。安装脚本遇到同名入口会停止，不覆盖已有快捷方式；可指定新名称：

```powershell
.\Install-Shortcut.ps1 -Name '我的电影启动器'
```

从新快捷方式启动才会显示动画。直接打开系统原入口不会经过本项目。移动源码目录后需要重新创建快捷方式。删除本项目目录和新建快捷方式即可移除启动器；上游主题工具另行管理。

如执行策略阻止下载的脚本，请先审查源码与来源，按自己的受信任软件流程处理。本项目不要求管理员权限，不永久修改执行策略。

## 预览与检查

以下操作不会启动或关闭官方客户端：

```powershell
.\dist\ChatGPT.exe --preview
# 输出路径需要是绝对路径
.\dist\ChatGPT.exe --render-frame 3.5 "$PWD\dist\preview.png"
```

依赖检查（不主动启动客户端）：

```powershell
.\dist\ChatGPT.exe --preflight
```

正常启动等待真实窗口就绪，再对齐边界并以实时窗口画面融合进入界面。DWM 合成不可用时会直接交接到已就绪窗口。需要重新打开既有客户端时会提示，请先保存工作。`--restart` 明确请求重新打开现有客户端，不用于离线预览。

冷启动时间取决于官方客户端、主题初始化和电脑状态；动画不能缩短底层客户端的加载时间。远程桌面、DWM、显卡驱动和应用更新均可能影响过渡效果。

## 自定义图片

仓库不分发图片。首次运行 `Build.ps1` 时会自动生成本地 `assets/artwork-v2.jpg`，画面为代码绘制的抽象红蓝光带。也可以手动重新生成：

```powershell
.\scripts\Generate-DefaultArtwork.ps1
.\Build.ps1
```

可以在自己电脑上用有权使用的图片替换 `assets/artwork-v2.jpg`，再构建；只影响启动动画，不改变应用内背景。图片文件已加入 `.gitignore`，不要强制添加到公开仓库。

布局位于 `assets/Splash.xaml`，动画时间线和融合在 `src/CinematicSplash.cs`，启动与身份验证在 `src/Launcher.cs`。

## 日志与隐私

运行日志只保存在本地 `dist/logs/`，不会自动上传。日志可能含本地路径、窗口/进程编号和主题标识，分享前请先检查脱敏。不要提交用户主题、浏览器配置、状态文件、快捷方式、截图、聊天、账号或凭据。`.gitignore` 已排除构建产物与运行日志。

公开范围仅为源码、构建/安装脚本和说明，不包含图片、聊天截图、账号、凭据或本地运行记录。自行构建的程序未签名。

## 来源与许可

本项目程序源码采用 [MIT License](LICENSE)。主题引擎属于独立上游 [Codex Dream Skin](https://github.com/Fei-Away/Codex-Dream-Skin)，依赖说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。OpenAI、ChatGPT、Codex 名称属于各自权利人，仅用于说明兼容目标，不表示官方认可。
