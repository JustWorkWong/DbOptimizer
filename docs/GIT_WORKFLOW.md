# Git 工作流规范

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [分支策略](#1-分支策略)
2. [提交规范](#2-提交规范)
3. [Pull Request 流程](#3-pull-request-流程)
4. [代码审查清单](#4-代码审查清单)
5. [冲突解决指南](#5-冲突解决指南)
6. [版本标签规范](#6-版本标签规范)

---

## 1. 分支策略

### 1.1 分支类型

| 分支类型 | 命名规则 | 生命周期 | 用途 |
|---------|---------|---------|------|
| `main` | `main` | 永久 | 生产环境代码，始终可部署 |
| `develop` | `develop` | 永久 | 开发主分支，集成最新功能 |
| `feature` | `feature/<issue-id>-<description>` | 临时 | 新功能开发 |
| `bugfix` | `bugfix/<issue-id>-<description>` | 临时 | Bug 修复 |
| `hotfix` | `hotfix/<issue-id>-<description>` | 临时 | 紧急生产环境修复 |
| `release` | `release/v<version>` | 临时 | 发布准备 |

### 1.2 分支命名示例

```bash
# ✅ 正确
feature/123-add-sql-analysis-workflow
feature/456-implement-mcp-client
bugfix/789-fix-sse-reconnection
hotfix/101-fix-database-connection-leak
release/v1.0.0

# ❌ 错误
feature/sql-analysis  # 缺少 issue ID
feature/123  # 缺少描述
Feature/123-add-workflow  # 首字母大写
feature/123_add_workflow  # 使用下划线
```

### 1.3 分支工作流

```
main (生产环境)
  ↑
  └─ hotfix/101-fix-critical-bug (紧急修复)
  
develop (开发主分支)
  ↑
  ├─ feature/123-add-feature-a (功能 A)
  ├─ feature/456-add-feature-b (功能 B)
  └─ bugfix/789-fix-bug-c (Bug 修复)
  
  ↓
release/v1.0.0 (发布准备)
  ↓
main (合并发布)
```

### 1.4 创建分支

```bash
# 从 develop 创建 feature 分支
git checkout develop
git pull origin develop
git checkout -b feature/123-add-sql-analysis

# 从 main 创建 hotfix 分支
git checkout main
git pull origin main
git checkout -b hotfix/101-fix-connection-leak

# 从 develop 创建 release 分支
git checkout develop
git pull origin develop
git checkout -b release/v1.0.0
```

---

## 2. 提交规范

### 2.1 Conventional Commits

**格式**：

```
<type>(<scope>): <subject>

<body>

<footer>
```

**字段说明**：
- `type`：提交类型（必需）
- `scope`：影响范围（可选）
- `subject`：简短描述（必需，不超过 50 字符）
- `body`：详细描述（可选）
- `footer`：关联 issue 或 breaking changes（可选）

### 2.2 提交类型

| 类型 | 说明 | 示例 |
|------|------|------|
| `feat` | 新功能 | `feat(workflow): add SQL analysis workflow` |
| `fix` | Bug 修复 | `fix(sse): fix reconnection logic` |
| `docs` | 文档更新 | `docs(readme): update installation guide` |
| `style` | 代码格式（不影响功能） | `style(api): format code with prettier` |
| `refactor` | 重构（不改变功能） | `refactor(mcp): extract connection pool` |
| `perf` | 性能优化 | `perf(query): optimize database query` |
| `test` | 测试相关 | `test(workflow): add unit tests` |
| `chore` | 构建/工具相关 | `chore(deps): update dependencies` |
| `ci` | CI/CD 相关 | `ci(github): add workflow for tests` |
| `revert` | 回滚提交 | `revert: revert commit abc123` |

### 2.3 提交示例

**简单提交**：

```bash
git commit -m "feat(workflow): add SQL analysis workflow"
git commit -m "fix(sse): fix reconnection after network error"
git commit -m "docs(api): update API documentation"
```

**详细提交**：

```bash
git commit -m "feat(workflow): add SQL analysis workflow

Implement the SQL analysis workflow with the following features:
- Parse SQL using MCP client
- Analyze query performance
- Generate optimization recommendations

Closes #123"
```

**Breaking Change**：

```bash
git commit -m "feat(api): change API response format

BREAKING CHANGE: API response format changed from { data } to { success, data, error }

Migration guide:
- Update all API clients to handle new format
- Check success field before accessing data

Closes #456"
```

### 2.4 提交最佳实践

**规则**：
- 每个提交只做一件事
- 提交信息使用英文
- 主题行不超过 50 字符
- 主题行首字母小写
- 主题行不以句号结尾
- 使用祈使语气（"add" 而不是 "added"）

```bash
# ✅ 正确
git commit -m "feat(workflow): add SQL analysis workflow"
git commit -m "fix(sse): fix reconnection logic"
git commit -m "refactor(mcp): extract connection pool"

# ❌ 错误
git commit -m "Added SQL analysis workflow"  # 过去式
git commit -m "Fix bug."  # 句号结尾
git commit -m "feat(workflow): Add SQL analysis workflow and update documentation and fix bugs"  # 做了多件事
git commit -m "feat(workflow): 添加 SQL 分析工作流"  # 使用中文
```

---

## 3. Pull Request 流程

### 3.1 创建 PR

```bash
# 1. 确保分支是最新的
git checkout feature/123-add-sql-analysis
git pull origin develop
git rebase develop

# 2. 推送到远程
git push origin feature/123-add-sql-analysis

# 3. 在 GitHub 上创建 PR
# - Base: develop
# - Compare: feature/123-add-sql-analysis
```

### 3.2 PR 标题

**格式**：与提交信息相同

```
feat(workflow): add SQL analysis workflow
fix(sse): fix reconnection after network error
```

### 3.3 PR 描述模板

```markdown
## 描述

简要描述此 PR 的目的和实现方式。

## 变更类型

- [ ] 新功能 (feat)
- [ ] Bug 修复 (fix)
- [ ] 重构 (refactor)
- [ ] 文档更新 (docs)
- [ ] 其他

## 相关 Issue

Closes #123

## 变更内容

- 实现 SQL 分析 Workflow
- 添加 MCP 客户端集成
- 更新 API 文档

## 测试

- [ ] 单元测试已通过
- [ ] 集成测试已通过
- [ ] 手动测试已完成

## 测试步骤

1. 启动项目：`dotnet run`
2. 访问 API：`POST /api/workflows/sql-analysis`
3. 验证响应格式

## 截图（如适用）

![Screenshot](url)

## Checklist

- [ ] 代码遵循项目编码规范
- [ ] 已添加必要的测试
- [ ] 测试覆盖率 >= 80%
- [ ] 文档已更新
- [ ] 无 breaking changes（或已在描述中说明）
- [ ] 已自我审查代码
- [ ] 无 console.log 或调试代码
```

### 3.4 PR 审查流程

```
1. 创建 PR
   ↓
2. CI/CD 自动检查
   - 编译通过
   - 测试通过
   - 代码覆盖率 >= 80%
   - Lint 检查通过
   ↓
3. 代码审查
   - 至少 1 人审查
   - 解决所有评论
   ↓
4. 合并到目标分支
   - Squash and merge（推荐）
   - Merge commit
   ↓
5. 删除 feature 分支
```

### 3.5 合并策略

**Squash and Merge（推荐）**：

```bash
# 将所有提交压缩为一个
# 优点：保持主分支历史清晰
# 缺点：丢失详细提交历史
```

**Merge Commit**：

```bash
# 保留所有提交历史
# 优点：完整的提交历史
# 缺点：主分支历史复杂
```

**Rebase and Merge**：

```bash
# 将提交重新应用到目标分支
# 优点：线性历史
# 缺点：需要解决冲突
```

---

## 4. 代码审查清单

### 4.1 功能性

- [ ] 代码实现了 PR 描述的功能
- [ ] 没有引入新的 bug
- [ ] 边界条件已处理
- [ ] 错误处理完善

### 4.2 代码质量

- [ ] 代码遵循项目编码规范
- [ ] 命名清晰、有意义
- [ ] 函数短小（< 50 行）
- [ ] 文件适中（< 800 行）
- [ ] 无深层嵌套（< 4 层）
- [ ] 无重复代码

### 4.3 测试

- [ ] 单元测试已添加
- [ ] 测试覆盖率 >= 80%
- [ ] 测试用例覆盖主要场景
- [ ] 测试用例覆盖边界条件

### 4.4 安全性

- [ ] 无硬编码密钥或密码
- [ ] 用户输入已验证
- [ ] SQL 注入已防范
- [ ] XSS 已防范

### 4.5 性能

- [ ] 无 N+1 查询
- [ ] 数据库查询已优化
- [ ] 无不必要的循环
- [ ] 资源已正确释放

### 4.6 文档

- [ ] 代码注释清晰
- [ ] API 文档已更新
- [ ] README 已更新（如需要）
- [ ] CHANGELOG 已更新（如需要）

---

## 5. 冲突解决指南

### 5.1 预防冲突

```bash
# 定期同步主分支
git checkout feature/123-add-sql-analysis
git fetch origin
git rebase origin/develop

# 推送到远程
git push origin feature/123-add-sql-analysis --force-with-lease
```

### 5.2 解决冲突

```bash
# 1. 拉取最新代码
git checkout develop
git pull origin develop

# 2. 切换到 feature 分支
git checkout feature/123-add-sql-analysis

# 3. Rebase
git rebase develop

# 4. 解决冲突
# 编辑冲突文件，保留正确的代码

# 5. 标记为已解决
git add <conflicted-file>

# 6. 继续 rebase
git rebase --continue

# 7. 推送到远程
git push origin feature/123-add-sql-analysis --force-with-lease
```

### 5.3 冲突标记

```csharp
<<<<<<< HEAD (当前分支)
var result = await _service.AnalyzeAsync(sql);
=======
var result = await _service.ExecuteAsync(sql);
>>>>>>> feature/123-add-sql-analysis (incoming 分支)
```

**解决方式**：
- 保留 HEAD：删除 `=======` 到 `>>>>>>>` 之间的内容
- 保留 incoming：删除 `<<<<<<<` 到 `=======` 之间的内容
- 合并两者：手动编辑，保留两者的逻辑

### 5.4 中止 Rebase

```bash
# 如果冲突太复杂，可以中止 rebase
git rebase --abort

# 使用 merge 代替
git merge develop
```

---

## 6. 版本标签规范

### 6.1 语义化版本

**格式**：`v<major>.<minor>.<patch>`

- `major`：不兼容的 API 变更
- `minor`：向后兼容的功能新增
- `patch`：向后兼容的 bug 修复

**示例**：
- `v1.0.0`：首个正式版本
- `v1.1.0`：新增功能
- `v1.1.1`：Bug 修复
- `v2.0.0`：Breaking changes

### 6.2 创建标签

```bash
# 1. 切换到 main 分支
git checkout main
git pull origin main

# 2. 创建标签
git tag -a v1.0.0 -m "Release version 1.0.0"

# 3. 推送标签
git push origin v1.0.0

# 4. 推送所有标签
git push origin --tags
```

### 6.3 预发布版本

**格式**：`v<major>.<minor>.<patch>-<pre-release>`

```bash
# Alpha 版本
git tag -a v1.0.0-alpha.1 -m "Alpha release 1"

# Beta 版本
git tag -a v1.0.0-beta.1 -m "Beta release 1"

# Release Candidate
git tag -a v1.0.0-rc.1 -m "Release candidate 1"
```

### 6.4 删除标签

```bash
# 删除本地标签
git tag -d v1.0.0

# 删除远程标签
git push origin --delete v1.0.0
```

---

## 与其他文档的映射关系

- **开发环境搭建**：[DEV_SETUP.md](./DEV_SETUP.md)
- **C# 编码规范**：[CODING_STANDARDS_CSHARP.md](./CODING_STANDARDS_CSHARP.md)
- **TypeScript 编码规范**：[CODING_STANDARDS_TYPESCRIPT.md](./CODING_STANDARDS_TYPESCRIPT.md)
- **API 接口规范**：[API_SPEC.md](./API_SPEC.md)

---

## 快速参考

### 常用命令

```bash
# 创建分支
git checkout -b feature/123-add-feature

# 提交代码
git add .
git commit -m "feat(scope): add feature"

# 推送分支
git push origin feature/123-add-feature

# 同步主分支
git fetch origin
git rebase origin/develop

# 创建标签
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin v1.0.0
```

### 提交类型速查

| 类型 | 用途 |
|------|------|
| `feat` | 新功能 |
| `fix` | Bug 修复 |
| `docs` | 文档 |
| `style` | 格式 |
| `refactor` | 重构 |
| `perf` | 性能 |
| `test` | 测试 |
| `chore` | 构建/工具 |

---

**文档版本**：v1.0  
**最后更新**：2026-04-15
