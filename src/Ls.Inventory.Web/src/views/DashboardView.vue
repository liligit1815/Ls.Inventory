<script setup lang="ts">
import { computed, nextTick, onMounted, onBeforeUnmount, ref } from 'vue'
import axios from 'axios'
import { ElMessage, ElMessageBox } from 'element-plus'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, Product } from '@/types/api'
import PageHeader from '@/components/PageHeader.vue'
import RecentMovementsOverview from '@/components/RecentMovementsOverview.vue'
import { productStyle } from '@/utils/productColors'
import { formatQuantity, validQuantity, productLabel, isLowStock, matchesProduct, localDate } from '@/utils/inventory'
const products = ref<Product[]>([])
const alerts = computed(() => products.value.filter(isLowStock))
const productId = ref(''), kind = ref('Inbound'), quantity = ref<number>(), note = ref(''), busy = ref(false), loading = ref(false)
const pending = ref<{ requestId: string; productId: string; kind: string; quantity: number; note: string }>()
const active = computed(() => products.value.filter(p => p.isActive))
const search = ref('')
const options = computed(() => active.value.filter(p => matchesProduct(p, search.value)))
function filterProducts(query: string) { search.value = query }
function resetSearchOnClose(visible: boolean) { if (!visible) search.value = '' }
const selected = computed(() => products.value.find(p => p.id === productId.value))
const historyRefresh = ref(0)
const entryOpen = ref(false)
const overviewRef = ref<InstanceType<typeof RecentMovementsOverview>>()
const quantityInput = ref<{ focus: () => void }>()
const receipt = ref<{ productId: string; name: string; unit: string; kind: string; quantity: number; quantityAfter: number }>()
const projectedQuantity = computed(() => selected.value && validQuantity(quantity.value)
  ? selected.value.quantity + (kind.value === 'Inbound' ? quantity.value : -quantity.value) : undefined)
const quantityError = computed(() => {
  if (quantity.value != null && !validQuantity(quantity.value)) return '请输入大于零的整数数量'
  if (selected.value && !selected.value.isActive) return '该商品已停用，请重新选择商品'
  if (projectedQuantity.value != null && projectedQuantity.value < 0) return '出库数量超过当前库存，请减少数量'
  if (projectedQuantity.value != null && projectedQuantity.value > 2147483647) return '入库后的库存超出允许范围，请减少数量'
  return ''
})
function openEntry(nextKind: 'Inbound' | 'Outbound', id?: string) {
  if (busy.value || pending.value) { entryOpen.value = true; return }
  productId.value = id ?? ''; kind.value = nextKind; quantity.value = undefined; note.value = ''; search.value = ''
  entryOpen.value = true
}
function focusQuantity() { if (productId.value) void nextTick(() => quantityInput.value?.focus()) }
async function closeEntry(done: () => void) {
  if (busy.value || pending.value) { ElMessage.warning('请先确认本次操作结果，再关闭'); return }
  if (quantity.value != null || note.value.trim()) {
    try { await ElMessageBox.confirm('这笔还没有提交，关闭后会清空已填内容。', '放弃本次填写？', { confirmButtonText: '放弃填写', cancelButtonText: '继续填写', type: 'warning' }) }
    catch { return }
  }
  quantity.value = undefined; note.value = ''; done()
}
function viewReceipt() {
  if (busy.value || pending.value || !receipt.value) return
  overviewRef.value?.focusToday(receipt.value.productId)
  entryOpen.value = false
}

let loadSequence = 0
async function load() {
  const sequence = ++loadSequence
  loading.value = true
  try {
    const result = (await api.get<ApiResponse<Product[]>>('/api/v1/products/')).data.data
    if (sequence === loadSequence) products.value = result
  } catch(e) { if (sequence === loadSequence) ElMessage.error(getErrorMessage(e)) }
  finally { if (sequence === loadSequence) { loading.value = false; historyRefresh.value++ } }
}
async function submit() {
  if (busy.value) return
  if (!pending.value) {
    if (!selected.value || !selected.value.isActive || !validQuantity(quantity.value)) { ElMessage.warning('请选择商品，并输入大于零的数量'); return }
    if (quantityError.value) { ElMessage.warning(quantityError.value); return }
    pending.value = { requestId: crypto.randomUUID(), productId: productId.value, kind: kind.value, quantity: quantity.value, note: note.value }
  }
  busy.value=true
  try {
    const response = await api.post<ApiResponse<{ quantityAfter: number; alreadyPosted: boolean }>>('/api/v1/movements', pending.value)
    const posted = pending.value
    const product = products.value.find(p => p.id === posted.productId)
    receipt.value = { ...posted, name: product?.name ?? '', unit: product?.unit ?? '', quantityAfter: response.data.data.quantityAfter }
    if (product) product.quantity = response.data.data.quantityAfter
    ElMessage.success(`${response.data.data.alreadyPosted ? '已确认此前操作成功' : '已入账'}，结存 ${formatQuantity(response.data.data.quantityAfter)}`)
    pending.value=undefined; quantity.value=undefined; note.value=''; await load(); focusQuantity()
  } catch(e) {
    if (axios.isAxiosError(e) && e.response && e.response.status < 500) pending.value=undefined
    ElMessage.error(pending.value ? '结果尚未确认，请点击“重试确认”；系统不会重复入账' : getErrorMessage(e))
  } finally { busy.value=false }
}
let refreshTimer: ReturnType<typeof setInterval> | undefined
onMounted(() => { void load(); refreshTimer = setInterval(() => { if (!document.hidden && !busy.value) void load() }, 30000) })
onBeforeUnmount(() => { if (refreshTimer) clearInterval(refreshTimer) })
</script>
<template><div class="page-shell">
  <PageHeader eyebrow="轻量版" title="库存工作台"><el-button type="primary" @click="load" :loading="loading">刷新数据</el-button></PageHeader>
  <RecentMovementsOverview ref="overviewRef" :products="products" :refresh-key="historyRefresh" :entry-locked="busy || Boolean(pending)" @entry="openEntry">
    <template #receipt><div v-if="receipt" class="entry-receipt" role="status"><span>已{{receipt.kind === 'Inbound' ? '入库' : '出库'}} · {{receipt.name}} · {{formatQuantity(receipt.quantity)}} {{receipt.unit}} · 本次结存 {{formatQuantity(receipt.quantityAfter)}} {{receipt.unit}}</span><el-button link type="primary" :disabled="busy || Boolean(pending)" @click="viewReceipt">查看今天记录</el-button></div></template>
  </RecentMovementsOverview>
  <el-drawer v-model="entryOpen" :title="'快捷'+(kind === 'Inbound' ? '入库' : '出库')" size="min(620px, 100vw)" :before-close="closeEntry" :close-on-click-modal="false" :close-on-press-escape="!busy && !pending" @opened="focusQuantity">
    <section class="quick-entry" aria-label="快捷出入库">
      <p class="entry-date">记账日期：{{localDate()}} · 今天</p>
      <p class="muted entry-description">这是一笔新的出入库，提交后会更新近日记录。</p>
    <el-form @submit.prevent="submit" label-position="top" size="large"><div class="entry-grid">
      <el-form-item label="商品"><el-select class="product-picker" v-model="productId" filterable :filter-method="filterProducts" @visible-change="resetSearchOnClose" @change="focusQuantity" placeholder="输入名称、规格或备注查找" :disabled="busy || Boolean(pending)" popper-class="product-options quick-product-options" placement="left-start" :fallback-placements="['bottom-end', 'top-end']" :fit-input-width="false" :offset="28" :popper-options="{ modifiers: [{ name: 'flip', options: { altAxis: false } }] }"><el-option v-for="p in options" :key="p.id" :label="productLabel(p)" :value="p.id" :style="productStyle(p.name)"><span class="product-name">{{p.name}}</span><span class="product-spec"> · {{p.productSpecification || '无产品规格'}} · {{p.rawMaterialSpecification || '无原料规格'}} · </span><b class="product-unit">{{p.unit}}</b><template v-if="p.note"><span> · </span><b class="product-note">{{p.note}}</b></template></el-option></el-select></el-form-item>
      <el-form-item label="操作"><el-radio-group class="entry-kind" v-model="kind" aria-label="出入库操作" :disabled="busy || Boolean(pending)"><el-radio-button value="Inbound">入库</el-radio-button><el-radio-button value="Outbound">出库</el-radio-button></el-radio-group></el-form-item>
      <div v-if="selected" class="selected-stock" :style="productStyle(selected.name)">
        <strong class="product-name">{{selected.name}}</strong><p class="product-spec">{{selected.productSpecification || '无产品规格'}} · {{selected.rawMaterialSpecification || '无原料规格'}}</p><b class="product-unit">{{selected.unit}}</b><p v-if="selected.note" class="product-note">{{selected.note}}</p>
        <div class="stock-preview" aria-live="polite"><div><span>当前库存</span><strong>{{formatQuantity(selected.quantity)}}</strong></div><span class="stock-arrow">→</span><div><span>{{kind === 'Inbound' ? '入库' : '出库'}}后预计</span><strong :class="{ negative: Boolean(quantityError) }">{{projectedQuantity == null ? '—' : formatQuantity(projectedQuantity)}}</strong></div></div>
        <p v-if="isLowStock(selected)" class="negative">库存告急</p>
      </div>
      <el-form-item :label="'数量' + (selected ? '（'+selected.unit+'）' : '')" :error="quantityError"><el-input-number ref="quantityInput" v-model="quantity" :controls="false" :precision="0" :step="1" :max="2147483647" :min="0" :disabled="busy || Boolean(pending)" placeholder="输入数量" /></el-form-item>
      <el-form-item label="本次操作备注（选填）"><el-input v-model="note" maxlength="500" :disabled="busy || Boolean(pending)" placeholder="如：补货、售出" /></el-form-item>
      <div v-if="pending && !busy" class="pending-message" role="alert">结果尚未确认，请使用“重试确认”。本次填写已保留，重试不会重复入账。</div>
      <div class="entry-submit"><el-button type="primary" native-type="submit" :loading="busy" :disabled="!pending && (!selected || !validQuantity(quantity) || Boolean(quantityError))">{{ pending ? '重试确认' : '确认'+(kind === 'Inbound' ? '入库' : '出库') }}</el-button></div>
    </div></el-form>
      <div v-if="receipt && quantity == null && !note && !pending" class="drawer-receipt" role="status"><strong>上一笔已{{receipt.kind === 'Inbound' ? '入库' : '出库'}}</strong><p>{{receipt.name}} · {{formatQuantity(receipt.quantity)}} {{receipt.unit}}</p><p>本次结存 {{formatQuantity(receipt.quantityAfter)}} {{receipt.unit}}</p><el-button :disabled="busy" @click="viewReceipt">完成，查看今天记录</el-button><p class="muted">也可以继续填写下一笔。</p></div>
    </section>
  </el-drawer>
  <section class="panel warning-panel" aria-live="polite"><div class="section-title"><h2>库存预警 <span class="warning-count">{{alerts.length}}</span></h2><RouterLink to="/stock-warnings" custom v-slot="{ navigate }"><el-button type="primary" @click="navigate">设置预警值</el-button></RouterLink></div>
    <div v-if="alerts.length" class="warning-list"><article v-for="p in alerts" :key="p.id" :style="productStyle(p.name)" class="warning-item"><div><strong class="product-name negative">{{p.name}}库存告急，请及时处理</strong><p><span class="product-spec">{{p.productSpecification || '无产品规格'}} · {{p.rawMaterialSpecification || '无原料规格'}} · </span><b class="product-unit">{{p.unit}}</b><span v-if="p.note" class="product-note"> · {{p.note}}</span></p><div class="warning-quantities"><span>当前库存 <b>{{formatQuantity(p.quantity)}}</b></span><span>预警值 <b>{{formatQuantity(p.warningQuantity!)}}</b></span></div></div><el-button type="primary" @click="openEntry('Inbound',p.id)" :disabled="Boolean(pending)">去入库</el-button></article></div>
    <p v-else class="muted no-warning">{{active.some(p=>p.warningQuantity!=null) ? '当前没有低于预警值的商品。' : '尚未设置库存预警值，可按商品设置后在这里查看提醒。'}}</p>
  </section>
</div></template>
<style scoped>
.entry-grid{display:grid;gap:22px}.entry-grid .el-select,.entry-grid .el-input-number{width:100%}.entry-grid .el-form-item{margin-bottom:0;min-width:0}.entry-submit .el-button{width:100%;height:52px;font-size:18px}
.quick-entry{--product-text-size:18px;--product-unit-size:19px}.entry-grid :deep(.el-form-item__label){font-size:16px}.entry-grid :deep(.el-select__wrapper){min-height:48px}.entry-grid :deep(.el-input__wrapper){min-height:46px;font-size:16px}.entry-kind{display:flex;width:100%}.entry-kind :deep(.el-radio-button){flex:1;min-width:0}.entry-kind :deep(.el-radio-button__inner){display:flex;align-items:center;justify-content:center;width:100%;min-height:54px;padding:14px 24px;font-size:20px;font-weight:700}
.entry-date{margin:0;color:#34596f;font-weight:600}.entry-description{margin:10px 0 24px;font-size:13px;line-height:1.7}.selected-stock{padding:18px;border-radius:10px;border-left:4px solid var(--product-accent);overflow-wrap:anywhere}.selected-stock>p{margin:7px 0}.stock-preview{display:flex;align-items:center;gap:22px;border-top:1px solid #dce7ee;margin-top:15px;padding-top:15px}.stock-preview>div{flex:1;min-width:0}.stock-preview span{font-size:12px;color:#516b7c}.stock-preview strong{display:block;font-size:27px;overflow-wrap:anywhere;margin-top:6px}.stock-arrow{font-size:22px!important}.entry-receipt{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:10px;background:#edf8f2;border:1px solid #c3e3d0;color:#247256;border-radius:8px;padding:12px 15px;margin-top:18px;font-size:13px;line-height:1.7}.drawer-receipt{background:#edf8f2;border-radius:10px;padding:18px;margin-top:24px;color:#247256;line-height:1.6}.drawer-receipt p{font-size:13px;margin:8px 0}.pending-message{background:#fff5e8;color:#955625;padding:12px;border-radius:8px;line-height:1.7;font-size:13px}.warning-count{background:#fff0ed;color:#af4350;border-radius:20px;padding:3px 10px;font-size:15px}
.warning-list{display:grid;grid-template-columns:1fr 1fr;gap:12px}
.warning-item{padding:22px;border-radius:12px;border:1px solid #e9d6d2;display:flex;align-items:center;justify-content:space-between;gap:18px}
.warning-item>div{min-width:0}.warning-item strong{font-size:18px;font-weight:800;line-height:1.6}
.warning-item p{font-size:16px;font-weight:700;margin:10px 0;line-height:1.6}
.warning-quantities{display:flex;flex-wrap:wrap;gap:12px 26px;color:#3b576a;font-size:16px;font-weight:700}
.warning-quantities b{font-size:24px;margin-left:8px}.no-warning{margin:0;padding:12px 0}
@media(max-width:1150px){.warning-list{grid-template-columns:1fr}}
@media(max-width:650px){.warning-item{align-items:flex-start}.warning-item .el-button{flex-shrink:0}}
</style>
