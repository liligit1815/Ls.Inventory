<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, Paged, Product, ProductHistory } from '@/types/api'
import ProductComparison from './ProductComparison.vue'
import { formatDateTime, formatQuantity } from '@/utils/inventory'

const props = defineProps<{ product: Product }>()
const emit = defineEmits<{ close: [] }>()
const page = ref(1), refresh = ref(0), total = ref(0), pageSize = ref(10)
const entries = ref<ProductHistory[]>([]), selected = ref<ProductHistory>(), showAll = ref(false)
const loading = ref(true), error = ref('')
const chartScroll = ref<HTMLElement>()
watch([() => props.product.id, page, refresh], async ([id, currentPage], _, onCleanup) => {
  const controller = new AbortController()
  onCleanup(() => controller.abort())
  loading.value = true
  error.value = ''
  entries.value = []
  selected.value = undefined
  showAll.value = false
  try {
    const response = await api.get<ApiResponse<Paged<ProductHistory> & {pageSize: number}>>(
      `/api/v1/products/${id}/history`, { params: { page: currentPage }, signal: controller.signal })
    if (controller.signal.aborted) return
    const data = response.data.data
    total.value = data.total
    pageSize.value = data.pageSize
    entries.value = [...data.items].reverse()
    selected.value = data.items[0]
  } catch (e) {
    if (!controller.signal.aborted) error.value = getErrorMessage(e)
  } finally {
    if (!controller.signal.aborted) {
      loading.value = false
      await nextTick()
      if (chartScroll.value) chartScroll.value.scrollLeft = chartScroll.value.scrollWidth
    }
  }
}, { immediate: true })

const eventLabel = (h: ProductHistory) => !h.before ? '建档' : h.before.name !== h.after.name ? '改名' : '资料变更'
const dateLabel = new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', month: '2-digit', day: '2-digit' })
const timeLabel = new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' })

// Space records in chronological order so edits in the same minute remain individually selectable.
const scale = computed(() => {
  const max = Math.max(1, ...entries.value.map(h => h.quantity))
  const base = 10 ** Math.floor(Math.log10(max / 4))
  const step = Math.max(1, ([1, 2, 5, 10].find(n => n * base >= max / 4) ?? 10) * base)
  const count = Math.ceil(max / step)
  return { top: count * step, ticks: Array.from({ length: count + 1 }, (_, i) => i * step) }
})
const x = (i: number) => (i + 0.5) * 100 / entries.value.length
const y = (qty: number) => 100 * (1 - qty / scale.value.top)
const points = computed(() => entries.value.map((h, i) => `${x(i)},${y(h.quantity)}`).join(' '))
const pages = computed(() => Math.ceil(total.value / pageSize.value))
</script>

<template>
  <el-dialog :model-value="true" title="商品变更历史" width="800px" top="5vh" class="product-history-dialog" @closed="emit('close')">
    <header class="product-heading">
      <div><span class="code">{{ product.code }}</span><h2>{{ product.name }}</h2></div>
      <span v-if="!loading && !error" class="history-count">{{ total }} 条记录</span>
    </header>
    <div v-if="loading" class="state" role="status">正在读取变更记录…</div>
    <div v-else-if="error" class="state" role="alert">{{ error }} <el-button @click="refresh++">重试</el-button></div>
    <el-empty v-else-if="!entries.length" description="暂无可追溯的变更记录。下次修改商品资料后，可在这里查看。" />
    <template v-else>
      <section class="chart-card" aria-label="变更时库存时间轴">
        <div class="chart-heading"><h3>变更时库存</h3><span>按变更时间排列</span></div>
        <div ref="chartScroll" class="chart-scroll" tabindex="0" aria-label="库存图，可横向滚动查看更多记录">
          <div class="chart-inner" :style="{ minWidth: (78 + entries.length * 70) + 'px' }">
            <div class="plot">
              <div v-for="tick in scale.ticks" :key="tick" class="grid-line" :style="{top:y(tick)+'%'}"><span>{{ formatQuantity(tick) }}</span></div>
              <svg viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
                <polyline :points="points" fill="none" stroke="#528aa2" stroke-width="2" vector-effect="non-scaling-stroke" />
              </svg>
              <template v-for="(entry,i) in entries" :key="entry.id">
                <i v-if="selected?.id===entry.id" class="cursor" :style="{left:x(i)+'%'}" />
                <button class="point" :class="{active:selected?.id===entry.id}" type="button" :style="{left:x(i)+'%',top:y(entry.quantity)+'%'}"
                  :aria-label="`${formatDateTime(entry.changedAt)}，${eventLabel(entry)}，库存 ${entry.quantity} ${entry.after.unit}`"
                  :aria-pressed="selected?.id===entry.id" @mouseenter="selected=entry" @focus="selected=entry" @click="selected=entry">
                  <span class="dot" />
                  <span v-if="selected?.id===entry.id" class="point-value" aria-hidden="true">{{ formatQuantity(entry.quantity) }}</span>
                </button>
                <div class="time-tick" :class="{active:selected?.id===entry.id}" :style="{left:x(i)+'%'}">
                  <b>{{ dateLabel.format(new Date(entry.changedAt)) }}</b>{{ timeLabel.format(new Date(entry.changedAt)) }}
                </div>
              </template>
            </div>
          </div>
        </div>
        <div class="chart-foot"><span>悬停或点选圆点，查看该次变更</span>
          <div v-if="pages>1" class="pager"><button :disabled="page>=pages" @click="page++">更早</button><span>{{page}} / {{pages}}</span><button :disabled="page<=1" @click="page--">更新</button></div>
        </div>
      </section>
      <section v-if="selected" class="detail-card" aria-label="所选变更明细">
        <div class="detail-heading"><div><span class="kind">{{ eventLabel(selected) }}</span><strong>{{ formatDateTime(selected.changedAt) }}</strong></div>
          <span class="balance">当时库存 <b>{{ formatQuantity(selected.quantity) }} {{ selected.after.unit }}</b></span></div>
        <div class="detail-meta"><span>操作人 · {{ selected.userName }}</span>
          <button type="button" @click="showAll=!showAll">{{ showAll ? '只看变更字段' : '查看全部字段' }}</button></div>
        <ProductComparison :before="selected.before" :after="selected.after" :quantity="selected.quantity" :all="showAll"/>
      </section>
    </template>
  </el-dialog>
</template>

<style scoped>
.product-heading,.chart-heading,.chart-foot,.detail-heading,.detail-meta,.pager,.detail-heading>div{display:flex;align-items:center;justify-content:space-between;gap:12px}
.product-heading{margin:2px 0 16px}.product-heading h2{margin:4px 0 0;color:#203e53;font-size:21px;overflow-wrap:anywhere}
.code,.time-tick,.balance,.detail-heading strong{font-variant-numeric:tabular-nums}
.code{font-size:12px;letter-spacing:.07em;color:#57768a}.history-count,.kind{white-space:nowrap;border-radius:20px;padding:4px 10px;background:#e8f3f7;color:#32637b;font-size:12px;font-weight:650}
.chart-card,.detail-card{overflow:hidden;border:1px solid #cfe1e9;border-radius:12px}
.chart-card{background:linear-gradient(140deg,#eff8fb,#fcfefe)}.chart-heading{padding:15px 18px 0}.chart-heading h3{margin:0;color:#264f64;font-size:15px}
.chart-heading span,.chart-foot,.detail-meta{font-size:12px;color:#526e81}.chart-scroll{overflow-x:auto}.chart-inner{padding:32px 12px 46px 78px}
.plot{height:126px;position:relative}.plot svg,.cursor{position:absolute;inset:0;width:100%;height:100%;pointer-events:none}
.grid-line{position:absolute;left:0;right:0;border-top:1px dashed #ccdee7}.grid-line span{position:absolute;right:calc(100% + 8px);transform:translateY(-50%);font:11px/1.2 system-ui;color:#536e7e}
.cursor{width:0;border-left:1px dashed #77a8b8}.point{position:absolute;transform:translate(-50%,-50%);width:30px;height:30px;border:0;background:none;display:grid;place-items:center;cursor:pointer;padding:0;border-radius:50%}
.dot{width:11px;height:11px;border:2px solid #468097;border-radius:50%;background:#fff}.point.active .dot{width:14px;height:14px;background:#217a7a;border:3px solid white;box-shadow:0 0 0 2px #217a7a}.point:focus-visible{outline:2px solid #217a7a}
.point-value{position:absolute;bottom:calc(50% + 11px);background:#216d70;color:white;border-radius:5px;padding:1px 6px;font:600 11px/15px system-ui;white-space:nowrap;pointer-events:none}
.time-tick{position:absolute;top:calc(100% + 9px);display:grid;justify-items:center;transform:translateX(-50%);color:#597585;font-size:11px;line-height:1.4}.time-tick.active{color:#176669}
.chart-foot{min-height:33px;padding:6px 14px;border-top:1px solid #dce9ed}.pager{gap:6px;white-space:nowrap}
.pager button,.detail-meta button{border:0;background:none;padding:2px;color:#236780;font:inherit;cursor:pointer}.pager button:disabled{opacity:.4;cursor:default}
.detail-card{margin-top:12px;background:#fff}.detail-heading,.detail-meta{padding:12px 16px 0;flex-wrap:wrap}.detail-heading strong{color:#2a495e}.kind{color:#156663;background:#e2f3ee}
.balance{color:#506b7b;font-size:13px}.balance b{color:#176665}.detail-meta{padding-top:6px;padding-bottom:10px}
.state{min-height:185px;display:grid;place-content:center;justify-items:center;gap:12px;color:#607789}
@media(max-width:600px){.chart-foot{flex-wrap:wrap}.chart-inner{padding-left:70px}.detail-heading strong{font-size:13px}}
</style>
