<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, DailyMovements, Product } from '@/types/api'
import MovementQuickActions from '@/components/MovementQuickActions.vue'
import { PRODUCT_PAGE_SIZE, formatQuantity, isLowStock, matchesProduct, productLabel } from '@/utils/inventory'
import { productStyle } from '@/utils/productColors'
import { calendarDates, movementIndex, overviewProducts, readOverviewDays } from '@/utils/movementOverview'

const props = defineProps<{ products: Product[]; refreshKey: number; entryLocked?: boolean; category?: string | null }>()
const emit = defineEmits<{ entry: [kind: 'Inbound' | 'Outbound', productId?: string] }>()
const daysStorageKey = 'ls-inventory:workbench-days'
function savedDays() {
  try { return readOverviewDays(localStorage.getItem(daysStorageKey)) } catch { return 7 }
}
const days = ref<number | undefined>(savedDays())
const selectedIds = ref<string[]>([]), search = ref(''), onlyWithActivity = ref(false)
const data = ref<DailyMovements>(), loading = ref(false), error = ref('')
const page = ref(1), pageSize = PRODUCT_PAGE_SIZE
const tableScroll = ref<HTMLElement>()
const tableElement = ref<HTMLTableElement>(), horizontalScroll = ref<HTMLElement>()
const tableHeaderScroll = ref<HTMLElement>()
const scrollContentWidth = ref(0)
const pageHeaderHeight = ref(0)
let resizeObserver: ResizeObserver | undefined
let followRightEdge = true
const mirroredScrollPositions = new WeakMap<HTMLElement, number>()
let userScrollRevision = 0, measurementRevision = 0, measuredClientWidth = 0
const productDetailId = ref(''), productDetailOpen = ref(false)
const detailProduct = computed(() => props.products.find(p => p.id === productDetailId.value))
const options = computed(() => props.products.filter(p => matchesProduct(p, search.value)))
const shownProducts = computed(() => overviewProducts(props.products, data.value?.entries ?? [], selectedIds.value, onlyWithActivity.value))
const pagedProducts = computed(() => shownProducts.value.slice((page.value - 1) * pageSize, page.value * pageSize))
const dates = computed(() => data.value ? calendarDates(data.value.from, data.value.to) : [])
const index = computed(() => movementIndex(data.value?.entries ?? []))
const tableRows = computed(() => pagedProducts.value.map(product => ({
  product,
  lowStock: isLowStock(product),
  days: dates.value.map(date => ({ date, movement: index.value.get(date)?.get(product.id) })),
})))
const movementColumns = computed(() => {
  const lengths = new Map(dates.value.map(date => [date, { outbound: 1, inbound: 1 }]))
  const productIds = new Set(pagedProducts.value.map(product => product.id))
  for (const entry of data.value?.entries ?? []) {
    const day = lengths.get(entry.date)
    if (!day || !productIds.has(entry.productId)) continue
    day.outbound = Math.max(day.outbound, formatQuantity(entry.outbound).length)
    day.inbound = Math.max(day.inbound, formatQuantity(entry.inbound).length)
  }
  return dates.value.flatMap(date => (['outbound', 'inbound'] as const).map(direction => ({
    key: `${date}-${direction}`,
    // Reserve cell padding/borders; actions have their own trailing column.
    width: `max(var(--quantity-column-width), calc(${lengths.get(date)![direction]}ch + 12px))`,
  })))
})
const matrixStyle = computed(() => {
  const widths = new Map<string, number>()
  for (const column of movementColumns.value) widths.set(column.width, (widths.get(column.width) ?? 0) + 1)
  return {
    '--date-columns-width': [...widths].map(([width, count]) => `${count} * ${width}`).join(' + ') || '0px',
    '--date-column-count': movementColumns.value.length || 1,
    '--stock-character-count': Math.max(1, ...pagedProducts.value.map(product => formatQuantity(product.quantity).length)),
  }
})

function startEntry(kind: 'Inbound' | 'Outbound', productId?: string) {
  if (props.entryLocked) return
  productDetailOpen.value = false
  emit('entry', kind, productId ?? (selectedIds.value.length === 1 ? selectedIds.value[0] : undefined))
}
function scrollToToday() {
  if (tableScroll.value) writeScrollPosition(tableScroll.value, tableScroll.value.scrollWidth)
  if (horizontalScroll.value) writeScrollPosition(horizontalScroll.value, horizontalScroll.value.scrollWidth)
  syncHeaderScroll()
  followRightEdge = true
}
function writeScrollPosition(element: HTMLElement, left: number) {
  if (Math.abs(element.scrollLeft - left) <= 0.5) return
  element.scrollLeft = left
  // Scroll events arrive later. An echoed write must never pull an active drag back.
  mirroredScrollPositions.set(element, element.scrollLeft)
}
function syncHeaderScroll() {
  if (tableHeaderScroll.value && tableScroll.value && Math.abs(tableHeaderScroll.value.scrollLeft - tableScroll.value.scrollLeft) > 0.5)
    tableHeaderScroll.value.scrollLeft = tableScroll.value.scrollLeft
}
function syncHorizontalScroll(source: 'table' | 'bar') {
  const from = source === 'table' ? tableScroll.value : horizontalScroll.value
  if (!from) return
  const mirrored = mirroredScrollPositions.get(from)
  if (mirrored != null && Math.abs(from.scrollLeft - mirrored) <= 0.5) return
  mirroredScrollPositions.delete(from)
  userScrollRevision++
  followRightEdge = from.scrollWidth - from.clientWidth - from.scrollLeft <= 1
  alignHorizontalScroll(source)
}
function alignHorizontalScroll(source: 'table' | 'bar') {
  const from = source === 'table' ? tableScroll.value : horizontalScroll.value
  const to = source === 'table' ? horizontalScroll.value : tableScroll.value
  if (!from || !to) return
  const max = Math.max(0, from.scrollWidth - from.clientWidth)
  const targetMax = Math.max(0, to.scrollWidth - to.clientWidth)
  const targetLeft = max ? from.scrollLeft / max * targetMax : 0
  writeScrollPosition(to, targetLeft)
  syncHeaderScroll()
}
async function measureTable() {
  if (!tableScroll.value) return
  const current = ++measurementRevision, scrollRevision = userScrollRevision
  const widthChanged = scrollContentWidth.value !== tableScroll.value.scrollWidth || measuredClientWidth !== tableScroll.value.clientWidth
  scrollContentWidth.value = tableScroll.value.scrollWidth
  measuredClientWidth = tableScroll.value.clientWidth
  pageHeaderHeight.value = tableScroll.value.closest('.workspace')?.querySelector('.workspace-header')?.getBoundingClientRect().height ?? 0
  if (!widthChanged) return
  await nextTick()
  if (current !== measurementRevision || scrollRevision !== userScrollRevision) return
  if (followRightEdge) scrollToToday()
  else alignHorizontalScroll('table')
}
function focusToday(productId: string) {
  selectedIds.value = [productId]; onlyWithActivity.value = false; page.value = 1
  if (days.value == null || !Number.isInteger(days.value) || days.value < 1 || days.value > 367) days.value = 7
  void nextTick(() => { scrollToToday(); if (tableScroll.value) tableScroll.value.scrollTop = 0 })
}
defineExpose({ focusToday })
function setDays(value: number) { days.value = value }
function showProduct(product: Product) { productDetailId.value = product.id; productDetailOpen.value = true }
function filterProducts(value: string) { search.value = value }
function closeSearch(visible: boolean) { if (!visible) search.value = '' }

watch(() => props.category, () => { selectedIds.value = []; search.value = ''; onlyWithActivity.value = false; page.value = 1 }, { flush: 'sync' })
watch([selectedIds, onlyWithActivity], () => { page.value = 1 }, { deep: true })
watch(() => shownProducts.value.length, count => { page.value = Math.min(page.value, Math.max(1, Math.ceil(count / pageSize))) })
watch(page, () => { tableScroll.value?.scrollIntoView?.({ block: 'start' }) })
// A new range opens at today; background refreshes retain the scroll position.
watch([tableScroll, () => data.value?.from, () => data.value?.to], () => { if (data.value) void nextTick(scrollToToday) })
watch([tableScroll, tableElement, horizontalScroll, tableHeaderScroll], () => {
  resizeObserver?.disconnect()
  if (!tableScroll.value || !tableElement.value || !horizontalScroll.value) return
  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(() => { void measureTable() })
    resizeObserver.observe(tableScroll.value)
    resizeObserver.observe(tableElement.value)
    const pageHeader = tableScroll.value.closest('.workspace')?.querySelector('.workspace-header')
    if (pageHeader) resizeObserver.observe(pageHeader)
  }
  void measureTable()
})
watch(days, value => {
  if (value != null && Number.isInteger(value) && value >= 1 && value <= 367) {
    try { localStorage.setItem(daysStorageKey, String(value)) } catch { /* Storage may be disabled. */ }
  }
})

let sequence = 0
let loadedQuery = ''
async function loadHistory() {
  const current = ++sequence
  error.value = ''; loading.value = false
  const query = JSON.stringify([days.value, props.category ?? null])
  if (loadedQuery !== query) data.value = undefined
  if (days.value == null || !Number.isInteger(days.value) || days.value < 1 || days.value > 367) {
    data.value = undefined; error.value = '请输入 1～367 之间的整数天数'; return
  }
  loading.value = true
  try {
    const result = (await api.get<ApiResponse<DailyMovements>>('/api/v1/movements/daily', { params: { days: days.value, category: props.category ?? undefined } })).data.data
    if (current === sequence) { data.value = result; loadedQuery = query }
  } catch (e) { if (current === sequence) { data.value = undefined; error.value = getErrorMessage(e) } }
  finally { if (current === sequence) loading.value = false }
}
watch(() => [days.value, props.category, props.refreshKey], () => { void loadHistory() }, { immediate: true })
onBeforeUnmount(() => { sequence++; measurementRevision++; resizeObserver?.disconnect() })
</script>

<template>
  <section class="panel movement-overview" aria-label="出入库概览">
    <div class="overview-filters">
      <el-button-group aria-label="常用展示天数"><el-button size="small" :type="days === 1 ? 'primary' : 'default'" @click="setDays(1)">今天</el-button><el-button size="small" :type="days === 7 ? 'primary' : 'default'" @click="setDays(7)">近7天</el-button><el-button size="small" :type="days === 30 ? 'primary' : 'default'" @click="setDays(30)">近30天</el-button></el-button-group>
      <div class="days-filter" title="展示天数包含今天"><span>最近</span><el-input-number v-model="days" size="small" :min="1" :max="367" :precision="0" :step="1" controls-position="right" aria-label="展示最近多少天的出入库" /><span>天</span></div>
      <div class="overview-product-filters">
        <slot name="category-filter" />
        <div class="product-filter"><label for="overview-product">商品</label>
          <el-select id="overview-product" v-model="selectedIds" size="small" multiple collapse-tags collapse-tags-tooltip :max-collapse-tags="1" filterable :filter-method="filterProducts" @visible-change="closeSearch" clearable placeholder="全部商品" aria-label="概览商品筛选" class="overview-product-filter" popper-class="product-options">
            <el-option v-for="p in options" :key="p.id" :value="p.id" :label="productLabel(p)" :style="productStyle(p.name)"><span class="product-name">{{p.name}}</span><span class="product-spec"> · {{p.productSpecification || '无产品规格'}} · {{p.rawMaterialSpecification || '无原料规格'}} · </span><b class="product-unit">{{p.unit}}</b><span v-if="p.note"> · {{p.note}}</span></el-option>
          </el-select>
        </div>
      </div>
      <el-checkbox v-model="onlyWithActivity" size="small">只看有出入库</el-checkbox>
    </div>
    <slot name="receipt" />
    <div class="overview-body" v-loading="loading" :aria-busy="loading">
      <div v-if="error" class="overview-error" role="alert"><span>{{error}}</span><el-button @click="loadHistory">重新查询</el-button></div>
      <template v-else-if="data">
        <div v-if="shownProducts.length" class="movement-table-frame" :style="{ '--table-header-top': pageHeaderHeight + 'px' }">
        <!-- Keep the visible header outside horizontal overflow so it sticks to the page, with matching columns and scroll position. -->
        <div ref="tableHeaderScroll" class="movement-table-header" aria-hidden="true">
          <table class="movement-table-heading movement-matrix" :style="matrixStyle">
            <colgroup><col class="product-column" /><col class="stock-column" /><col v-for="column in movementColumns" :key="column.key" :style="{ width: `calc(${column.width} + var(--date-extra-width))` }" /><col class="action-column" /></colgroup>
            <thead>
              <tr class="date-header">
                <th rowspan="2" class="fixed-product">商品</th>
                <th rowspan="2" class="fixed-stock">库存数量</th>
                <th v-for="date in dates" :key="date" colspan="2" class="date-group" :class="{ 'today-heading': date === data.to }"><time :datetime="date">{{date}}</time><span v-if="date === data.to" class="today-label">今天</span></th>
                <th rowspan="2" class="action-cell">出入库</th>
              </tr>
              <tr class="direction-header">
                <template v-for="date in dates" :key="date"><th class="outbound day-start" :class="{ 'today-heading': date === data.to }">出</th><th class="inbound" :class="{ 'today-heading': date === data.to }">入</th></template>
              </tr>
            </thead>
          </table>
        </div>
        <div ref="tableScroll" class="movement-table-scroll" tabindex="0" role="region" aria-label="商品每日出入库表，左右滚动查看日期，商品、库存数量和出入库列保持固定" @scroll.passive="syncHorizontalScroll('table')">
          <table ref="tableElement" class="movement-table movement-matrix" :style="matrixStyle">
            <caption class="sr-only">商品每日出入库数量。日期从早到晚排列，今天在日期列的最后，最右侧固定的出入库列可登记今天的出入库；出、入单元格空白表示该项数量为零。当前库存为此刻结存，每日出入库不含库存导入和盘库调整。</caption>
            <colgroup><col class="product-column" /><col class="stock-column" /><col v-for="column in movementColumns" :key="column.key" :style="{ width: `calc(${column.width} + var(--date-extra-width))` }" /><col class="action-column" /></colgroup>
            <thead class="sr-only">
              <tr class="date-header">
                <th rowspan="2" scope="col" class="fixed-product">商品</th>
                <th rowspan="2" scope="col" class="fixed-stock">库存数量</th>
                <th v-for="date in dates" :key="date" colspan="2" scope="colgroup" class="date-group" :class="{ 'today-heading': date === data.to }"><time :datetime="date">{{date}}</time><span v-if="date === data.to" class="today-label">今天</span></th>
                <th rowspan="2" scope="col" class="action-cell">出入库</th>
              </tr>
              <tr class="direction-header">
                <template v-for="date in dates" :key="date"><th scope="col" class="outbound day-start" :class="{ 'today-heading': date === data.to }">出</th><th scope="col" class="inbound" :class="{ 'today-heading': date === data.to }">入</th></template>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in tableRows" :key="row.product.id" :data-product-id="row.product.id" :style="productStyle(row.product.name)" :class="{ 'low-stock-row': row.lowStock }">
                <th scope="row" class="fixed-product product-cell">
                  <button type="button" class="product-detail-link product-name" :aria-label="'查看商品资料：'+productLabel(row.product)" @click="showProduct(row.product)">{{row.product.name}}</button>
                  <div class="product-meta"><span class="product-spec">{{row.product.productSpecification || '无产品规格'}} · {{row.product.rawMaterialSpecification || '无原料规格'}}</span> · <b class="product-unit">{{row.product.unit}}</b><span v-if="row.product.note" class="product-note"> · {{row.product.note}}</span></div>
                </th>
                <td class="fixed-stock stock-cell"><strong class="stock-quantity">{{formatQuantity(row.product.quantity)}}</strong><span v-if="row.lowStock" class="stock-warning">库存不足</span></td>
                <template v-for="day in row.days" :key="day.date">
                  <td class="quantity-cell outbound day-start" :class="{ 'today-cell': day.date === data.to }"><div class="movement-value"><strong :title="day.movement?.outbound ? formatQuantity(day.movement.outbound) : undefined">{{day.movement?.outbound ? formatQuantity(day.movement.outbound) : ''}}</strong></div></td>
                  <td class="quantity-cell inbound" :class="{ 'today-cell': day.date === data.to }"><div class="movement-value"><strong :title="day.movement?.inbound ? formatQuantity(day.movement.inbound) : undefined">{{day.movement?.inbound ? formatQuantity(day.movement.inbound) : ''}}</strong></div></td>
                </template>
                <td class="action-cell"><div class="row-actions">
                  <el-button class="row-action outbound-action" size="small" :disabled="entryLocked || !row.product.isActive" :aria-label="row.product.name+'，今天出库'" @click="startEntry('Outbound', row.product.id)">出</el-button>
                  <el-button class="row-action inbound-action" size="small" :disabled="entryLocked || !row.product.isActive" :aria-label="row.product.name+'，今天入库'" @click="startEntry('Inbound', row.product.id)">入</el-button>
                </div></td>
              </tr>
            </tbody>
          </table>
        </div>
        <div ref="horizontalScroll" class="table-horizontal-scroll" tabindex="0" role="region" aria-label="日期横向滚动条" @scroll.passive="syncHorizontalScroll('bar')"><div :style="{ width: scrollContentWidth + 'px' }" /></div>
        </div>
        <div v-else class="detail-empty"><p>没有符合筛选条件的商品</p><span>可调整类目或商品筛选，或取消“只看有出入库”。</span></div>
        <div class="table-footer"><p class="muted">左右滚动查看往日 · 商品、库存与出入库列固定 · 不含库存导入和盘库调整</p><el-pagination v-if="shownProducts.length" v-model:current-page="page" :page-size="pageSize" :total="shownProducts.length" :pager-count="5" layout="total, prev, pager, next" aria-label="出入库商品分页" /></div>
      </template>
      <p v-else class="muted loading-message">正在查询出入库…</p>
    </div>
    <el-dialog v-model="productDetailOpen" title="商品资料" width="min(540px, 95vw)">
      <dl v-if="detailProduct" class="product-full-details"><dt>商品名称</dt><dd>{{detailProduct.name}}</dd><dt>商品类目</dt><dd>{{detailProduct.category || '未分类'}}</dd><dt>产品规格</dt><dd>{{detailProduct.productSpecification || '无产品规格'}}</dd><dt>原料规格</dt><dd>{{detailProduct.rawMaterialSpecification || '无原料规格'}}</dd><dt>单位</dt><dd>{{detailProduct.unit}}</dd><dt>商品备注</dt><dd>{{detailProduct.note || '无备注'}}</dd><dt>当前库存</dt><dd>{{formatQuantity(detailProduct.quantity)}} {{detailProduct.unit}}<span v-if="!detailProduct.isActive"> · 已停用</span></dd></dl>
      <p class="muted">当前库存为此刻结存；入库、出库均记为今天的新记录。</p>
      <MovementQuickActions v-if="detailProduct" :product="detailProduct" :disabled="entryLocked" @entry="startEntry" />
    </el-dialog>
  </section>
</template>

<style scoped>
.movement-overview{min-width:0;container-type:inline-size;--product-column-width:260px;--stock-column-min-width:76px;--stock-number-size:18;--quantity-column-width:60px;--action-column-width:72px;--date-header-height:32px}


.overview-filters{display:flex;align-items:center;gap:10px;flex-wrap:nowrap;margin-bottom:12px;font-size:13px}.overview-filters>.el-button-group,.overview-filters>.el-checkbox{flex-shrink:0}.overview-filters>.el-checkbox{margin-right:0}.days-filter{display:flex;align-items:center;gap:5px;flex-shrink:0;white-space:nowrap}.days-filter .el-input-number{width:74px}.overview-product-filters{display:contents}.product-filter{display:flex;align-items:center;gap:6px;flex:1 1 190px;min-width:160px;max-width:320px}.product-filter label{font-weight:600;white-space:nowrap}.overview-product-filter{flex:1;min-width:0}
.overview-body{min-height:120px;margin-top:12px;min-width:0}
.movement-table-frame{position:relative;min-width:0}.movement-table-header{position:sticky;top:var(--table-header-top);z-index:10;width:100%;overflow:hidden;border:1px solid #b9d1df;border-bottom:0;border-radius:8px 8px 0 0;background:#edf6fb;box-shadow:0 2px 4px #34596f15;pointer-events:none}.movement-table-scroll{width:100%;overflow-x:auto;overflow-y:hidden;position:relative;isolation:isolate;border:1px solid #b9d1df;border-top:0;scrollbar-width:none;scroll-margin-top:calc(var(--table-header-top) + 64px)}.movement-table-scroll::-webkit-scrollbar{display:none}.movement-table-scroll:focus-visible,.table-horizontal-scroll:focus-visible{outline:2px solid #367ca2;outline-offset:2px}
.table-horizontal-scroll{position:sticky;bottom:0;z-index:6;width:100%;height:16px;overflow-x:scroll;overflow-y:hidden;border:1px solid #b9d1df;border-top:0;border-radius:0 0 5px 5px;background:#edf5fa;scrollbar-color:#87adc6 #edf5fa;scrollbar-width:auto}.table-horizontal-scroll>div{height:1px}.table-horizontal-scroll::-webkit-scrollbar{height:14px}.table-horizontal-scroll::-webkit-scrollbar-track{background:#edf5fa}.table-horizontal-scroll::-webkit-scrollbar-thumb{background:#87adc6;border:2px solid #edf5fa;border-radius:7px}.table-horizontal-scroll::-webkit-scrollbar-thumb:hover{background:#648fa9}
.movement-matrix{--date-extra-width:max(0px,calc((100cqw - 2px - var(--product-column-width) - var(--stock-column-width) - var(--action-column-width) - (var(--date-columns-width))) / var(--date-column-count)));width:100%;min-width:calc(var(--product-column-width) + var(--stock-column-width) + var(--date-columns-width) + var(--action-column-width));table-layout:fixed;border-collapse:separate;border-spacing:0;font-size:14px;font-weight:700;font-variant-numeric:tabular-nums;--stock-column-width:max(var(--stock-column-min-width),calc(var(--stock-character-count) * 1ch * var(--stock-number-size) / 14 + 12px));--product-text-size:14px;--product-unit-size:12px}.product-column{width:var(--product-column-width)}.stock-column{width:var(--stock-column-width)}.action-column{width:var(--action-column-width)}.movement-matrix .action-cell{position:sticky;right:0;z-index:3;border-left:2px solid #9dbccc;box-shadow:-3px 0 5px #34596f10;text-align:center}.row-actions{display:flex;justify-content:center;align-items:center;gap:6px}
.movement-matrix th,.movement-matrix td{border-right:1px solid #d4e3ec;border-bottom:1px solid #c4d8e5;padding:3px 5px;overflow-wrap:anywhere}.movement-matrix thead th{font-size:13px;background:#edf6fb;text-align:center;color:#34596f;white-space:nowrap}.date-header th{height:var(--date-header-height);padding:5px 3px}.direction-header th{height:24px;padding:3px}.movement-matrix .fixed-product{position:sticky;left:0;z-index:2;text-align:left}.movement-matrix .fixed-stock{position:sticky;left:var(--product-column-width);z-index:2;text-align:center;border-right:2px solid #9dbccc;box-shadow:3px 0 5px #34596f10}.movement-matrix thead .fixed-product,.movement-matrix thead .fixed-stock,.movement-matrix thead .action-cell{z-index:5}.movement-matrix thead .fixed-product{padding-left:8px}.movement-matrix .date-group,.movement-matrix .day-start{border-left:1px solid #b9d1df}.movement-matrix tbody tr{--row-background:var(--product-tint,#fff)}.movement-matrix tbody th,.movement-matrix tbody td{background:var(--row-background)}
.movement-matrix .product-cell{padding:3px 8px;font-weight:400}.product-detail-link{border:0;background:transparent;padding:0;text-align:left;cursor:pointer;overflow-wrap:anywhere;line-height:20px!important}.product-detail-link:hover{text-decoration:underline}.product-meta{font-size:12px;line-height:16px}.product-meta .product-spec,.product-meta .product-note{--product-text-size:12px;font-weight:400!important;line-height:16px!important}.product-meta .product-unit{line-height:16px!important}.stock-quantity{font-size:calc(var(--stock-number-size) * 1px);white-space:nowrap;overflow-wrap:normal;line-height:22px;display:block}.stock-warning{display:block;font-size:11px;line-height:14px;font-weight:700;color:#ad3542}.movement-matrix tbody tr.low-stock-row{--row-background:#fff0f0;--product-text-color:#963744}.low-stock-row .stock-quantity{color:#ad3542}.low-stock-row .product-cell{box-shadow:inset 3px 0 #cb5a65}.low-stock-row th,.low-stock-row td{border-bottom-color:#e7bcc1}
.movement-matrix .outbound{color:#955625}.movement-matrix .inbound{color:#247256}.quantity-cell{text-align:center}.movement-value>strong{display:block;min-width:0;font-size:14px;line-height:20px;white-space:nowrap;overflow-wrap:normal}.movement-matrix .today-heading{background:#dceef9}.today-label{display:inline-block;font-size:11px;line-height:16px;color:#276589;background:#c6e3f4;border-radius:3px;padding:0 3px;margin-left:4px}.row-action.el-button{display:inline-flex;margin:0;width:26px;min-width:26px;height:24px;padding:2px 4px;font-size:12px;font-weight:700}.outbound-action{color:#955625;border-color:#dfc5b0;background:#fffaf4}.inbound-action{color:#247256;border-color:#b3d6c5;background:#f1faf5}.row-action.is-disabled{opacity:.6}
.table-footer{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:10px;margin-top:10px}.table-footer p,.table-footer .el-pagination{margin:0}.overview-error{display:flex;align-items:center;gap:16px;padding:20px;color:#ae4650}.loading-message{padding:24px 0}.detail-empty{padding:35px 20px;text-align:center;background:#f6fafc;border:1px dashed #c6dce9;border-radius:10px;color:#607b90}.detail-empty p{margin-bottom:8px}.detail-empty span{font-size:13px}.product-full-details{display:grid;grid-template-columns:90px minmax(0,1fr);gap:12px;line-height:1.7;margin:0 0 20px}.product-full-details dt{color:#657f93}.product-full-details dd{margin:0;font-weight:600;overflow-wrap:anywhere}.sr-only{position:absolute;width:1px;height:1px;padding:0;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0}
@container(max-width:850px){.overview-filters{flex-wrap:wrap}}
@media(max-width:1100px){.movement-overview{--product-column-width:230px;--stock-column-min-width:72px}}
@media(max-width:650px){.movement-overview{--product-column-width:clamp(88px,35cqw,148px);--stock-column-min-width:clamp(60px,20cqw,72px);--stock-number-size:16;--quantity-column-width:58px;padding:14px}.product-filter{max-width:none}.movement-matrix{--product-text-size:13px;--product-unit-size:11px}.movement-matrix th,.movement-matrix td{padding:3px 4px}.movement-matrix .product-cell{padding:3px 5px}.movement-matrix thead .fixed-stock{font-size:12px;padding:3px 2px}.product-meta,.product-meta .product-spec,.product-meta .product-note{font-size:11px;--product-text-size:11px}.table-footer .el-pagination{max-width:100%;flex-wrap:wrap}.movement-matrix .date-group{font-size:12px;padding:5px 2px}}
@media(max-width:420px){.movement-overview{margin-inline:-8px;padding:10px}}
</style>
