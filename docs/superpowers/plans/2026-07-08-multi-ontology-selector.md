# Multi-Ontology File Selector — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace single default ontology loading with a dropdown selector that lists all `.owl` files in the `ontology/` directory, with caching per-file.

**Architecture:** Two new backend endpoints (`/files` for listing, `/load` for per-file loading with cache), frontend dropdown replaces the "Load bundled ontology" button in the top toolbar. External file upload remains available.

**Tech Stack:** .NET 10 minimal API, Svelte 5 runes, TypeScript, dotNetRDF

## Global Constraints

- All new endpoints use the existing `OntologyParser` service
- Ontology directory resolved via `{ContentRoot}/../ontology/` and `{ContentRoot}/ontology/` (existing pattern)
- File names must not contain `..`, `/`, or `\` (security: directory traversal prevention)
- Cache uses `ConcurrentDictionary<string, OntologyDto>` for thread safety
- Chinese file names must display correctly (existing `LocalName` fix already handles this)
- Frontend uses Svelte 5 `$state` runes (existing pattern)

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `server/Program.cs` | Modify | New endpoints; remove old; add cache; update ontology path resolution |
| `client/src/lib/api.ts` | Modify | Add `listOntologyFiles()`, `loadOntology()` |
| `client/src/lib/store.svelte.ts` | Modify | Add `currentFile`, `fileList` state |
| `client/src/App.svelte` | Modify | Replace "Load bundled" button with dropdown; adjust onMount |

---

### Task 1: Backend — `GET /api/ontology/files` endpoint

**Files:**
- Modify: `server/Program.cs` (add endpoint after existing `parser`/`logger` resolution)

**Interfaces:**
- Consumes: `OntologyParser` (already registered), `ILogger<Program>` (already resolved)
- Produces: `GET /api/ontology/files` → `{ files: { name: string, displayName: string }[] }`

- [ ] **Step 1: Add ontology directory resolution helper and `/files` endpoint**

In `server/Program.cs`, after the existing `var parser = ...` line (line 35), add a shared helper to resolve the ontology directory, then add the `/files` endpoint after the existing `/api/health` endpoint:

```csharp
// Shared helper to resolve the ontology directory.
string? ResolveOntologyDir()
{
    var candidates = new[]
    {
        Path.Combine(app.Environment.ContentRootPath, "..", "ontology"),
        Path.Combine(app.Environment.ContentRootPath, "ontology"),
    };
    foreach (var c in candidates)
        if (Directory.Exists(c))
            return Path.GetFullPath(c);
    return null;
}

// -- add this after app.MapGet("/api/health", ...) --

app.MapGet("/api/ontology/files", () =>
{
    var dir = ResolveOntologyDir();
    if (dir == null)
        return Results.Ok(new { files = Array.Empty<object>() });

    var files = Directory.GetFiles(dir, "*.owl")
        .Select(f => new
        {
            name = Path.GetFileName(f),
            displayName = Path.GetFileNameWithoutExtension(f)
        })
        .OrderBy(f => f.displayName)
        .ToList();

    return Results.Ok(new { files });
});
```

- [ ] **Step 2: Test the endpoint**

```powershell
Invoke-RestMethod -Uri "http://localhost:5174/api/ontology/files" | ConvertTo-Json
```

Expected: JSON object with `files` array containing `{name, displayName}` for each `.owl` file in the ontology directory.

- [ ] **Step 3: Commit**

```bash
git add server/Program.cs
git commit -m "feat: add GET /api/ontology/files endpoint to list .owl files"
```

---

### Task 2: Backend — `GET /api/ontology/load` endpoint with caching

**Files:**
- Modify: `server/Program.cs` (add cache field and endpoint)

**Interfaces:**
- Consumes: `OntologyParser.ParseFile(string path)`, `ResolveOntologyDir()` from Task 1
- Produces: `GET /api/ontology/load?file=xxx.owl` → `OntologyDto`

- [ ] **Step 1: Add the cache and `/load` endpoint**

In `server/Program.cs`, add the cache field right after `var logger = ...` (near line 35):

```csharp
// Cache parsed ontologies by file name for fast switching.
var ontologyCache = new System.Collections.Concurrent.ConcurrentDictionary<string, OntologyDto>();
```

Then add the `/load` endpoint after the `/files` endpoint from Task 1:

```csharp
app.MapGet("/api/ontology/load", (string file) =>
{
    // Security: reject path traversal attempts.
    if (string.IsNullOrWhiteSpace(file) || file.Contains("..") || file.Contains('/') || file.Contains('\\'))
        return Results.BadRequest(new { error = "Invalid file name." });

    if (ontologyCache.TryGetValue(file, out var cached))
        return Results.Ok(cached);

    var dir = ResolveOntologyDir();
    if (dir == null)
        return Results.NotFound(new { error = "Ontology directory not found." });

    var path = Path.Combine(dir, file);
    if (!File.Exists(path))
        return Results.NotFound(new { error = $"File '{file}' not found." });

    try
    {
        logger.LogInformation("Parsing ontology: {Path}", path);
        var dto = parser.ParseFile(path);
        ontologyCache[file] = dto;
        return Results.Ok(dto);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to parse ontology {File}", file);
        return Results.BadRequest(new { error = $"Could not parse '{file}': {ex.Message}" });
    }
}).Produces<OntologyDto>(StatusCodes.Status200OK)
  .Produces(StatusCodes.Status400BadRequest)
  .Produces(StatusCodes.Status404NotFound);
```

- [ ] **Step 2: Test the endpoint — load existing file**

```powershell
Invoke-RestMethod -Uri "http://localhost:5174/api/ontology/load?file=Resource.owl" | Select-Object -ExpandProperty Meta
```

Expected: Returns `OntologyDto` with `sourceName: "Resource.owl"`, IRI, stats.

- [ ] **Step 3: Test — verify caching (second call should be fast)**

```powershell
Measure-Command { Invoke-RestMethod -Uri "http://localhost:5174/api/ontology/load?file=Resource.owl" | Out-Null }
```

Expected: Second call ~0ms (cached), log only shows "Parsing ontology" once.

- [ ] **Step 4: Test — invalid file name (directory traversal)**

```powershell
Invoke-RestMethod -Uri "http://localhost:5174/api/ontology/load?file=../secret.txt"
```

Expected: 400 Bad Request with `{ "error": "Invalid file name." }`

- [ ] **Step 5: Test — non-existent file**

```powershell
Invoke-RestMethod -Uri "http://localhost:5174/api/ontology/load?file=nonexistent.owl"
```

Expected: 404 Not Found

- [ ] **Step 6: Commit**

```bash
git add server/Program.cs
git commit -m "feat: add GET /api/ontology/load with ConcurrentDictionary caching"
```

---

### Task 3: Backend — Remove old endpoints, update default loading

**Files:**
- Modify: `server/Program.cs` (remove `/default`, `/source` endpoints and related helpers)

**Interfaces:**
- Consumes: Cache from Task 2
- Removes: `GET /api/ontology/default`, `GET /api/ontology/source`, `ResolveDefaultOntology()`, `GetDefault()`, `defaultCache`

- [ ] **Step 1: Remove old endpoints and helpers**

In `server/Program.cs`, delete the following blocks (approximately lines 40-83):

1. Delete `string? ResolveDefaultOntology() { ... }` function
2. Delete `OntologyDto? defaultCache = null;` field
3. Delete `OntologyDto? GetDefault() { ... }` function
4. Delete `app.MapGet("/api/ontology/default", ...)` endpoint
5. Delete `app.MapGet("/api/ontology/source", ...)` endpoint

The file should now have: health endpoint, files endpoint, load endpoint, parse (upload) endpoint, and fallback.

- [ ] **Step 2: Verify old endpoints return 404**

```powershell
Invoke-RestMethod -Uri "http://localhost:5174/api/ontology/default"
```

Expected: Returns `index.html` (SPA fallback) — the endpoint no longer exists as an API route.

- [ ] **Step 3: Compile check**

```powershell
dotnet build server/ResourceOntology.Api.csproj
```

Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add server/Program.cs
git commit -m "refactor: remove deprecated /default and /source endpoints"
```

---

### Task 4: Frontend — `api.ts` new functions

**Files:**
- Modify: `client/src/lib/api.ts`

**Interfaces:**
- Consumes: None (standalone fetch functions)
- Produces: `listOntologyFiles()`, `loadOntology(fileName)`

- [ ] **Step 1: Add `listOntologyFiles` and `loadOntology` functions**

In `client/src/lib/api.ts`, add these functions after the existing `loadDefaultSource` function:

```typescript
/** File entry returned by GET /api/ontology/files. */
export interface OntologyFileEntry {
  name: string
  displayName: string
}

/** List all .owl files in the server's ontology directory. */
export function listOntologyFiles(): Promise<{ files: OntologyFileEntry[] }> {
  return fetch('/api/ontology/files').then((r) => asJson<{ files: OntologyFileEntry[] }>(r))
}

/** Load a specific ontology file from the server's ontology directory. */
export function loadOntology(fileName: string): Promise<Ontology> {
  return fetch(`/api/ontology/load?file=${encodeURIComponent(fileName)}`).then((r) => asJson<Ontology>(r))
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```powershell
cd client; npx tsc --noEmit
```

Expected: No type errors.

- [ ] **Step 3: Commit**

```bash
git add client/src/lib/api.ts
git commit -m "feat: add listOntologyFiles and loadOntology API functions"
```

---

### Task 5: Frontend — `store.svelte.ts` new state

**Files:**
- Modify: `client/src/lib/store.svelte.ts`

**Interfaces:**
- Consumes: `OntologyFileEntry` from Task 4 (imported via api.ts)
- Produces: `store.fileList`, `store.currentFile`

- [ ] **Step 1: Add `fileList` and `currentFile` to store**

In `client/src/lib/store.svelte.ts`, add to the `store` object after the existing `filters` field:

```typescript
import type { OntologyFileEntry } from './api'

// ... inside the $state({ ... }) object, add after the filters block:

  /** Available ontology files from the server directory. */
  fileList: [] as OntologyFileEntry[],
  /** Currently selected ontology file name (e.g. "Resource.owl"), or null if uploaded. */
  currentFile: null as string | null,
```

The store object should look like:

```typescript
export const store = $state({
  ontology: null as Ontology | null,
  loading: true,
  error: null as string | null,
  selection: null as Selection | null,
  search: '',
  focusRequest: null as { id: string; nonce: number } | null,
  filters: {
    showClasses: true,
    showIndividuals: false,
    subClassOf: true,
    disjoint: false,
    restriction: true,
    domainRange: true,
    typeOf: true,
    assertion: true,
  } as GraphFilters,
  fileList: [] as OntologyFileEntry[],
  currentFile: null as string | null,
})
```

- [ ] **Step 2: Verify TypeScript compiles**

```powershell
cd client; npx tsc --noEmit
```

Expected: No type errors.

- [ ] **Step 3: Commit**

```bash
git add client/src/lib/store.svelte.ts
git commit -m "feat: add fileList and currentFile to store"
```

---

### Task 6: Frontend — `App.svelte` dropdown selector

**Files:**
- Modify: `client/src/App.svelte`

**Interfaces:**
- Consumes: `listOntologyFiles`, `loadOntology`, `loadDefaultOntology` (keep for now), `parseOntologyFile` from api.ts; `store` from store.svelte.ts
- Produces: Dropdown selector replacing "Load bundled ontology" button

- [ ] **Step 1: Update imports and onMount logic**

In `client/src/App.svelte`, update the import line:

```svelte
<script lang="ts">
  import { onMount } from 'svelte'
  import { store } from './lib/store.svelte'
  import { loadDefaultOntology, parseOntologyFile, listOntologyFiles, loadOntology } from './lib/api'
  import GraphView from './lib/GraphView.svelte'
  import Sidebar from './lib/Sidebar.svelte'
  import DetailsPanel from './lib/DetailsPanel.svelte'
```

Replace the `loadDefault` function with:

```typescript
  let fileInput: HTMLInputElement
  let dragging = $state(false)

  async function initFiles() {
    try {
      const res = await listOntologyFiles()
      store.fileList = res.files
    } catch {
      store.fileList = []
    }
  }

  async function selectFile(fileName: string) {
    store.loading = true
    store.error = null
    store.currentFile = fileName
    try {
      store.ontology = await loadOntology(fileName)
      store.selection = null
    } catch (e) {
      store.error = e instanceof Error ? e.message : String(e)
    } finally {
      store.loading = false
    }
  }
```

Replace `onMount(loadDefault)` with:

```typescript
  onMount(async () => {
    await initFiles()
    // Auto-select the first file in the list.
    if (store.fileList.length > 0) {
      await selectFile(store.fileList[0].name)
    }
  })
```

- [ ] **Step 2: Replace the "Load bundled ontology" button with a dropdown**

In the `<header>` section, find the two buttons (around lines 105-115):

Replace:
```svelte
    <div class="ml-auto flex items-center gap-2">
      <button
        class="rounded-md border border-edge bg-canvas px-3 py-1.5 text-xs text-ink hover:bg-panel2"
        onclick={loadDefault}>Load bundled ontology</button
      >
      <button
        class="rounded-md bg-klass px-3 py-1.5 text-xs font-medium text-canvas hover:opacity-90"
        onclick={() => fileInput.click()}>Open OWL file…</button
      >
      <input bind:this={fileInput} type="file" accept=".owl,.rdf,.xml" class="hidden" onchange={onPick} />
    </div>
```

With:
```svelte
    <div class="ml-auto flex items-center gap-2">
      {#if store.fileList.length > 0}
        <select
          class="rounded-md border border-edge bg-canvas px-3 py-1.5 text-xs text-ink outline-none hover:border-klass focus:border-klass"
          value={store.currentFile ?? ''}
          onchange={(e) => {
            const val = (e.target as HTMLSelectElement).value
            if (val) selectFile(val)
          }}
        >
          {#each store.fileList as f}
            <option value={f.name}>{f.displayName}</option>
          {/each}
        </select>
      {:else}
        <span class="text-xs text-muted">No ontology files found</span>
      {/if}
      <button
        class="rounded-md bg-klass px-3 py-1.5 text-xs font-medium text-canvas hover:opacity-90"
        onclick={() => fileInput.click()}>Open OWL file…</button
      >
      <input bind:this={fileInput} type="file" accept=".owl,.rdf,.xml" class="hidden" onchange={onPick} />
    </div>
```

- [ ] **Step 3: Update header subtitle to show current file**

Find the subtitle line (around line 88):

```svelte
        <p class="text-[11px] leading-tight text-muted">
          {store.ontology?.meta.sourceName ?? 'Describing Visualization Resources'}
        </p>
```

Keep as-is — the `sourceName` from the parser already reflects the loaded file name.

- [ ] **Step 4: Verify TypeScript compiles**

```powershell
cd client; npx tsc --noEmit
```

Expected: No type errors.

- [ ] **Step 5: Build and run the full app**

```powershell
.\run.ps1
```

Expected: Server starts, dropdown shows available `.owl` files, selecting switches the graph/sidebar.

- [ ] **Step 6: Commit**

```bash
git add client/src/App.svelte
git commit -m "feat: replace bundled ontology button with file dropdown selector"
```

---

### Task 7: End-to-end verification

**Files:**
- None (manual testing)

- [ ] **Step 1: Verify dropdown shows both files**

Open `http://localhost:5174` → dropdown should show "Resource" and "多要素生产环境建模".

- [ ] **Step 2: Verify switching between files**

Select "多要素生产环境建模" → graph and sidebar should update with Chinese class names (员工, 工厂, etc.). Switch back to "Resource" → should show Resource ontology classes.

- [ ] **Step 3: Verify upload still works**

Click "Open OWL file…" → select an external `.owl` file → should parse and display.

- [ ] **Step 4: Verify caching (no re-parse on re-select)**

In server logs, switch back to a previously loaded file → log should NOT show "Parsing ontology" again.

- [ ] **Step 5: Verify error handling**

Manually delete a `.owl` file, select it from dropdown → should show error banner.

- [ ] **Step 6: Commit (if any fixes made)**

```bash
git add -A
git commit -m "chore: end-to-end verification of multi-ontology selector"
```
