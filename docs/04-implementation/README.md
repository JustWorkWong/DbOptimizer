# Implementation Entry

AI 或人工执行时，按这个顺序阅读：

1. [../00-overview/SYSTEM_SCOPE_AND_STATUS.md](../00-overview/SYSTEM_SCOPE_AND_STATUS.md)
2. [../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md](../02-architecture/MAF_WORKFLOW_ARCHITECTURE.md)
3. `03-design/` 下与当前任务相关的 API / workflow 文档
4. [MASTER_DELIVERY_ROADMAP.md](./MASTER_DELIVERY_ROADMAP.md)
5. [IMPLEMENTATION_TASK_CHECKLIST.md](./IMPLEMENTATION_TASK_CHECKLIST.md)
6. [TASK_CARDS_INDEX.md](./TASK_CARDS_INDEX.md)
7. [AI_EXECUTION_PLAYBOOK.md](./AI_EXECUTION_PLAYBOOK.md)

执行原则：

1. 先按 roadmap 选 Epic，再按 checklist 选原子任务。
2. 一次只给 AI 一个原子任务。
3. AI 必须同时遵守 [AI_TASK_PACKET_TEMPLATE.md](./AI_TASK_PACKET_TEMPLATE.md) 的交接格式。
4. 任一任务开始编码前，先确认：
   - 使用 MAF workflow
   - 不改变统一 API 契约
   - 不引入第二套结果协议
