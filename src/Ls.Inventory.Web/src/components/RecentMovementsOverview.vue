<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, DailyMovements, Product } from '@/types/api'
import MovementQuickActions from '@/components/MovementQuickActions.vue'
import { formatQuantity, matchesProduct, productLabel, localDate } from '@/utils/inventory'
import { productStyle, productTone } from '@/utils/productColors'
import { calendarDates, calendarPadding, dateLabel, movementIndex, overviewProducts, summarizeDay } from '@/utils/movementOverview'

const props = defineProps<{ products: Product[]; refreshKey: number; entryLocked?: boolean }>()
const emit = defineEmits<{ entry: [kind: 'Inbound' | 'Outbound', productId?: string] }>()
function startEntry(kind: 'Inbound' | 'Outbound', productId?: string) {
  if (props.entryLocked) return
  productDetailOpen.value = false
  emit('entry', kind, productId ?? (selectedIds.value.length === 1 ? selectedIds.value[0] : undefined))
}
function focusToday(productId: string) {
  days.value = 1; selectedIds.value = [productId]; onlyWithActivity.value = false
  detailDate.value = localDate(); detailPage.value = 1
}
defineExpose({ focusToday })
const days = ref<number | undefined>(7), view = ref<'calendar' | 'table' | 'cards'>('calendar')
const selectedIds = ref<string[]>([]), search = ref(''), onlyWithActivity = ref(true)
const data = ref<DailyMovements>(), loading = ref(false), error = ref('')
const detailDate = ref(''), detailPage = ref(1), pageSize = 8
const dateRegion = ref<HTMLElement>()
const productDetailId = ref(''), productDetailOpen = ref(false)
const detailProduct = computed(() => props.products.find(p => p.id === productDetailId.value))
function showProduct(product: Product) { productDetailId.value = product.id; productDetailOpen.value = true }

const options = computed(() => props.products.filter(p => matchesProduct(p, search.value)))
const shownProducts = computed(() => overviewProducts(props.products, data.value?.entries ?? [], selectedIds.value, onlyWithActivity.value))
const dates = computed(() => data.value ? calendarDates(data.value.from, data.value.to) : [])
const padding = computed(() => calendarPadding(dates.value[0]))
const index = computed(() => movementIndex(data.value?.entries ?? []))
const calendarDays = computed(() => dates.value.map(date => ({ date, items: shownProducts.value.flatMap(product => {
  const movement = index.value.get(date)?.get(product.id)
  return movement ? [{ product, movement }] : []
}) })).map(day => ({ ...day, summary: summarizeDay(day.items) })))
const details = computed(() => calendarDays.value.find(day => day.date === detailDate.value)?.items ?? [])
const summaryDays = computed(() => calendarDays.value)
const detailSummary = computed(() => summarizeDay(details.value))
const pagedDetails = computed(() => details.value.slice((detailPage.value - 1) * pageSize, detailPage.value * pageSize))
watch([detailDate, selectedIds, onlyWithActivity], () => { detailPage.value = 1 }, { deep: true })
watch(() => details.value.length, count => { detailPage.value = Math.min(detailPage.value, Math.max(1, Math.ceil(count / pageSize))) })
watch(() => [detailDate.value, view.value, data.value?.from, data.value?.to], async () => {
  await nextTick()
  const region = dateRegion.value
  const selected = region?.querySelector<HTMLElement>('[aria-pressed="true"]')
  if (!region || !selected) return
  const bounds = region.getBoundingClientRect(), target = selected.getBoundingClientRect()
  if (target.top < bounds.top + 36 || target.bottom > bounds.bottom)
    region.scrollTop += target.top - bounds.top - 40
})
function compactCount(value: number) { return value >= 10000 ? new Intl.NumberFormat('zh-CN', { notation: 'compact', maximumFractionDigits: 1 }).format(value) : formatQuantity(value) }
function signedQuantity(value: number) { return (value > 0 ? '+' : '') + formatQuantity(value) }
const counts = computed(() => calendarDays.value.reduce((sum, day) => {
  for (const item of day.items) { sum.inbound += item.movement.inboundCount; sum.outbound += item.movement.outboundCount }
  return sum
}, { inbound: 0, outbound: 0 }))
function filterProducts(value: string) { search.value = value }
function closeSearch(visible: boolean) { if (!visible) search.value = '' }
function showDate(date: string) { detailDate.value = date; detailPage.value = 1 }
function dayAria(day: (typeof calendarDays.value)[number]) { return `${day.date}，${day.items.length}种商品，入库${day.summary.inboundCount}笔，出库${day.summary.outboundCount}笔，查看商品明细` }
let sequence = 0
async function loadHistory() {
  const current = ++sequence
  error.value = ''; loading.value = false
  if (data.value?.days !== days.value) data.value = undefined
  if (days.value == null || !Number.isInteger(days.value) || days.value < 1 || days.value > 367) {
    data.value = undefined; error.value = '请输入 1～367 之间的整数天数'; return
  }
  loading.value = true
  try {
    const result = (await api.get<ApiResponse<DailyMovements>>('/api/v1/movements/daily', { params: { days: days.value } })).data.data
    if (current === sequence) {
      data.value = result
      if (!detailDate.value || detailDate.value < result.from || detailDate.value > result.to) {
        detailDate.value = result.to; detailPage.value = 1
      }
    }
  } catch (e) { if (current === sequence) { data.value = undefined; error.value = getErrorMessage(e) } }
  finally { if (current === sequence) loading.value = false }
}
watch(() => [days.value, props.refreshKey], () => { void loadHistory() }, { immediate: true })
onBeforeUnmount(() => { sequence++ })
</script>

<template>
  <section class="panel movement-overview" aria-label="近日出入库概览">
    <div class="overview-controls">
    <div class="overview-heading">
      <div class="overview-heading-main">
        <div class="overview-title-row"><h2>近日出入库</h2><el-radio-group v-model="view" aria-label="出入库展示方式"><el-radio-button value="calendar">日历</el-radio-button><el-radio-button value="table">表格</el-radio-button><el-radio-button value="cards">每日汇总卡片</el-radio-button></el-radio-group></div>
        <p class="muted">查看商品进出，随手记一笔。新增记录按今天入账。<span class="overview-tip">点击日期查看下方商品明细</span></p>
      </div>
    </div>
    <div class="overview-entry-actions"><el-button type="primary" :disabled="entryLocked" @click="startEntry('Inbound')">记入库</el-button><el-button class="outbound-entry" :disabled="entryLocked" @click="startEntry('Outbound')">记出库</el-button></div>
    <div class="overview-filters">
      <el-button-group aria-label="常用日期范围"><el-button :type="days === 1 ? 'primary' : 'default'" @click="days=1">今天</el-button><el-button :type="days === 7 ? 'primary' : 'default'" @click="days=7">近7天</el-button><el-button :type="days === 30 ? 'primary' : 'default'" @click="days=30">近30天</el-button></el-button-group>
      <div class="days-filter"><span>最近</span><el-input-number v-model="days" :min="1" :max="367" :precision="0" :step="1" controls-position="right" aria-label="查询最近多少天的出入库" /><span>天</span></div>
      <el-select v-model="selectedIds" multiple collapse-tags collapse-tags-tooltip :max-collapse-tags="1" filterable :filter-method="filterProducts" @visible-change="closeSearch" clearable placeholder="全部商品，可搜索名称、规格或备注" aria-label="概览商品筛选" class="overview-product-filter" popper-class="product-options">
        <el-option v-for="p in options" :key="p.id" :value="p.id" :label="productLabel(p)" :style="productStyle(p.name)"><span class="product-name">{{p.name}}</span><span class="product-spec"> · {{p.productSpecification || '无产品规格'}} · {{p.rawMaterialSpecification || '无原料规格'}} · </span><b class="product-unit">{{p.unit}}</b><span v-if="p.note"> · {{p.note}}</span></el-option>
      </el-select>
      <el-checkbox v-model="onlyWithActivity">只看有出入库的商品</el-checkbox>
    </div>
    </div>
    <slot name="receipt" />
    <div class="overview-body" v-loading="loading" :aria-busy="loading">
      <div v-if="error" class="overview-error" role="alert"><span>{{error}}</span><el-button @click="loadHistory">重新查询</el-button></div>
      <template v-else-if="data">
        <div class="range-line"><span>{{data.from}} 至 {{data.to}} · 含今天</span><span>{{shownProducts.length}} 种商品 · 入库 {{formatQuantity(counts.inbound)}} 次 · 出库 {{formatQuantity(counts.outbound)}} 次</span></div>
        <div class="overview-legend"><span><i class="legend-dot" />商品颜色与商品档案一致</span><span class="inbound">入 = 入库</span><span class="outbound">出 = 出库</span><span>不含库存导入和盘库调整 · — 表示无出入库</span></div>
        <div v-if="view === 'calendar'" ref="dateRegion" class="calendar-scroll" tabindex="0" role="region" aria-label="出入库日历，点击日期查看明细">
          <div class="calendar-grid" :class="{ 'single-day': dates.length === 1 }">
            <div v-for="weekday in ['一','二','三','四','五','六','日']" :key="weekday" class="weekday">周{{weekday}}</div>
            <div v-for="blank in padding" :key="'blank-'+blank" class="calendar-blank" aria-hidden="true" />
            <button v-for="day in calendarDays" :key="day.date" type="button" class="calendar-day date-tile" :class="{ today: day.date === localDate(), selected: day.date === detailDate }" :aria-label="dayAria(day)" :aria-pressed="day.date === detailDate" @click="showDate(day.date)">
              <span class="day-heading"><time :datetime="day.date"><span class="date-full">{{dateLabel(day.date)}}</span><span class="date-short">{{Number(day.date.slice(8))}}</span></time><span v-if="day.date === localDate()" class="today-label">今天</span></span>
              <span class="day-product-count">{{compactCount(day.items.length)}}<small> <span class="count-full">种商品</span><span class="count-short">种</span></small></span>
              <span class="day-counts"><span class="inbound">入 {{compactCount(day.summary.inboundCount)}}<span class="count-suffix"> 笔</span></span><span class="outbound">出 {{compactCount(day.summary.outboundCount)}}<span class="count-suffix"> 笔</span></span></span>
            </button>
          </div>
        </div>
        <div v-else-if="view === 'cards'" ref="dateRegion" class="summary-scroll" tabindex="0" role="region" aria-label="每日汇总卡片，日期从早到晚排列">
          <div class="summary-grid">
            <button v-for="day in summaryDays" :key="day.date" type="button" class="summary-card date-tile" :class="{ today: day.date === localDate(), selected: day.date === detailDate }" :aria-label="dayAria(day)" :aria-pressed="day.date === detailDate" @click="showDate(day.date)">
              <span class="day-heading"><time :datetime="day.date">{{day.date}}</time><span v-if="day.date === localDate()" class="today-label">今天</span></span>
              <span class="day-product-count">{{compactCount(day.items.length)}}<small> 种商品</small></span>
              <span class="day-counts"><span class="inbound">入库 {{compactCount(day.summary.inboundCount)}} 笔</span><span class="outbound">出库 {{compactCount(day.summary.outboundCount)}} 笔</span></span>
            </button>
          </div>
        </div>
        <div v-else-if="shownProducts.length" class="matrix-scroll" tabindex="0" role="region" aria-label="出入库表格，日期在上方、商品在左侧">
          <table class="movement-matrix">
            <caption>每日商品出入库汇总，数量单位见商品信息</caption>
            <thead><tr><th scope="col" class="product-column">商品 / 日期</th><th v-for="date in dates" :key="date" scope="col" class="date-heading"><time :datetime="date">{{date}}</time><small v-if="date===localDate()">今天</small></th></tr></thead>
            <tbody><tr v-for="p in shownProducts" :key="p.id"><th scope="row" class="product-column" :style="productStyle(p.name)"><div class="matrix-product-name"><i :style="{backgroundColor:productTone(p.name).accent}" /><button type="button" class="product-detail-link two-lines" @click="showProduct(p)">{{p.name}}</button></div><div class="matrix-spec">{{p.productSpecification || '无产品规格'}} · {{p.rawMaterialSpecification || '无原料规格'}}</div><div class="matrix-unit">单位：{{p.unit}}<span v-if="!p.isActive"> · 已停用</span></div><MovementQuickActions :product="p" :disabled="entryLocked" @entry="startEntry" /></th><td v-for="date in dates" :key="date"><template v-if="index.get(date)?.get(p.id)"><div class="inbound">入 <b>{{formatQuantity(index.get(date)!.get(p.id)!.inbound)}}</b> {{p.unit}}</div><div class="outbound">出 <b>{{formatQuantity(index.get(date)!.get(p.id)!.outbound)}}</b> {{p.unit}}</div></template><span v-else class="muted">—</span></td></tr></tbody>
          </table>
        </div>
        <el-empty v-else description="当前范围没有符合筛选条件的商品，可调整天数或商品筛选" :image-size="70" />
        <section v-if="view !== 'table'" class="day-details" aria-label="所选日期商品明细">
          <div class="detail-heading"><div><h3>{{detailDate}} <span v-if="detailDate === localDate()" class="today-label">今天</span> · 商品明细</h3><p class="muted" aria-live="polite">{{details.length}} 种商品 · 入库 {{formatQuantity(detailSummary.inboundCount)}} 笔 · 出库 {{formatQuantity(detailSummary.outboundCount)}} 笔</p></div><span class="detail-hint">快捷操作均记为今天的新记录</span></div>
          <details v-if="details.length" class="unit-summary">
            <summary>按单位查看当日汇总（{{detailSummary.units.length}} 种单位）</summary>
            <div class="unit-totals"><div v-for="total in detailSummary.units" :key="total.unit" class="unit-total"><b>{{total.unit}}</b><span class="inbound">入 {{formatQuantity(total.inbound)}}</span><span class="outbound">出 {{formatQuantity(total.outbound)}}</span><span>净增减 {{signedQuantity(total.netChange)}}</span></div></div>
          </details>
          <el-table v-if="details.length" :data="pagedDetails" row-key="product.id" class="day-detail-table">
            <el-table-column label="商品 / 规格" min-width="190"><template #default="s"><div class="detail-product" :style="productStyle(s.row.product.name)"><button type="button" class="product-detail-link two-lines" @click="showProduct(s.row.product)" :aria-label="'查看商品资料：'+productLabel(s.row.product)">{{s.row.product.name}}</button><p class="detail-spec">{{s.row.product.productSpecification || '无产品规格'}} · {{s.row.product.rawMaterialSpecification || '无原料规格'}}</p></div></template></el-table-column>
            <el-table-column label="单位" min-width="60"><template #default="s">{{s.row.product.unit}}</template></el-table-column>
            <el-table-column label="入库" min-width="80"><template #default="s"><strong class="inbound">{{formatQuantity(s.row.movement.inbound)}}</strong><small class="movement-count">{{s.row.movement.inboundCount}} 笔</small></template></el-table-column>
            <el-table-column label="出库" min-width="80"><template #default="s"><strong class="outbound">{{formatQuantity(s.row.movement.outbound)}}</strong><small class="movement-count">{{s.row.movement.outboundCount}} 笔</small></template></el-table-column>
            <el-table-column label="今天记一笔" width="130"><template #default="s"><MovementQuickActions :product="s.row.product" :disabled="entryLocked" @entry="startEntry" /></template></el-table-column>
          </el-table>
          <div v-else class="detail-empty"><p>当天没有符合筛选条件的出入库记录</p><span>可选择其他日期，或用上方“记入库 / 记出库”登记今天的进出。</span></div>
          <el-pagination v-if="details.length > pageSize" v-model:current-page="detailPage" :page-size="pageSize" :total="details.length" :pager-count="5" layout="prev, pager, next" aria-label="当日商品明细分页" />
        </section>
      </template>
      <p v-else class="muted loading-message">正在查询近日出入库…</p>
    </div>
    <el-dialog v-model="productDetailOpen" title="商品资料" width="min(540px, 95vw)">
      <dl v-if="detailProduct" class="product-full-details"><dt>商品名称</dt><dd>{{detailProduct.name}}</dd><dt>产品规格</dt><dd>{{detailProduct.productSpecification || '无产品规格'}}</dd><dt>原料规格</dt><dd>{{detailProduct.rawMaterialSpecification || '无原料规格'}}</dd><dt>单位</dt><dd>{{detailProduct.unit}}</dd><dt>商品备注</dt><dd>{{detailProduct.note || '无备注'}}</dd><dt>当前库存</dt><dd>{{formatQuantity(detailProduct.quantity)}} {{detailProduct.unit}}<span v-if="!detailProduct.isActive"> · 已停用</span></dd></dl>
      <p class="muted">当前库存为此刻结存；入库、出库均记为今天的新记录。</p>
      <MovementQuickActions v-if="detailProduct" :product="detailProduct" :disabled="entryLocked" @entry="startEntry" />
    </el-dialog>
  </section>
</template>

<style scoped>
.overview-controls{display:grid;grid-template-columns:minmax(0,1fr) 264px;gap:20px 24px;margin-bottom:12px}.overview-heading{grid-column:1;grid-row:1}.overview-heading-main{flex:1;min-width:0}.overview-title-row{display:flex;align-items:center;gap:20px;flex-wrap:wrap}.overview-title-row h2{flex-shrink:0}.overview-title-row .el-radio-group{flex-shrink:0}.overview-tip{display:inline-block;margin-left:12px;font-size:12px}.overview-entry-actions{grid-column:2;grid-row:1 / span 2;display:flex;gap:12px;min-height:110px}.overview-entry-actions .el-button{flex:1;min-width:0;margin:0;height:auto;padding:18px 12px;font-size:22px;font-weight:700}.outbound-entry{color:#955625;border-color:#dfc5b0;background:#fffaf4}.movement-overview{min-width:0;container-type:inline-size}.overview-heading{display:flex;align-items:flex-start;justify-content:space-between;gap:16px;flex-wrap:wrap}.overview-heading h2{margin:0}.overview-heading p{margin:8px 0 0;font-size:13px}
.overview-filters{grid-column:1;grid-row:2;display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin:0}.days-filter{display:flex;align-items:center;gap:8px}.days-filter .el-input-number{width:106px}.overview-product-filter{flex:1;min-width:240px;max-width:520px}
.overview-body{min-height:120px}.range-line{display:flex;flex-wrap:wrap;justify-content:space-between;gap:8px;color:#496878;font-size:13px;margin-bottom:10px}.overview-legend{display:flex;align-items:center;flex-wrap:wrap;gap:8px 18px;color:#6d8292;font-size:12px;margin-bottom:16px}.legend-dot{display:inline-block;width:8px;height:8px;border-radius:50%;background:#548ac0;margin-right:5px}.inbound{color:#247256}.outbound{color:#955625}
.date-short,.count-short{display:none}.calendar-scroll,.summary-scroll{max-height:310px;overflow:auto;border:1px solid #dce7ee;border-radius:10px;scrollbar-gutter:stable;position:relative}.calendar-grid{display:grid;grid-template-columns:repeat(7,minmax(0,1fr));background:#e1ecf2;gap:1px}.weekday{position:sticky;top:0;z-index:2;text-align:center;padding:9px 0;background:#eaf4fa;color:#476777;font-size:12px}.calendar-blank{background:#f8fafc}.date-tile{appearance:none;border:0;background:#fff;text-align:left;color:#34596f;cursor:pointer;min-width:0;height:132px;padding:8px;display:flex;flex-direction:column;gap:4px;position:relative}.date-tile:hover{background:#f1f8fc}.date-tile.today{background:#f4faff}.date-tile.selected{background:#eaf5fc;box-shadow:inset 0 0 0 2px #367ca2}.date-tile:focus-visible{outline:3px solid #235a7c;outline-offset:-4px;z-index:1}.day-heading{display:flex;align-items:center;justify-content:space-between;gap:3px;flex-wrap:wrap;font-size:13px;font-weight:700;min-height:38px;flex-shrink:0;align-content:flex-start}.today-label{font-size:11px;background:#3685a7;color:#fff;padding:2px 5px;border-radius:4px}.day-product-count{font-size:22px;font-weight:700}.day-product-count small{font-size:12px;font-weight:400}.day-counts{display:flex;gap:3px 12px;flex-wrap:wrap;margin-top:auto;font-size:12px;line-height:1.6}.day-counts>span{overflow-wrap:anywhere}.calendar-grid.single-day{grid-template-columns:minmax(0,1fr)}.single-day .weekday,.single-day .calendar-blank{display:none}.summary-scroll{padding:10px;background:#f7fafc}.summary-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px}.summary-card{border:1px solid #dce7ee;border-radius:8px}.matrix-scroll{max-height:540px;overflow:auto;border:1px solid #dce7ee;border-radius:10px;scrollbar-gutter:stable}
.movement-matrix{border-collapse:separate;border-spacing:0;width:100%;font-size:13px}.movement-matrix caption{position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%)}.movement-matrix th,.movement-matrix td{border-right:1px solid #dce7ee;border-bottom:1px solid #dce7ee;padding:12px 14px;vertical-align:middle;text-align:left;min-width:140px}.movement-matrix thead th{position:sticky;top:0;z-index:2;vertical-align:top;background:#eff6fa}.movement-matrix .product-column{min-width:260px;width:260px;max-width:300px;position:sticky;left:0;z-index:1;background:#f3f8fb}.movement-matrix thead .product-column{z-index:3}.date-heading{white-space:nowrap}.date-heading small{display:block;color:#3685a7;margin-top:6px}.matrix-product-name{font-size:14px;overflow-wrap:anywhere;line-height:1.5}.matrix-spec,.matrix-unit{font-size:12px;font-weight:400;margin-top:5px;color:#516b7c}.matrix-note{font-size:11px;color:#6d8292;font-weight:400;margin:5px 0 0;max-width:225px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.movement-matrix tbody td{line-height:1.9;background:#fff}.movement-matrix tbody tr:hover td{background:#f8fbfd}
.day-details{border-top:1px solid #dce7ee;margin-top:22px;padding-top:20px;min-width:0}.detail-heading{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:8px;margin-bottom:12px}.detail-heading h3{margin:0 0 7px;font-size:16px}.detail-heading p{margin:0}.detail-hint{font-size:12px;color:#657b8b}.unit-summary{border-radius:8px;background:#f4f8fb;margin-bottom:14px;padding:10px 12px;font-size:13px}.unit-summary summary{cursor:pointer;color:#426579;min-height:24px;line-height:24px}.unit-totals{max-height:180px;overflow:auto;margin-top:8px}.unit-total{display:flex;flex-wrap:wrap;gap:8px 20px;padding:7px 0}.unit-total b{min-width:70px}.detail-product{border-left:3px solid var(--product-accent);border-radius:5px;padding:7px 10px;min-width:0}.product-detail-link{display:block;border:0;padding:0;background:none;text-align:left;color:#285c7b;font-weight:600;cursor:pointer;font-size:14px;line-height:1.5;overflow-wrap:anywhere}.product-detail-link:hover{text-decoration:underline}.product-detail-link:focus-visible{outline:2px solid #367ca2;outline-offset:2px}.two-lines{display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:2;overflow:hidden}.detail-spec{font-size:12px;color:#657b8b;margin:4px 0 0;white-space:nowrap;text-overflow:ellipsis;overflow:hidden}.movement-count{display:block;font-size:12px;color:#657b8b;margin-top:3px}.day-detail-table :deep(.movement-actions){margin-top:0}.detail-empty{padding:25px 16px;background:#f8fafc;border-radius:8px;text-align:center;color:#657b8b}.detail-empty p{margin:0 0 8px;font-size:14px}.detail-empty span{font-size:12px}.product-full-details{margin:0 0 20px;line-height:1.7}.product-full-details dt{color:#657b8b;font-size:12px;margin-top:12px}.product-full-details dd{margin:3px 0 0;overflow-wrap:anywhere;font-size:15px}
.overview-error{display:flex;align-items:center;gap:16px;color:#af4350;padding:24px 0}.loading-message{padding:20px 0}
@container(max-width:850px){.overview-controls{grid-template-columns:minmax(0,1fr);gap:16px}.overview-entry-actions{grid-column:1;grid-row:2;min-height:64px;width:100%;max-width:320px;justify-self:end}.overview-entry-actions .el-button{font-size:20px}.overview-filters{grid-row:3}}
@container(max-width:500px){.overview-entry-actions{max-width:none}}
@media(max-width:650px){.calendar-day .date-full,.calendar-day .count-full,.calendar-day .count-suffix{display:none}.calendar-day .date-short,.calendar-day .count-short{display:inline}.calendar-day .day-heading{font-size:13px}.calendar-day .day-counts>span{white-space:nowrap}.calendar-day .today-label{font-size:8px;padding:1px;white-space:nowrap}.overview-filters{gap:10px}.overview-product-filter{min-width:100%;max-width:none}.range-line{font-size:12px}.overview-legend{gap:7px 12px}.date-tile{padding:8px 4px;height:140px;gap:5px}.day-heading{font-size:11px}.today-label{font-size:10px;padding:1px 3px}.day-product-count{font-size:18px}.day-product-count small{display:block;font-size:10px}.day-counts{font-size:10px;display:grid;gap:2px}.summary-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.summary-card{padding:12px;height:132px}.summary-card .day-product-count small{display:inline;font-size:12px}.summary-card .day-counts{font-size:12px;display:flex}.summary-card .day-heading{font-size:13px}.movement-matrix .product-column{min-width:160px;width:160px;max-width:180px;padding:10px 8px}.detail-heading h3{font-size:14px}}
</style>
