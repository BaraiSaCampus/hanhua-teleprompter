# 汉化提词器

一个 Windows 10/11 x64 便携式全局提词工具。导入 TXT、DOCX 或 DOC 后，启用状态下在其他程序中按 `Ctrl+V` 输出下一行，按 `Ctrl+C` 回退一行。

## 使用

1. 运行 `提词器新版.exe`，选择或拖入文件。
2. 导入后程序会自动启用；把光标放到目标程序输入位置。
3. 按 `Ctrl+V` 输出下一条，按 `Ctrl+C` 回退；需要普通复制粘贴时先点“暂停”。
4. 展开窗口可编辑、删除、插入、排序队列或指定下一条。

程序不会覆盖系统中原有的 `提词器.exe`。运行进度与窗口设置保存在新版 EXE 旁的 `data` 目录；复制程序时一并复制该目录即可带走进度。

## 构建

需要 .NET 10 SDK：

```powershell
dotnet test .\提词器新版.slnx -c Release
dotnet publish .\src\Teleprompter\Teleprompter.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

## 第三方组件

- DocSharp.Binary.Doc 与 DocSharp.Docx：MIT License，用于离线解析 Word DOC/DOCX。
- .NET Runtime：随 self-contained 构建一并发布，遵循其对应许可。
