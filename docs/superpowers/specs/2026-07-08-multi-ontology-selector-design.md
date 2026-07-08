# Multi-Ontology File Selector

**Date:** 2026-07-08
**Status:** Design

---

## 目标

将当前单一默认 ontology 加载改为支持 `ontology/` 目录下多个 `.owl` 文件列表，用户通过顶部工具栏下拉框切换查看。

## 范围

- 后端新增两个 API 端点，废弃两个旧端点
- 前端工具栏改造为下拉选择器
- 保留外部文件上传能力

---

## 后端 API

| 端点 | 方法 | 说明 |
|------|------|------|
| `GET /api/ontology/files` | 新增 | 扫描 `ontology/` 目录，返回 `.owl` 文件名列表 |
| `GET /api/ontology/load?file=xxx` | 新增 | 按文件名加载解析指定 ontology（带缓存） |
| `POST /api/ontology/parse` | 保留 | 上传外部文件解析 |
| `GET /api/ontology/default` | 废弃 | 由 `/load` 替代 |
| `GET /api/ontology/source` | 废弃 | 不再需要 |
| `GET /api/health` | 保留 | 健康检查 |

### `GET /api/ontology/files`

**响应：**
```json
{
  "files": [
    { "name": "Resource.owl", "displayName": "Resource" },
    { "name": "多要素生产环境建模.owl", "displayName": "多要素生产环境建模" }
  ]
}
```

- 扫描路径：`{ContentRoot}/../ontology/` 和 `{ContentRoot}/ontology/`
- 过滤：仅 `.owl` 扩展名
- `displayName` = 去掉扩展名的文件名

### `GET /api/ontology/load?file=xxx.owl`

**参数：** `file` — 文件名（不含路径），必须存在于 `ontology/` 目录

**响应：** `OntologyDto`（与现有结构一致）

**安全：** 拒绝含 `..`、`/`、`\` 的文件名（400 Bad Request）

**缓存：** `ConcurrentDictionary<string, OntologyDto>`，同文件只解析一次

**错误：**
- 文件不存在 → 404
- 解析失败 → 400 + error message

---

## 前端

### api.ts 新增

```ts
listOntologyFiles(): Promise<{files: {name: string, displayName: string}[]}>
loadOntology(fileName: string): Promise<Ontology>
```

### store 新增

```ts
currentFile: string | null
fileList: {name: string, displayName: string}[]
```

### App.svelte 改动

**工具栏：**
- 移除 "Load bundled ontology" 按钮
- 新增 `<select>` 下拉框，选项来自 `fileList`
- `onMount`：先调 `/files` → 自动选中第一项 → 调 `/load` 加载
- 切换下拉框 → 调 `/load?file=xxx`
- 保留 "Open OWL file…" 按钮及拖拽上传

**上传文件交互：**
- 上传成功后在 `fileList` 中追加一项（标记来源为外部，不下拉框中持久化）
- 外部文件切换回目录文件后从列表中移除

---

## 错误处理

| 场景 | 行为 |
|------|------|
| `ontology/` 目录为空 | 下拉框显示 "No files"，仅保留上传入口 |
| 请求文件不存在 | API 返回 404，前端 toast 提示 |
| 文件名含路径穿越 (`..`, `/`, `\`) | API 返回 400 |
| 解析失败 | API 返回 400 + 错误信息，前端显示 error banner |
| 切换到错误文件后 | 保持当前视图不变，显示错误 |

---

## 文件变更清单

| 文件 | 变更 |
|------|------|
| `server/Program.cs` | 新增 `/files`、`/load` 端点；移除 `/default`、`/source`；新增缓存字典 |
| `client/src/lib/api.ts` | 新增 `listOntologyFiles()`、`loadOntology()` |
| `client/src/lib/store.svelte.ts` | 新增 `currentFile`、`fileList` 状态 |
| `client/src/App.svelte` | 工具栏：按钮 → 下拉框；onMount 逻辑调整 |
