# OpenCode MCP & Skills 使用指南

> 本文档说明 SrcBox 和 Channel Sentinel 项目的 OpenCode 配置

---

## 目录

- [MCP 配置](#mcp-配置)
- [Skills 配置](#skills-配置)
- [快速使用](#快速使用)

---

## MCP 配置

### 概述

MCP (Model Context Protocol) 为 OpenCode 提供外部工具扩展能力。

### 配置文件

- 全局配置：`~/.config/opencode/opencode.json`
- 项目配置：`opencode.json` (项目根目录)

### 当前 MCP 列表

| MCP | 状态 | 用途 |
|-----|------|------|
| github | ✅ | GitHub 代码管理、PR、Issues |
| filesystem | ✅ | 文件操作、读写文件 |
| memory | ✅ | 知识图谱持久记忆 |
| context7 | ✅ | 搜索最新文档 |
| sequentialthinking | ✅ | 复杂问题推理 |

### 配置文件内容

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "github": {
      "type": "local",
      "command": ["npx", "-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "{env:GITHUB_TOKEN}"
      }
    },
    "filesystem": {
      "type": "local",
      "command": ["npx", "-y", "@modelcontextprotocol/server-filesystem", "."]
    },
    "memory": {
      "type": "local",
      "command": ["npx", "-y", "@modelcontextprotocol/server-memory"]
    },
    "context7": {
      "type": "remote",
      "url": "https://mcp.context7.com/mcp"
    },
    "sequentialthinking": {
      "type": "local",
      "command": ["npx", "-y", "@modelcontextprotocol/server-sequential-thinking"]
    }
  }
}
```

### MCP 详细说明

#### 1. GitHub MCP

**用途**：直接与 GitHub 交互，管理仓库、Issues、PRs

**命令示例**：
```
列出 CGG888/srcbox 的所有 issues
查看 srcbox 最近的 commits
创建 CGG888/srcbox 的新分支
```

#### 2. Filesystem MCP

**用途**：安全读写本地文件

**命令示例**：
```
读取 SrcBox/Models/Channel.cs 文件
列出 Services 目录下的所有文件
```

#### 3. Memory MCP

**用途**：知识图谱持久记忆，跨会话记住重要信息

**命令示例**：
```
记住 SrcBox 使用 libmpv 播放内核
记住 Channel Sentinel 使用 Express 框架
查询之前记住的项目架构信息
```

#### 4. Context7 MCP

**用途**：搜索最新文档，获取准确的技术参考

**命令示例**：
```
用 context7 搜索 WPF 数据绑定最佳实践
用 context7 查找 Node.js Express 中间件写法
搜索 libmpv 最新 API 文档
```

#### 5. Sequential Thinking MCP

**用途**：复杂问题的分步推理，解决需要多步骤思考的问题

**命令示例**：
```
用 sequential thinking 分析为什么频道切换慢
用 sequential thinking 设计新功能的架构
```

---

## Skills 配置

### 概述

Skills 是可复用的行为定义，存放在 `~/.config/opencode/skills/` 目录。

### 当前 Skills 列表

| Skill | 用途 |
|-------|------|
| git-release | Git 发布和 Changelog 生成 |
| code-review | 代码审查清单 |
| write-tests | 编写单元/集成测试 |
| build-project | 项目构建命令 |
| pr-description | PR 描述模板 |

### Skill 文件结构

```
~/.config/opencode/skills/
├── git-release/SKILL.md
├── code-review/SKILL.md
├── write-tests/SKILL.md
├── build-project/SKILL.md
└── pr-description/SKILL.md
```

### Skill 详细说明

#### 1. git-release

**用途**：标准化发布流程

**使用方式**：
```
使用 git-release skill 帮我准备 1.1.7 版本发布
使用 git-release 生成 CHANGELOG
```

**功能**：
- 从 PR 和 commits 生成发布说明
- 建议版本号（语义化版本）
- 生成 `gh release create` 命令

#### 2. code-review

**用途**：标准化代码审查

**使用方式**：
```
使用 code-review skill 审查 MainWindow.xaml.cs
使用 code-review 检查这个 PR 的代码质量
```

**审查清单**：
- 代码质量（可读性、错误处理）
- 安全性（注入防护、敏感信息）
- 性能（内存泄漏、异步使用）
- 测试覆盖率

#### 3. write-tests

**用途**：生成测试用例

**使用方式**：
```
使用 write-tests skill 为 M3UParser 写测试
使用 write-tests 生成 SrcBox 的测试覆盖
```

**支持的测试框架**：
- SrcBox: MSTest (C#)
- Channel Sentinel: Jest (Node.js)

#### 4. build-project

**用途**：项目构建命令参考

**使用方式**：
```
使用 build-project skill 构建 SrcBox
使用 build-project skill 构建 Channel Sentinel
```

**构建命令**：

**SrcBox (C# / WPF)**：
```bash
# 还原依赖
dotnet restore

# 构建 Debug
dotnet build

# 构建 Release
dotnet build -c Release

# 运行测试
dotnet test ./Tests/LibmpvIptvClient.Tests.csproj

# 发布
dotnet publish -c Release -r win-x64 --self-contained
```

**Channel Sentinel (Node.js)**：
```bash
# 安装依赖
npm ci

# 开发运行
npm start

# 生产构建
npm run build

# 运行测试
npm test
```

#### 5. pr-description

**用途**：生成标准化 PR 描述

**使用方式**：
```
使用 pr-description skill 生成 PR 描述
帮我写一个包含测试计划的 PR 描述
```

---

## 快速使用

### 1. 启动 OpenCode

```bash
cd C:\Users\超哥哥\Documents\GitHub\IPTV-Player
opencode
```

### 2. 基本对话示例

```
# 询问项目问题
SrcBox 的 M3U 解析逻辑在哪里？

# 使用 MCP
用 context7 搜索 WPF mvvm 模式最佳实践
列出 srcbox 仓库的所有分支

# 使用 Skill
使用 build-project 帮我构建项目
使用 code-review 审查这段代码

# Git 操作
帮我创建 feature-new-feature 分支
提交所有更改，消息是 'feat: 添加新功能'
```

### 3. 项目特定示例

**SrcBox 项目**：
```
用 context7 搜索 libmpv WPF 集成方案
使用 write-tests 为 EpgService 写测试
帮我审查 MpvPlayer.cs 的代码质量
```

**Channel Sentinel 项目**：
```
用 context7 搜索 Express 中间件设计模式
使用 build-project 构建 Docker 镜像
帮我审查 src/index.js 的错误处理
```

### 4. 复杂任务示例

```
# 使用 sequential thinking 分析问题
用 sequential thinking 分析频道切换延迟的原因

# 结合多个工具
1. 用 github 查看最近的 issues
2. 用 context7 搜索解决方案
3. 用 write-tests 写测试验证修复
```

---

## 环境变量

### GITHUB_TOKEN

用于 GitHub MCP 认证

**创建 Token**：
1. 打开 https://github.com/settings/tokens
2. 点击 Generate new token (classic)
3. 勾选 `repo` 权限
4. 生成并复制 Token

**设置环境变量** (Windows PowerShell)：
```powershell
[Environment]::SetEnvironmentVariable("GITHUB_TOKEN", "ghp_xxx...", "User")
```

**验证**：
```powershell
powershell -Command "[Environment]::GetEnvironmentVariable('GITHUB_TOKEN', 'User')"
```

---

## 故障排查

### MCP 连接失败

```bash
# 查看 MCP 状态
opencode mcp list

# 调试特定 MCP
opencode mcp debug <mcp-name>
```

### Skill 不显示

1. 确认 `SKILL.md` 文件名大小写正确
2. 确认 frontmatter 包含 `name` 和 `description`
3. 确认 skill 路径在正确位置

### 环境变量不生效

需要**重新打开 PowerShell 窗口**或**重启 OpenCode**

---

## 更多资源

- [OpenCode 官方文档](https://opencode.ai/docs)
- [MCP Registry](https://registry.modelcontextprotocol.io)
- [MCP GitHub 服务器列表](https://github.com/modelcontextprotocol/servers)

---

*本文档由 OpenCode 自动生成*
