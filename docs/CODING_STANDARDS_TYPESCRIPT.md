# TypeScript 编码规范

**项目名称**：DbOptimizer  
**创建日期**：2026-04-15  
**版本**：v1.0  
**作者**：tengfengsu

---

## 目录

1. [命名规范](#1-命名规范)
2. [Vue 3 组合式 API 规范](#2-vue-3-组合式-api-规范)
3. [类型定义规范](#3-类型定义规范)
4. [代码风格](#4-代码风格)
5. [异步处理规范](#5-异步处理规范)
6. [状态管理规范](#6-状态管理规范)
7. [组件规范](#7-组件规范)
8. [单元测试规范](#8-单元测试规范)
9. [JSDoc 注释规范](#9-jsdoc-注释规范)

---

## 1. 命名规范

### 1.1 组件名

**规则**：PascalCase，多个单词

```typescript
// ✅ 正确
WorkflowProgress.vue
SqlEditor.vue
EvidenceViewer.vue

// ❌ 错误
workflowProgress.vue  // camelCase
sqlEditor.vue  // camelCase
Editor.vue  // 单个单词（不够具体）
```

### 1.2 函数名

**规则**：camelCase，动词开头

```typescript
// ✅ 正确
function fetchWorkflowStatus() { }
function handleSubmit() { }
function validateInput() { }

// ❌ 错误
function FetchWorkflowStatus() { }  // PascalCase
function workflow_status() { }  // snake_case
function status() { }  // 名词（应该是动词）
```

### 1.3 变量名

**规则**：camelCase

```typescript
// ✅ 正确
const sessionId = '123'
const workflowStatus = 'Running'
const maxRetryCount = 3

// ❌ 错误
const SessionId = '123'  // PascalCase
const workflow_status = 'Running'  // snake_case
const MAX_RETRY_COUNT = 3  // UPPER_SNAKE_CASE（常量除外）
```

### 1.4 常量

**规则**：UPPER_SNAKE_CASE

```typescript
// ✅ 正确
const MAX_RETRY_COUNT = 3
const DEFAULT_TIMEOUT = 30000
const API_BASE_URL = 'http://localhost:5000'

// ❌ 错误
const maxRetryCount = 3  // camelCase
const DefaultTimeout = 30000  // PascalCase
```

### 1.5 类型和接口

**规则**：PascalCase

```typescript
// ✅ 正确
interface WorkflowSession { }
type WorkflowStatus = 'Running' | 'Completed' | 'Failed'
interface ApiResponse<T> { }

// ❌ 错误
interface workflowSession { }  // camelCase
type workflow_status = 'Running' | 'Completed'  // snake_case
```

### 1.6 枚举

**规则**：PascalCase（枚举名和成员）

```typescript
// ✅ 正确
enum WorkflowStatus {
  Running = 'Running',
  Completed = 'Completed',
  Failed = 'Failed'
}

// ❌ 错误
enum workflowStatus {  // camelCase
  running = 'Running',  // camelCase
  COMPLETED = 'Completed'  // UPPER_SNAKE_CASE
}
```

### 1.7 组合式函数（Composables）

**规则**：camelCase，以 `use` 开头

```typescript
// ✅ 正确
function useWorkflow() { }
function useSSEConnection() { }
function useDebounce() { }

// ❌ 错误
function workflow() { }  // 缺少 use 前缀
function UseWorkflow() { }  // PascalCase
function use_workflow() { }  // snake_case
```

---

## 2. Vue 3 组合式 API 规范

### 2.1 script setup

**规则**：优先使用 `<script setup>`

```vue
<!-- ✅ 正确 -->
<script setup lang="ts">
import { ref, computed } from 'vue'

const count = ref(0)
const doubled = computed(() => count.value * 2)

function increment() {
  count.value++
}
</script>

<!-- ❌ 错误（使用选项式 API） -->
<script lang="ts">
export default {
  data() {
    return {
      count: 0
    }
  },
  computed: {
    doubled() {
      return this.count * 2
    }
  }
}
</script>
```

### 2.2 ref vs reactive

**规则**：
- 基本类型使用 `ref`
- 对象使用 `reactive` 或 `ref`（推荐 `ref` 保持一致性）

```typescript
// ✅ 正确
const count = ref(0)
const name = ref('Alice')
const user = ref({ id: 1, name: 'Alice' })

// ✅ 也可以（使用 reactive）
const user = reactive({ id: 1, name: 'Alice' })

// ❌ 错误（基本类型使用 reactive）
const count = reactive(0)  // reactive 不支持基本类型
```

### 2.3 computed

**规则**：
- 只读计算属性使用 `computed(() => ...)`
- 可写计算属性使用 `computed({ get, set })`

```typescript
// ✅ 正确（只读）
const doubled = computed(() => count.value * 2)

// ✅ 正确（可写）
const fullName = computed({
  get: () => `${firstName.value} ${lastName.value}`,
  set: (value) => {
    const parts = value.split(' ')
    firstName.value = parts[0]
    lastName.value = parts[1]
  }
})

// ❌ 错误（在 computed 中修改状态）
const doubled = computed(() => {
  count.value++  // 副作用
  return count.value * 2
})
```

### 2.4 watch vs watchEffect

**规则**：
- 需要访问旧值或精确控制依赖时使用 `watch`
- 自动追踪依赖时使用 `watchEffect`

```typescript
// ✅ 正确（watch - 需要旧值）
watch(count, (newValue, oldValue) => {
  console.log(`Count changed from ${oldValue} to ${newValue}`)
})

// ✅ 正确（watchEffect - 自动追踪）
watchEffect(() => {
  console.log(`Count is ${count.value}`)
})

// ✅ 正确（watch - 多个源）
watch([count, name], ([newCount, newName]) => {
  console.log(`Count: ${newCount}, Name: ${newName}`)
})
```

### 2.5 生命周期钩子

**规则**：使用组合式 API 的生命周期钩子

```typescript
// ✅ 正确
import { onMounted, onUnmounted } from 'vue'

onMounted(() => {
  console.log('Component mounted')
})

onUnmounted(() => {
  console.log('Component unmounted')
})

// ❌ 错误（选项式 API）
export default {
  mounted() {
    console.log('Component mounted')
  }
}
```

---

## 3. 类型定义规范

### 3.1 interface vs type

**规则**：
- 对象形状使用 `interface`
- 联合类型、交叉类型、工具类型使用 `type`

```typescript
// ✅ 正确（interface - 对象形状）
interface WorkflowSession {
  sessionId: string
  status: WorkflowStatus
  createdAt: Date
}

// ✅ 正确（type - 联合类型）
type WorkflowStatus = 'Running' | 'Completed' | 'Failed'

// ✅ 正确（type - 交叉类型）
type ExtendedSession = WorkflowSession & {
  metadata: Record<string, unknown>
}

// ❌ 错误（type 用于简单对象）
type WorkflowSession = {
  sessionId: string
  status: WorkflowStatus
}
```

### 3.2 泛型

**规则**：使用描述性的泛型参数名

```typescript
// ✅ 正确
interface ApiResponse<TData> {
  success: boolean
  data: TData | null
  error: string | null
}

function fetchData<TResult>(url: string): Promise<TResult> {
  // ...
}

// ❌ 错误（单字母泛型，不够描述性）
interface ApiResponse<T> {
  success: boolean
  data: T | null
}

// ✅ 例外：通用工具类型可以使用单字母
type Nullable<T> = T | null
type ReadonlyDeep<T> = { readonly [K in keyof T]: ReadonlyDeep<T[K]> }
```

### 3.3 联合类型

**规则**：使用字面量类型而不是字符串

```typescript
// ✅ 正确
type WorkflowStatus = 'Running' | 'Completed' | 'Failed'

function updateStatus(status: WorkflowStatus) {
  // TypeScript 会检查类型
}

// ❌ 错误
function updateStatus(status: string) {
  // 任何字符串都可以，没有类型安全
}
```

### 3.4 可选属性

**规则**：使用 `?` 而不是 `| undefined`

```typescript
// ✅ 正确
interface User {
  id: string
  name: string
  email?: string  // 可选
}

// ❌ 错误
interface User {
  id: string
  name: string
  email: string | undefined  // 冗长
}
```

---

## 4. 代码风格

### 4.1 缩进

**规则**：2 个空格

```typescript
// ✅ 正确
function example() {
  if (condition) {
    doSomething()
  }
}

// ❌ 错误（4 个空格或 Tab）
```

### 4.2 分号

**规则**：不使用分号（依赖 ASI）

```typescript
// ✅ 正确
const name = 'Alice'
const age = 30

function greet() {
  console.log('Hello')
}

// ❌ 错误
const name = 'Alice';
const age = 30;
```

### 4.3 引号

**规则**：单引号，模板字符串使用反引号

```typescript
// ✅ 正确
const name = 'Alice'
const message = `Hello, ${name}!`

// ❌ 错误
const name = "Alice"  // 双引号
const message = 'Hello, ' + name + '!'  // 字符串拼接
```

### 4.4 尾逗号

**规则**：多行时使用尾逗号

```typescript
// ✅ 正确
const user = {
  id: 1,
  name: 'Alice',
  email: 'alice@example.com',  // 尾逗号
}

const numbers = [
  1,
  2,
  3,  // 尾逗号
]

// ❌ 错误
const user = {
  id: 1,
  name: 'Alice',
  email: 'alice@example.com'  // 缺少尾逗号
}
```

### 4.5 箭头函数

**规则**：
- 单个参数不使用括号
- 单行返回不使用大括号

```typescript
// ✅ 正确
const double = (n: number) => n * 2
const greet = (name: string) => `Hello, ${name}!`
const users = items.map(item => item.user)

// ✅ 正确（多行）
const process = (data: Data) => {
  const result = transform(data)
  return result
}

// ❌ 错误
const double = (n: number) => { return n * 2 }  // 单行不需要大括号
const users = items.map((item) => item.user)  // 单参数不需要括号
```

---

## 5. 异步处理规范

### 5.1 async/await

**规则**：优先使用 `async/await` 而不是 `.then()`

```typescript
// ✅ 正确
async function fetchWorkflow(sessionId: string) {
  try {
    const response = await fetch(`/api/workflows/${sessionId}`)
    const data = await response.json()
    return data
  } catch (error) {
    console.error('Failed to fetch workflow:', error)
    throw error
  }
}

// ❌ 错误
function fetchWorkflow(sessionId: string) {
  return fetch(`/api/workflows/${sessionId}`)
    .then(response => response.json())
    .then(data => data)
    .catch(error => {
      console.error('Failed to fetch workflow:', error)
      throw error
    })
}
```

### 5.2 Promise 错误处理

**规则**：始终处理错误

```typescript
// ✅ 正确
async function loadData() {
  try {
    const data = await fetchData()
    processData(data)
  } catch (error) {
    handleError(error)
  }
}

// ❌ 错误（未处理错误）
async function loadData() {
  const data = await fetchData()  // 如果失败会导致未捕获的 Promise rejection
  processData(data)
}
```

### 5.3 并行请求

**规则**：使用 `Promise.all` 处理并行请求

```typescript
// ✅ 正确
async function loadDashboard() {
  const [workflows, stats, history] = await Promise.all([
    fetchWorkflows(),
    fetchStats(),
    fetchHistory(),
  ])
  
  return { workflows, stats, history }
}

// ❌ 错误（串行执行）
async function loadDashboard() {
  const workflows = await fetchWorkflows()
  const stats = await fetchStats()
  const history = await fetchHistory()
  
  return { workflows, stats, history }
}
```

---

## 6. 状态管理规范

### 6.1 Pinia Store 结构

**规则**：使用 setup 语法

```typescript
// ✅ 正确
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useWorkflowStore = defineStore('workflow', () => {
  // State
  const sessions = ref<WorkflowSession[]>([])
  const currentSessionId = ref<string | null>(null)
  
  // Getters
  const currentSession = computed(() => 
    sessions.value.find(s => s.sessionId === currentSessionId.value)
  )
  
  // Actions
  async function fetchSessions() {
    const data = await api.fetchWorkflows()
    sessions.value = data
  }
  
  function setCurrentSession(sessionId: string) {
    currentSessionId.value = sessionId
  }
  
  return {
    // State
    sessions,
    currentSessionId,
    // Getters
    currentSession,
    // Actions
    fetchSessions,
    setCurrentSession,
  }
})

// ❌ 错误（选项式语法）
export const useWorkflowStore = defineStore('workflow', {
  state: () => ({
    sessions: [],
  }),
  actions: {
    fetchSessions() {
      // ...
    },
  },
})
```

### 6.2 Store 命名

**规则**：`use{Name}Store`

```typescript
// ✅ 正确
export const useWorkflowStore = defineStore('workflow', () => { })
export const useAuthStore = defineStore('auth', () => { })
export const useNotificationStore = defineStore('notification', () => { })

// ❌ 错误
export const workflowStore = defineStore('workflow', () => { })  // 缺少 use 前缀
export const useWorkflow = defineStore('workflow', () => { })  // 缺少 Store 后缀
```

### 6.3 Store 使用

**规则**：在 setup 中调用

```typescript
// ✅ 正确
<script setup lang="ts">
import { useWorkflowStore } from '@/stores/workflow'

const workflowStore = useWorkflowStore()

onMounted(() => {
  workflowStore.fetchSessions()
})
</script>

// ❌ 错误（在 setup 外调用）
import { useWorkflowStore } from '@/stores/workflow'

const workflowStore = useWorkflowStore()  // 错误：在模块顶层调用

export default {
  setup() {
    // ...
  }
}
```

---

## 7. 组件规范

### 7.1 Props 定义

**规则**：使用 TypeScript 类型定义

```typescript
// ✅ 正确
<script setup lang="ts">
interface Props {
  sessionId: string
  status: WorkflowStatus
  progress?: number
}

const props = withDefaults(defineProps<Props>(), {
  progress: 0,
})
</script>

// ❌ 错误（运行时验证）
<script setup lang="ts">
const props = defineProps({
  sessionId: {
    type: String,
    required: true,
  },
  status: {
    type: String,
    required: true,
  },
})
</script>
```

### 7.2 Emits 定义

**规则**：使用 TypeScript 类型定义

```typescript
// ✅ 正确
<script setup lang="ts">
interface Emits {
  (e: 'update:modelValue', value: string): void
  (e: 'submit', data: FormData): void
  (e: 'cancel'): void
}

const emit = defineEmits<Emits>()

function handleSubmit(data: FormData) {
  emit('submit', data)
}
</script>

// ❌ 错误（运行时验证）
<script setup lang="ts">
const emit = defineEmits(['update:modelValue', 'submit', 'cancel'])
</script>
```

### 7.3 Slots

**规则**：使用 TypeScript 类型定义

```typescript
// ✅ 正确
<script setup lang="ts">
interface Slots {
  default(props: { item: WorkflowSession }): any
  header(): any
  footer(): any
}

defineSlots<Slots>()
</script>

<template>
  <div>
    <slot name="header" />
    <slot :item="session" />
    <slot name="footer" />
  </div>
</template>
```

### 7.4 组件大小

**规则**：
- 单个组件不超过 400 行
- 超过则拆分为子组件或组合式函数

```typescript
// ✅ 正确（拆分为组合式函数）
// useWorkflowLogic.ts
export function useWorkflowLogic(sessionId: string) {
  const status = ref<WorkflowStatus>('Running')
  const progress = ref(0)
  
  async function fetchStatus() {
    // ...
  }
  
  return { status, progress, fetchStatus }
}

// WorkflowView.vue
<script setup lang="ts">
import { useWorkflowLogic } from './useWorkflowLogic'

const props = defineProps<{ sessionId: string }>()
const { status, progress, fetchStatus } = useWorkflowLogic(props.sessionId)
</script>
```

---

## 8. 单元测试规范

### 8.1 测试框架

**规则**：使用 Vitest + Vue Test Utils

```typescript
// ✅ 正确
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import WorkflowProgress from './WorkflowProgress.vue'

describe('WorkflowProgress', () => {
  it('renders progress correctly', () => {
    const wrapper = mount(WorkflowProgress, {
      props: {
        progress: 50,
      },
    })
    
    expect(wrapper.find('.progress-bar').attributes('style')).toContain('width: 50%')
  })
})
```

### 8.2 测试结构（AAA 模式）

**规则**：Arrange - Act - Assert

```typescript
// ✅ 正确
it('updates status when workflow completes', async () => {
  // Arrange
  const wrapper = mount(WorkflowView, {
    props: { sessionId: '123' },
  })
  
  // Act
  await wrapper.vm.completeWorkflow()
  
  // Assert
  expect(wrapper.vm.status).toBe('Completed')
})
```

### 8.3 测试命名

**规则**：描述性命名，说明测试的行为

```typescript
// ✅ 正确
it('displays error message when API call fails', () => { })
it('disables submit button when form is invalid', () => { })
it('emits update event when value changes', () => { })

// ❌ 错误
it('test 1', () => { })
it('works', () => { })
it('button', () => { })
```

### 8.4 Mock

**规则**：使用 Vitest 的 mock 功能

```typescript
// ✅ 正确
import { vi } from 'vitest'

const mockFetch = vi.fn()
global.fetch = mockFetch

it('fetches workflow data', async () => {
  mockFetch.mockResolvedValue({
    json: async () => ({ sessionId: '123', status: 'Running' }),
  })
  
  const data = await fetchWorkflow('123')
  
  expect(mockFetch).toHaveBeenCalledWith('/api/workflows/123')
  expect(data.status).toBe('Running')
})
```

---

## 9. JSDoc 注释规范

### 9.1 函数注释

**规则**：公共函数必须添加 JSDoc

```typescript
// ✅ 正确
/**
 * 获取 Workflow 状态
 * @param sessionId - Workflow 会话 ID
 * @returns Workflow 状态信息
 * @throws {Error} 当会话不存在时抛出错误
 */
async function fetchWorkflowStatus(sessionId: string): Promise<WorkflowStatus> {
  // ...
}

// ❌ 错误（缺少注释）
async function fetchWorkflowStatus(sessionId: string): Promise<WorkflowStatus> {
  // ...
}
```

### 9.2 接口注释

**规则**：复杂接口添加注释

```typescript
// ✅ 正确
/**
 * Workflow 会话信息
 */
interface WorkflowSession {
  /** 会话唯一标识 */
  sessionId: string
  
  /** 当前状态 */
  status: WorkflowStatus
  
  /** 执行进度（0-100） */
  progress: number
  
  /** 创建时间 */
  createdAt: Date
}
```

### 9.3 复杂逻辑注释

**规则**：使用中文注释解释复杂逻辑

```typescript
// ✅ 正确
function calculateConfidence(evidence: Evidence[]): number {
  // 1. 过滤有效证据（置信度 > 0.5）
  const validEvidence = evidence.filter(e => e.confidence > 0.5)
  
  // 2. 计算加权平均值
  const totalWeight = validEvidence.reduce((sum, e) => sum + e.weight, 0)
  const weightedSum = validEvidence.reduce((sum, e) => sum + e.confidence * e.weight, 0)
  
  // 3. 返回归一化结果
  return totalWeight > 0 ? weightedSum / totalWeight : 0
}
```

---

## 与其他文档的映射关系

- **项目规范**：[CLAUDE.md](../CLAUDE.md) - 项目级别的技术栈和约定
- **C# 规范**：[CODING_STANDARDS_CSHARP.md](./CODING_STANDARDS_CSHARP.md) - 后端编码规范
- **Git 工作流**：[GIT_WORKFLOW.md](./GIT_WORKFLOW.md) - 提交和分支规范
- **API 规范**：[API_SPEC.md](./API_SPEC.md) - 前后端接口契约

---

## 工具配置

### ESLint 配置

```javascript
// .eslintrc.cjs
module.exports = {
  extends: [
    'plugin:vue/vue3-recommended',
    '@vue/eslint-config-typescript',
    '@vue/eslint-config-prettier',
  ],
  rules: {
    'vue/multi-word-component-names': 'error',
    'vue/component-name-in-template-casing': ['error', 'PascalCase'],
    '@typescript-script/no-explicit-any': 'warn',
    '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
  },
}
```

### Prettier 配置

```javascript
// .prettierrc.cjs
module.exports = {
  semi: false,
  singleQuote: true,
  trailingComma: 'es5',
  printWidth: 100,
  tabWidth: 2,
}
```

### TypeScript 配置

```json
// tsconfig.json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noImplicitReturns": true,
    "skipLibCheck": true
  }
}
```

---

**文档版本**：v1.0  
**最后更新**：2026-04-15