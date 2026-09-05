<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from 'vue'
import axios from 'axios'
import { ElMessage } from 'element-plus'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, Product } from '@/types/api'
import PageHeader from '@/components/PageHeader.vue'
import { productStyle } from '@/utils/productColors'
import { formatQuantity, validQuantity, productLabel, isLowStock, matchesProduct } from '@/utils/inventory'
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
let loadSequence = 0
async function load() {
  const sequence = ++loadSequence
  loading.value = true
  try {
    const result = (await api.get<ApiResponse<Product[]>>('/api/v1/products/')).data.data
    if (sequence === loadSequence) products.value = result
  } catch(e) { if (sequence === loadSequence) ElMessage.error(getErrorMessage(e)) }
  finally { if (sequence === loadSequence) loading.value = false }
}
async function submit() {
  if (busy.value) return
  if (!pending.value) {
    if (!productId.value || !validQuantity(quantity.value)) { ElMessage.warning('请选择商品，并输入大于零的数量'); return }
    pending.value = { requestId: crypto.randomUUID(), productId: productId.value, kind: kind.value, quantity: quantity.value, note: note.value }
  }
  busy.value=true
  try {
    const response = await api.post<ApiResponse<{ quantityAfter: number; alreadyPosted: boolean }>>('/api/v1/movements', pending.value)
    ElMessage.success(`${response.data.data.alreadyPosted ? '已确认此前操作成功' : '已入账'}，结存 ${formatQuantity(response.data.data.quantityAfter)}`)
    pending.value=undefined; quantity.value=undefined; note.value=''; await load()
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
  <section class="panel quick-entry"><div class="section-title"><h2>快捷出入库</h2></div>
    <el-form @submit.prevent="submit" label-position="top"><div class="entry-grid">
      <el-form-item label="商品"><el-select class="product-picker" v-model="productId" filterable :filter-method="filterProducts" @visible-change="resetSearchOnClose" placeholder="输入名称、规格或备注查找" :disabled="Boolean(pending)" popper-class="product-options quick-product-options"><el-option v-for="p in options" :key="p.id" :label="productLabel(p)" :value="p.id" :style="productStyle(p.name)"><span class="product-name">{{p.name}}</span><span class="product-spec"> · {{p.productSpecification || '无产品规格'}} · {{p.rawMaterialSpecification || '无原料规格'}} · </span><b class="product-unit">{{p.unit}}</b><template v-if="p.note"><span> · </span><b class="product-note">{{p.note}}</b></template></el-option></el-select></el-form-item>
      <el-form-item label="操作"><el-radio-group v-model="kind" :disabled="Boolean(pending)"><el-radio-button value="Inbound">入库</el-radio-button><el-radio-button value="Outbound">出库</el-radio-button></el-radio-group></el-form-item>
      <el-form-item :label="'数量' + (selected ? '（'+selected.unit+'）' : '')"><el-input-number v-model="quantity" :controls="false" :precision="0" :step="1" :max="2147483647" :min="0" :disabled="Boolean(pending)" placeholder="输入数量" /></el-form-item>
      <el-form-item label="本次操作备注（选填）"><el-input v-model="note" maxlength="500" :disabled="Boolean(pending)" placeholder="如：补货、售出" /></el-form-item>
      <div class="entry-submit"><el-button type="primary" native-type="submit" :loading="busy">{{ pending ? '重试确认' : '确认'+(kind === 'Inbound' ? '入库' : '出库') }}</el-button></div>
    </div></el-form>
    <div v-if="selected" class="selected-stock" :style="productStyle(selected.name)" aria-live="polite">
      <div class="selected-identity"><strong class="product-name">{{selected.name}}</strong><span class="product-spec"> · {{selected.productSpecification || '无产品规格'}} · {{selected.rawMaterialSpecification || '无原料规格'}} · </span><b class="selected-unit product-unit">{{selected.unit}}</b><template v-if="selected.note"><span> · </span><b class="product-note">{{selected.note}}</b></template></div>
      <div class="balance"><span>当前库存</span><strong>{{formatQuantity(selected.quantity)}}</strong><b v-if="isLowStock(selected)" class="negative">库存告急</b></div>
    </div>
    <p v-else class="muted empty-selection">选择商品后，这里显示当前库存和商品备注。</p>
  </section>
  <section class="panel warning-panel" aria-live="polite"><div class="section-title"><h2>库存预警 <span class="warning-count">{{alerts.length}}</span></h2><RouterLink to="/stock-warnings" custom v-slot="{ navigate }"><el-button type="primary" @click="navigate">设置预警值</el-button></RouterLink></div>
    <div v-if="alerts.length" class="warning-list"><article v-for="p in alerts" :key="p.id" :style="productStyle(p.name)" class="warning-item"><div><strong class="product-name negative">{{p.name}}库存告急，请及时处理</strong><p><span class="product-spec">{{p.productSpecification || '无产品规格'}} · {{p.rawMaterialSpecification || '无原料规格'}} · </span><b class="product-unit">{{p.unit}}</b><span v-if="p.note" class="product-note"> · {{p.note}}</span></p><div class="warning-quantities"><span>当前库存 <b>{{formatQuantity(p.quantity)}}</b></span><span>预警值 <b>{{formatQuantity(p.warningQuantity!)}}</b></span></div></div><el-button type="primary" @click="productId=p.id;kind='Inbound'" :disabled="Boolean(pending)">去入库</el-button></article></div>
    <p v-else class="muted no-warning">{{active.some(p=>p.warningQuantity!=null) ? '当前没有低于预警值的商品。' : '尚未设置库存预警值，可按商品设置后在这里查看提醒。'}}</p>
  </section>
</div></template>
<style scoped>
.entry-grid{display:grid;grid-template-columns:minmax(240px,2.2fr) 130px minmax(120px,.8fr) minmax(160px,1fr) 120px;gap:16px;align-items:end}
.entry-grid .el-select,.entry-grid .el-input-number{width:100%}.entry-grid .el-form-item{margin-bottom:0;min-width:0}
.entry-submit{display:flex;align-items:center;height:32px}.entry-submit .el-button{width:100%;height:32px}
.quick-entry{background:linear-gradient(115deg,#fff,#f1faff);min-width:0}
.selected-stock{display:flex;align-items:center;gap:30px;padding:24px;margin-top:24px;border-radius:12px;border-left:4px solid var(--product-accent);overflow-x:auto}
.selected-identity{flex:1;text-align:center;white-space:nowrap;font-size:20px;font-weight:600;line-height:1.6}
.selected-identity strong{font-size:22px}.selected-unit{font-size:26px;font-weight:800}
.balance{display:flex;align-items:center;gap:12px;white-space:nowrap;flex-shrink:0;font-weight:700}.balance>strong{font-size:32px}
.empty-selection{margin:20px 0 0}.warning-count{background:#fff0ed;color:#af4350;border-radius:20px;padding:3px 10px;font-size:15px}
.warning-list{display:grid;grid-template-columns:1fr 1fr;gap:12px}
.warning-item{padding:22px;border-radius:12px;border:1px solid #e9d6d2;display:flex;align-items:center;justify-content:space-between;gap:18px}
.warning-item>div{min-width:0}.warning-item strong{font-size:18px;font-weight:800;line-height:1.6}
.warning-item p{font-size:16px;font-weight:700;margin:10px 0;line-height:1.6}
.warning-quantities{display:flex;flex-wrap:wrap;gap:12px 26px;color:#3b576a;font-size:16px;font-weight:700}
.warning-quantities b{font-size:24px;margin-left:8px}.no-warning{margin:0;padding:12px 0}
@media(max-width:1150px){.entry-grid{grid-template-columns:minmax(200px,1fr) 130px minmax(100px,.6fr)}.entry-grid>.el-form-item:nth-child(4){grid-column:1/3}.warning-list{grid-template-columns:1fr}}
@media(max-width:650px){.entry-grid{grid-template-columns:1fr 1fr}.entry-grid>.el-form-item:first-child{grid-column:1/-1}.entry-grid>.el-form-item:nth-child(4){grid-column:1}.selected-stock{padding:16px}.warning-item{align-items:flex-start}.warning-item .el-button{flex-shrink:0}}
</style>
