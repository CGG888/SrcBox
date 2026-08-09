# 开发指南

> 完整的项目架构和技术细节请参阅 [完整架构文档](/ARCHITECTURE.md)。

## 环境依赖

- **操作系统**：Windows 10 / 11 (x64)
- **开发工具**：Visual Studio 2022 或 JetBrains Rider
- **SDK**：.NET 8.0 SDK
- **依赖库**：`libmpv-2.dll` (必须手动放置在输出目录)

## 编译与运行

```powershell
# 还原依赖
dotnet restore

# 编译（Debug）
dotnet build

# 运行
dotnet run
```

> **注意**：运行前请确保 `libmpv-2.dll` 已放置在 `bin\Debug\net8.0-windows\` 目录下，否则程序会闪退或报错。

## 故障排查

| 现象 | 可能原因 | 解决方案 |
| :--- | :--- | :--- |
| **程序启动即崩溃** | 缺少 `libmpv-2.dll` | 下载对应架构的 dll 放入运行目录 |
| **有画面无声音** | 音频流探测超时 | 属正常优化策略，可尝试切换音轨或重启播放 |
| **EPG 显示“无数据”** | 网络问题或格式不支持 | 检查 XMLTV URL 是否可访问，是否为 GZIP 格式 |
| **设置不保存** | 权限不足 | 确保程序目录有写入权限 |

## 测试与贡献

### 贡献流程

1. **Fork** 本仓库。
2. 创建特性分支：`git checkout -b feature/AmazingFeature`。
3. 提交代码：`git commit -m 'feat: Add some AmazingFeature'` (请遵循 [Conventional Commits](https://www.conventionalcommits.org/))。
4. 推送分支：`git push origin feature/AmazingFeature`。
5. 提交 **Pull Request**。

### 代码规范

- 保持现有的 C# 代码风格（K&R / Allman 混合，视文件而定，建议遵循 .editorconfig）。
- UI 修改请注意深色/浅色主题适配。

### 性能基准

- **CPU 占用**：1080p 播放时应 < 15% (i5-8250U 基准)。
- **内存占用**：稳定播放时应 < 500MB。
- **启动时间**：冷启动 < 2秒。
