<script setup lang="ts">
import { computed } from 'vue'
import type { ProductDetails } from '@/types/api'
import { formatQuantity } from '@/utils/inventory'

const props = defineProps<{ before: ProductDetails | null; after: ProductDetails; all: boolean; quantity: number }>()
const fields: {key: keyof ProductDetails | 'quantity'; label: string}[] = [
  {key:'name', label:'商品名称'}, {key:'code', label:'商品编码'}, {key:'category', label:'商品类目'},
  {key:'productSpecification', label:'产品规格'}, {key:'rawMaterialSpecification', label:'原料规格'},
  {key:'unit', label:'单位'}, {key:'note', label:'备注'}, {key:'isActive', label:'状态'},
  {key:'warningQuantity', label:'预警数量'}, {key:'quantity', label:'账面库存'},
]
function display(details: ProductDetails | null, key: typeof fields[number]['key']) {
  if (!details) return '—'
  if (key === 'quantity') return `${formatQuantity(props.quantity)} ${details.unit}`
  const value = details[key]
  if (key === 'isActive') return value ? '启用' : '停用'
  if (key === 'warningQuantity') return typeof value === 'number' ? `${formatQuantity(value)} ${details.unit}` : '未设置'
  return value || (key === 'category' ? '未分类' : key === 'code' ? '保存时自动生成' : '未填写')
}
const rows = computed(() => fields.map(field => {
  const {key} = field
  const old = display(props.before, key), next = display(props.after, key)
  const changed = props.before ? (key !== 'quantity' && props.before[key] !== props.after[key]) || old !== next
    : key === 'quantity' || (props.after[key] !== '' && props.after[key] !== null)
  return { ...field, changed, old, next }
}).filter(row => props.all || row.changed))
</script>
<template>
  <table class="comparison">
    <colgroup><col class="labels"><col><col class="arrows"><col></colgroup>
    <thead><tr><th>字段</th><th>旧商品信息</th><th aria-hidden="true"></th><th>新商品信息</th></tr></thead>
    <tbody><tr v-for="row in rows" :key="row.key" :class="{changed:row.changed}">
      <th scope="row">{{row.label}}</th><td>{{row.old}}</td>
      <td class="arrow" aria-hidden="true">{{row.changed ? '→' : ''}}</td><td class="new">{{row.next}}</td>
    </tr></tbody>
  </table>
</template>
<style scoped>
.comparison{border-collapse:collapse;width:100%;table-layout:fixed;border-top:1px solid #dce7ec;font-size:13px;line-height:1.5;color:#536c7b}
.labels{width:18%}.arrows{width:24px}.comparison th,.comparison td{text-align:left;vertical-align:top;overflow-wrap:anywhere;white-space:pre-wrap;padding:7px 10px;border-bottom:1px solid #e6eef1}
.comparison thead{background:#eef5f8;color:#38586a}.comparison th{font-weight:600}.comparison tbody th{color:#345264}
.comparison td.arrow{text-align:center;padding:7px 0}.changed .arrow,.changed .new{color:#116963;font-weight:650}.changed .new{background:#f0f8f5}
@media(max-width:600px){.labels{width:21%}.arrows{width:16px}.comparison th,.comparison td{padding:6px}}
</style>
