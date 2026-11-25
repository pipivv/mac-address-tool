# v2.0.0

如果启动时提示缺少 .NET Framework 4.8，请安装微软官方运行时，再打开 `MacAddressTool.exe`。[在线安装](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/thank-you/net48-web-installer) · [离线安装包](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/thank-you/net48-offline-installer)

- 重新设计 Windows 原生界面，参照 iOS Liquid Glass 的材质层级和交互反馈。
- 加入明暗外观、按压反馈、分段选择器过渡、开关动画及减少动态效果选项。
- 保留一屏显示、三个字号按钮和厂商分页。
- 修正大字号分段按钮和 MAC 输入框的文字裁切；检查三档字号与五种窗口尺寸。
- 使用简洁的窗口控制图标和外观、动效文字按钮；深色主题采用炭灰配色与完整的浅色文字。
- 厂商库后台加载，默认只显示常用厂商，搜索界面最多载入 100 项。
- 保留按厂商前缀生成、完整厂商随机生成、本地管理地址、应用和恢复功能。
- 采用通用演示数据，移除含真实网卡信息的旧版预览与诊断输出入口。
- 提供源码、构建脚本、测试入口、静态隐私检查及单文件 EXE。

支持 Windows 10 / 11 x64。实际 MAC 覆盖能力取决于网卡驱动。发布版未进行代码签名。
