# DbOptimizer 设计文档索引

**项目名称**：DbOptimizer - AI 驱动的数据库性能优化平台  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 文档说明

本文档是 DbOptimizer 设计文档的索引页面。详细设计内容已拆分为多个专项文档，便于维护和查阅。

---

## 设计文档目录

### 核心设计

1. **[系统架构设计](./ARCHITECTURE.md)**
   - 整体架构图
   - 分层设计
   - 技术选型
   - 模块依赖关系

2. **[MAF Workflow 详细设计](./WORKFLOW_DESIGN.md)**
   - Workflow 概述
   - SQL 分析 Workflow
   - 数据库配置优化 Workflow
   - Executor 接口设计
   - 数据传递与上下文
   - 错误处理与重试

3. **[数据模型设计](./DATA_MODEL.md)**
   - 数据库选型
   - 表结构设计
   - 实体关系图
   - JSONB 字段设计
   - 索引策略

4. **[MCP 集成方案](./MCP_INTEGRATION.md)**
   - MCP 概述
   - MCP 客户端设计
   - 超时处理
   - Fallback 策略
   - 错误处理
   - 连接池管理

### 安全与部署

5. **[安全设计](./SECURITY_DESIGN.md)**
   - 安全威胁分析
   - 认证与授权
   - 数据加密
   - 审计日志
   - 安全最佳实践

6. **[部署架构](./DEPLOYMENT.md)**
   - Aspire 编排
   - Docker 部署
   - 生产环境配置
   - 运维指南

### 前端设计

7. **[前端架构设计](./FRONTEND_ARCHITECTURE.md)**
   - 全局 UI 框架
   - 状态管理（Pinia）
   - 路由设计
   - 公共组件
   - SSE 集成

8. **[页面详细设计](./PAGE_DESIGN.md)**
   - 总览页面
   - SQL 调优页面
   - 实例调优页面
   - 审核工作台页面
   - 历史任务页面
   - 运行回放页面

9. **[组件规范](./COMPONENT_SPEC.md)**
   - SSE 连接器
   - Monaco 编辑器
   - Workflow 进度条
   - 建议卡片
   - 证据查看器
   - 日志查看器

### 参考文档

10. **[API 接口规范](./API_SPEC.md)**
    - 通用规范
    - Workflow API
    - Review API
    - Dashboard API
    - History API
    - SSE 事件规范

11. **[术语表](./GLOSSARY.md)**
    - 项目术语定义
    - 技术术语解释

12. **[P0/P1 优先级设计](./P0_P1_DESIGN.md)**
    - P0 必须实现的功能
    - P1 应该实现的功能
    - 实现细节

---

## 文档关系图

```
REQUIREMENTS.md (需求文档)
    ↓
DESIGN.md (本文档 - 索引)
    ├── ARCHITECTURE.md (系统架构)
    ├── WORKFLOW_DESIGN.md (Workflow 设计)
    ├── DATA_MODEL.md (数据模型)
    ├── MCP_INTEGRATION.md (MCP 集成)
    ├── SECURITY_DESIGN.md (安全设计)
    ├── DEPLOYMENT.md (部署架构)
    ├── FRONTEND_ARCHITECTURE.md (前端架构)
    ├── PAGE_DESIGN.md (页面设计)
    ├── COMPONENT_SPEC.md (组件规范)
    ├── API_SPEC.md (API 规范)
    ├── GLOSSARY.md (术语表)
    └── P0_P1_DESIGN.md (优先级设计)
```

---

## 快速导航

**开始开发前必读**：
1. [REQUIREMENTS.md](./REQUIREMENTS.md) - 了解项目需求
2. [ARCHITECTURE.md](./ARCHITECTURE.md) - 理解系统架构
3. [WORKFLOW_DESIGN.md](./WORKFLOW_DESIGN.md) - 掌握 Workflow 设计

**后端开发**：
- [DATA_MODEL.md](./DATA_MODEL.md) - 数据库设计
- [MCP_INTEGRATION.md](./MCP_INTEGRATION.md) - MCP 集成
- [API_SPEC.md](./API_SPEC.md) - API 接口

**前端开发**：
- [FRONTEND_ARCHITECTURE.md](./FRONTEND_ARCHITECTURE.md) - 前端架构
- [PAGE_DESIGN.md](./PAGE_DESIGN.md) - 页面设计
- [COMPONENT_SPEC.md](./COMPONENT_SPEC.md) - 组件规范

**运维部署**：
- [SECURITY_DESIGN.md](./SECURITY_DESIGN.md) - 安全配置
- [DEPLOYMENT.md](./DEPLOYMENT.md) - 部署指南

---

## 文档维护

**更新原则**：
- 架构变更必须同步更新相关文档
- 新增功能需更新对应的设计文档
- API 变更需同步更新 API_SPEC.md
- 新增术语需添加到 GLOSSARY.md

**文档版本**：
- 当前版本：v1.0
- 最后更新：2026-04-15
