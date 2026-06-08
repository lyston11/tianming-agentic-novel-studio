# 天命 Agentic Novel Studio

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

> AI 不会天然记得一本千万字小说。这个项目做的事情是：把故事变成系统能管理的数据，让 Agent 按状态、账本和创作约束推进长篇小说。

当前仓库已移除旧 WPF 桌面壳，主线是 **ASP.NET Core Web 工作台 + NovelAgent 纯逻辑服务 + 回归测试**。

## 核心机制

每一章都应围绕闭环推进，而不是只生成一段正文：

```text
故事地基 → 卷级规划 → 章节候选 → 用户确认 → 正文生成 → 生成后复盘 → 账本沉淀 → 下一章读取最新状态
```

NovelAgent 重点维护这些长期状态：

- Story Bible：整书创意宪法、类型承诺、禁区、Canon Ledger。
- 卷级规划：卷承诺、节拍、反转、高潮、伏笔投放/回收计划。
- 章节创意简报：多个候选、商业节奏、重复桥段风险、RAG 相似片段。
- 伏笔账本：Planned、Setup、Reinforced、Due、PaidOff 等状态。
- 角色账本：目标、秘密、关系、能力代价、心理压力。
- 生成后复盘：Proposed Canon、伏笔变化、角色状态变化、质量分和下一章建议。

## 项目结构

```text
Web/NovelAgentWeb/              ASP.NET Core Web API + 静态站点
Web/NovelAgentWeb.Frontend/     React + TypeScript + Vite 前端
Services/Framework/AI/NovelAgent/
                                NovelAgent 核心模型、编排器和纯逻辑服务
Tests/NovelAgentRegression/     不依赖 WPF 的核心回归测试
Tests/Fixtures/NovelAgent/      示例小说回归数据
Docs/                           当前工程质量和 TODO 文档
Storage/                        项目数据和服务配置
```

## 快速开始

构建 Web 后端：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet build Web/NovelAgentWeb/NovelAgentWeb.csproj
```

启动 Web 工作台：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet run --project Web/NovelAgentWeb/NovelAgentWeb.csproj
```

构建前端：

```bash
cd Web/NovelAgentWeb.Frontend
npm run build
```

运行核心回归测试：

```bash
/opt/homebrew/opt/dotnet@8/libexec/dotnet run --project Tests/NovelAgentRegression/NovelAgentRegression.csproj
```

## 当前状态

- Web 工作台可构建运行。
- NovelAgent 核心回归测试覆盖故事地基、卷规划、章节规划、章节执行、生成后复盘、Canon/伏笔/角色账本和 RAG 相似片段。
- Web 运行时仍包含可替换 writer / reviewer / guide context adapter，后续主任务是接入真实项目章节库和真实生成链路。
- 旧 WPF 桌面入口、桌面 UI 框架、历史桌面功能模块和历史版本副本已删除。

## 许可证

[MIT License](LICENSE)

> 商用须知：本项目代码基于 MIT 协议开源，但任何商业用途（包括但不限于二次包装售卖、商业 SaaS 部署、嵌入付费产品等）请先联系原作者获得授权。
