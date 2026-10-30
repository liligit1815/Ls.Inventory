<script setup lang="ts" generic="T extends object">
import { computed, nextTick, ref, watch } from 'vue'
import type { CSSProperties } from 'vue'
import type { TableInstance } from 'element-plus'
import { defaultTablePreferences, readTablePreferences, tableCellValue, type TableColumn } from '@/utils/tableColumns'

const props = defineProps<{
  data: T[]
  columns: TableColumn[]
  storageKey: string
  rowKey?: string
  rowStyle?: (args: { row: T; rowIndex: number }) => CSSProperties
  maxHeight?: string | number
  emptyText?: string
  selectable?: (row: T, index: number) => boolean
  selection?: boolean
}>()
const emit = defineEmits<{ 'selection-change': [rows: T[]] }>()
const table = ref<TableInstance>()
const preferences = ref(defaultTablePreferences(props.columns))
const storageName = () => `ls-inventory:table:${props.storageKey}`
watch(() => props.storageKey, () => {
  try { preferences.value = readTablePreferences(props.columns, localStorage.getItem(storageName())) }
  catch { preferences.value = defaultTablePreferences(props.columns) }
}, { immediate: true })
const visibleColumns = computed(() => props.columns.filter(column => column.required || !preferences.value.hidden.includes(column.key)))
function persist() {
  try { localStorage.setItem(storageName(), JSON.stringify(preferences.value)) } catch { /* Table remains usable when storage is unavailable. */ }
}
async function setVisible(column: TableColumn, visible: boolean) {
  if (column.required || (!visible && visibleColumns.value.length <= 1)) return
  preferences.value.hidden = preferences.value.hidden.filter(key => key !== column.key)
  if (!visible) preferences.value.hidden.push(column.key)
  persist()
  await nextTick()
  table.value?.doLayout()
}
function resizeColumn(width: number, _oldWidth: number, column: { columnKey?: string }) {
  const config = props.columns.find(item => item.key === column.columnKey)
  if (!config || !Number.isFinite(width)) return
  preferences.value.widths[config.key] = Math.round(Math.min(2000, Math.max(config.minWidth ?? 60, width)))
  persist()
}
async function resetColumns() {
  preferences.value = defaultTablePreferences(props.columns)
  persist()
  await nextTick()
  table.value?.doLayout()
}
defineExpose({ clearSelection: () => table.value?.clearSelection() })
</script>

<template>
  <div class="configurable-product-table">
    <div class="table-tools">
      <el-popover placement="bottom-end" trigger="click" :width="220">
        <template #reference><el-button size="small" aria-label="调整显示列">显示列</el-button></template>
        <div class="column-settings">
          <strong>选择显示列</strong>
          <el-checkbox v-for="column in columns" :key="column.key" :model-value="column.required || !preferences.hidden.includes(column.key)" :disabled="column.required || (visibleColumns.length === 1 && !preferences.hidden.includes(column.key))" @change="setVisible(column, $event === true)">{{column.label}}</el-checkbox>
          <el-button size="small" @click="resetColumns">恢复默认</el-button>
        </div>
      </el-popover>
    </div>
    <el-table ref="table" class="product-typography compact-product-table" :data="data" :row-key="rowKey" :row-style="rowStyle" :max-height="maxHeight" :empty-text="emptyText ?? '没有符合筛选条件的商品'" border size="small" @header-dragend="resizeColumn" @selection-change="emit('selection-change', $event)">
      <el-table-column v-if="selection" type="selection" :width="42" :reserve-selection="true" :selectable="selectable" />
      <el-table-column v-for="column in visibleColumns" :key="column.key" :column-key="column.key" :prop="column.prop" :label="column.label" :width="preferences.widths[column.key] ?? column.width" :min-width="column.minWidth" :class-name="column.className" :fixed="column.fixed" :resizable="true">
        <!-- Element Plus probes column slots with an empty row and index -1. -->
        <template #default="scope"><slot v-if="scope.$index >= 0" :name="column.key" :row="scope.row" :index="scope.$index">{{tableCellValue(scope.row, column.prop)}}</slot></template>
      </el-table-column>
    </el-table>
  </div>
</template>

<style scoped>
.configurable-product-table{min-width:0;width:100%}
.table-tools{display:flex;justify-content:flex-end;margin-bottom:6px}
.column-settings{display:flex;flex-direction:column;gap:2px;max-height:65vh;overflow:auto}
.column-settings>strong{margin-bottom:6px}
.column-settings .el-checkbox{margin:0;min-height:28px;height:auto}
.column-settings>.el-button{align-self:flex-start;margin-top:8px}
</style>
