<script setup lang="ts">
import { onMounted, ref } from 'vue'
const mounted = ref(false)
onMounted(() => { mounted.value = true })
defineProps<{
  eyebrow?: string
  title: string
  description?: string
}>()
</script>

<template>
  <Teleport v-if="mounted" to="#workspace-page-header">
  <header class="page-header">
    <div>
      <p v-if="eyebrow" class="eyebrow">{{ eyebrow }}</p>
      <h1>{{ title }}</h1>
      <p v-if="description" class="description">{{ description }}</p>
    </div>
    <div class="actions"><slot /></div>
  </header>
  </Teleport>
</template>

<style scoped>
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
  width: 100%;
}
.page-header > div:first-child { min-width: 0; }

.eyebrow {
  margin: 0 0 3px;
  color: var(--ice-600);
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

h1 {
  margin: 0;
  color: var(--ink-900);
  font-size: clamp(22px, 1.8vw, 26px);
  line-height: 1.24;
  letter-spacing: -0.02em;
}

.description {
  margin: 4px 0 0;
  color: var(--ink-500);
  font-size: 14px;
  line-height: 1.5;
}

.actions {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
  flex-wrap: wrap;
}
.actions:empty { display: none; }

@media (max-width: 640px) {
  .page-header {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
