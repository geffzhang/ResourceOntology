<script lang="ts">
  import { onMount } from 'svelte'
  import { store } from './lib/store.svelte'
  import { loadDefaultOntology, parseOntologyFile } from './lib/api'
  import GraphView from './lib/GraphView.svelte'
  import Sidebar from './lib/Sidebar.svelte'
  import DetailsPanel from './lib/DetailsPanel.svelte'

  let fileInput: HTMLInputElement
  let dragging = $state(false)

  async function loadDefault() {
    store.loading = true
    store.error = null
    try {
      store.ontology = await loadDefaultOntology()
      store.selection = null
    } catch (e) {
      store.error = e instanceof Error ? e.message : String(e)
    } finally {
      store.loading = false
    }
  }

  async function loadFile(file: File) {
    store.loading = true
    store.error = null
    try {
      store.ontology = await parseOntologyFile(file)
      store.selection = null
    } catch (e) {
      store.error = e instanceof Error ? e.message : String(e)
    } finally {
      store.loading = false
    }
  }

  function onPick(e: Event) {
    const f = (e.target as HTMLInputElement).files?.[0]
    if (f) loadFile(f)
  }
  function onDrop(e: DragEvent) {
    e.preventDefault()
    dragging = false
    const f = e.dataTransfer?.files?.[0]
    if (f) loadFile(f)
  }

  onMount(loadDefault)

  const stats = $derived(store.ontology?.stats)
  const chips = $derived(
    stats
      ? [
          { label: 'Classes', value: stats.classes, color: 'bg-klass' },
          { label: 'Obj. props', value: stats.objectProperties, color: 'bg-objprop' },
          { label: 'Data props', value: stats.datatypeProperties, color: 'bg-datatype' },
          { label: 'Instances', value: stats.individuals, color: 'bg-individual' },
          { label: 'Restrictions', value: stats.restrictions, color: 'bg-restriction' },
          { label: 'Disjoint', value: stats.disjointAxioms, color: 'bg-disjoint' },
        ]
      : []
  )
</script>

<svelte:window
  ondragover={(e) => {
    e.preventDefault()
    dragging = true
  }}
  ondragleave={() => (dragging = false)}
  ondrop={onDrop}
/>

<div class="flex h-screen flex-col">
  <!-- Header -->
  <header class="flex flex-wrap items-center gap-x-4 gap-y-2 border-b border-edge bg-panel px-4 py-2">
    <div class="flex items-center gap-2">
      <div class="grid h-8 w-8 place-items-center rounded-md bg-klass/20 text-lg text-klass">◈</div>
      <div>
        <h1 class="text-sm font-semibold leading-tight text-ink">Resource Ontology Visualiser</h1>
        <p class="text-[11px] leading-tight text-muted">
          {store.ontology?.meta.sourceName ?? 'Describing Visualization Resources'}
        </p>
      </div>
    </div>

    <!-- Stat chips -->
    <div class="flex flex-wrap items-center gap-1.5">
      {#each chips as c}
        <span class="flex items-center gap-1.5 rounded-full border border-edge bg-canvas px-2 py-0.5 text-xs">
          <span class="h-2 w-2 rounded-full {c.color}"></span>
          <span class="text-muted">{c.label}</span>
          <span class="font-semibold text-ink">{c.value}</span>
        </span>
      {/each}
    </div>

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
  </header>

  <!-- Body -->
  <div class="relative flex min-h-0 flex-1">
    {#if store.loading}
      <div class="absolute inset-0 z-20 grid place-items-center bg-canvas/70 backdrop-blur-sm">
        <div class="flex items-center gap-3 text-muted">
          <span class="h-5 w-5 animate-spin rounded-full border-2 border-edge border-t-klass"></span>
          Parsing ontology…
        </div>
      </div>
    {/if}

    {#if store.error}
      <div
        class="absolute left-1/2 top-4 z-30 -translate-x-1/2 rounded-md border border-disjoint/50 bg-disjoint/15 px-4 py-2 text-sm text-ink shadow-lg"
      >
        {store.error}
      </div>
    {/if}

    <aside class="w-72 shrink-0 border-r border-edge bg-panel"><Sidebar /></aside>
    <main class="min-w-0 flex-1"><GraphView /></main>
    <aside class="w-80 shrink-0 border-l border-edge bg-panel"><DetailsPanel /></aside>
  </div>
</div>

{#if dragging}
  <div class="pointer-events-none fixed inset-0 z-50 grid place-items-center bg-canvas/80">
    <div class="rounded-xl border-2 border-dashed border-klass px-10 py-8 text-lg text-klass">
      Drop an .owl file to visualise
    </div>
  </div>
{/if}
