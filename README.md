# 墨读 Markdown

轻量、离线、免安装的 Windows Markdown 阅读与编辑器。打开文档即可阅读，也可以切换到分栏视图，一边编辑，一边查看排版效果。

**[下载最新便携版](https://github.com/Nanamiwww/modu-markdown/releases/latest)** · **[使用说明](使用说明.md)** · **[反馈问题](https://github.com/Nanamiwww/modu-markdown/issues)**

![墨读 Markdown 界面示意](interface.png)

*界面示意图来自 v2.0 随包文档。*

## 下载与使用

1. 打开上方的下载页面，在 **Assets** 中下载 `modu-markdown-v2.0.0-windows-portable.zip`。
2. 将压缩包完整解压到一个文件夹。
3. 双击 `CodexMarkdownReader.exe` 启动。
4. 点击“打开文件”，或把 `.md` 文件拖入窗口。随包的 `示例文档.md` 可以用于快速上手。

GitHub 自动生成的 `Source code (zip)` / `Source code (tar.gz)` 是仓库文档归档；运行软件请下载上述便携版压缩包。

## 主要功能

- **三种视图**：源码、分栏、预览，可按 `Ctrl+1` / `Ctrl+2` / `Ctrl+3` 切换。
- **阅读导航**：标题大纲、最近文件、查找、缩放、阅读宽度，以及浅色和深色主题。
- **Markdown 排版**：表格、嵌套列表、任务列表、代码块、脚注、提示块和常用 LaTeX 数学公式。
- **文件变化提示**：文档被其他程序修改时自动刷新；存在未保存内容时提示处理冲突。
- **编码与草稿**：保留原文件编码和换行方式，支持自动草稿及异常退出后的恢复。
- **导出与打印**：导出含内嵌图片和公式的单文件 HTML；通过 Windows 打印对话框导出 PDF。
- **本地文档阅读**：软件可离线使用，远程图片不会自动加载。

完整操作与快捷键见[使用说明](使用说明.md)。

## 运行环境

- 推荐 64 位 Windows 10 / Windows 11。
- 需要 .NET Framework 4.8。
- 便携运行，无需安装；复制整个解压后的文件夹即可迁移。

程序尚未进行商业代码签名，下载后 Windows 可能显示安全提醒。请核对下载来源与文件校验值。

## 使用边界

数学公式使用内置离线排版器，支持常用公式写法，但不支持完整 TeX、宏定义、矩阵环境或第三方宏包。PDF 导出通过打印对话框中的 **Microsoft Print to PDF** 完成。

本仓库当前提供软件发行包和使用文档，暂未提供程序源码。

## 文件校验

v2.0.0 便携包的 SHA-256：

```text
C9C3F8B662C2AA3F2BF9CFA2A456DBCDB3D9318A13EA7CBE9A9539E25F4DECF1
```

发行页面同时提供 `SHA256SUMS.txt`。校验值可用于确认下载文件与发布文件一致。

## 问题反馈

欢迎在 [Issues](https://github.com/Nanamiwww/modu-markdown/issues) 中提供软件版本、Windows 版本、复现步骤，以及去除私人信息后的最小示例文档。
