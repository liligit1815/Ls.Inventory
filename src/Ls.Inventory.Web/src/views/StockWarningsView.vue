<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, Product } from '@/types/api'
import PageHeader from '@/components/PageHeader.vue'
import { formatQuantity, isLowStock, productLabel, validQuantity } from '@/utils/inventory'
import { productRowStyle } from '@/utils/productColors'
const products=ref<Product[]>([]), search=ref(''), onlyLow=ref(false), page=ref(1), loading=ref(false), saving=ref('')
const table=ref<{clearSelection:()=>void}>()
const selection=ref<Product[]>([]), batchQuantity=ref<number>(), batchEnabled=ref(true)
function selectProducts(rows:Product[]) { selection.value=rows }
function clearSelection() { table.value?.clearSelection(); selection.value=[] }
function applySaved(saved:Product[]) {
  const byId=new Map(saved.map(p=>[p.id,p]))
  products.value=products.value.map(p=>byId.get(p.id)??p)
  for(const p of saved) drafts[p.id]={enabled:p.warningQuantity!=null,quantity:p.warningQuantity??undefined}
}
async function saveBatch() {
  if(saving.value||loading.value)return
  const ids=new Set(selection.value.map(p=>p.id)), targets=products.value.filter(p=>ids.has(p.id))
  if(!targets.length){ElMessage.warning('请先勾选商品');return}
  if(batchEnabled.value&&!validQuantity(batchQuantity.value,true)){ElMessage.warning('请输入 0 至 2147483647 的整数预警值');return}
  const payload={warningQuantity:batchEnabled.value?batchQuantity.value:null,products:targets.map(p=>({id:p.id,version:p.version}))}
  saving.value='batch'
  try {
    try {await ElMessageBox.confirm(payload.warningQuantity===null?`关闭所选 ${targets.length} 种商品的预警？`:`将所选 ${targets.length} 种商品的预警值统一设置为 ${payload.warningQuantity}？所选商品原预警设置将被替换。`,'批量设置预警',{confirmButtonText:'确认保存',cancelButtonText:'返回',type:'warning'})}catch{return}
    const saved=(await api.put<ApiResponse<Product[]>>('/api/v1/products/warnings/batch',payload)).data.data
    applySaved(saved);clearSelection();ElMessage.success(`已保存 ${saved.length} 种商品的预警设置`)
  }catch(e){ElMessage.error(getErrorMessage(e))}finally{saving.value=''}
}
const drafts=reactive<Record<string,{enabled:boolean; quantity?:number}>>({})
const filtered=computed(()=>products.value.filter(p=>productLabel(p).includes(search.value) && (!onlyLow.value || isLowStock(p))))
const alertCount=computed(()=>products.value.filter(isLowStock).length)
async function load(){if(saving.value||loading.value)return;loading.value=true;try{clearSelection();products.value=(await api.get<ApiResponse<Product[]>>('/api/v1/products/')).data.data;for(const p of products.value)drafts[p.id]={enabled:p.warningQuantity!=null,quantity:p.warningQuantity??undefined}}catch(e){ElMessage.error(getErrorMessage(e))}finally{loading.value=false}}
async function save(p:Product){if(saving.value)return;const d=drafts[p.id]!;if(d.enabled&&!validQuantity(d.quantity,true)){ElMessage.warning('请输入 0 至 2147483647 的整数预警值');return}saving.value=p.id;try{const saved=(await api.put<ApiResponse<Product>>(`/api/v1/products/${p.id}/warning`,{warningQuantity:d.enabled?d.quantity:null,version:p.version})).data.data;products.value=products.value.map(x=>x.id===p.id?saved:x);drafts[p.id]={enabled:saved.warningQuantity!=null,quantity:saved.warningQuantity??undefined};ElMessage.success('预警设置已保存，工作台将按新规则提示')}catch(e){ElMessage.error(getErrorMessage(e))}finally{saving.value=''}}
onMounted(load)
</script>
<template><div class="page-shell"><PageHeader title="库存预警" description="可逐商品设置，或勾选多个商品统一设置预警值。"><el-button type="primary" :loading="loading" :disabled="!!saving" @click="load">刷新数据</el-button></PageHeader>
  <section class="panel" v-loading="loading"><div class="section-title"><h2>{{alertCount}} 种商品库存告急</h2></div><div class="toolbar"><el-input v-model="search" placeholder="搜索名称、规格、单位或备注" clearable @input="page=1"/><el-checkbox v-model="onlyLow" @change="page=1">只看库存告急</el-checkbox></div>
    <div class="batch-toolbar"><strong>已选 {{selection.length}} 种商品</strong><el-switch v-model="batchEnabled" active-text="开启预警" inactive-text="关闭预警" :disabled="!!saving" aria-label="批量开启预警"/><el-input-number v-model="batchQuantity" :min="0" :max="2147483647" :precision="0" :step="1" :controls="false" :disabled="!batchEnabled||!!saving" placeholder="统一预警值" aria-label="统一预警值"/><el-button type="primary" :disabled="!selection.length||!!saving" :loading="saving==='batch'" @click="saveBatch">保存所选商品</el-button><el-button :disabled="!selection.length||!!saving" @click="clearSelection">取消选择</el-button></div>
    <el-table class="product-typography" ref="table" row-key="id" @selection-change="selectProducts" :data="filtered.slice((page-1)*20,page*20)" :row-style="productRowStyle"><el-table-column type="selection" width="50" :reserve-selection="true" :selectable="() => !saving"/><el-table-column label="商品" min-width="260"><template #default="s"><strong class="product-name">{{s.row.name}}</strong><div class="spec product-spec">{{s.row.productSpecification || '无产品规格'}} · {{s.row.rawMaterialSpecification || '无原料规格'}}</div><div class="spec">备注：{{s.row.note || '无'}}</div></template></el-table-column><el-table-column prop="unit" class-name="product-unit" label="单位" width="120"/><el-table-column label="当前库存" width="115"><template #default="s"><strong>{{formatQuantity(s.row.quantity)}}</strong></template></el-table-column>
      <el-table-column label="开启预警" width="110"><template #default="s"><el-switch v-model="drafts[s.row.id]!.enabled" :disabled="!!saving" :aria-label="s.row.name+'开启预警'" /></template></el-table-column><el-table-column label="预警值" width="180"><template #default="s"><el-input-number v-model="drafts[s.row.id]!.quantity" :min="0" :precision="0" :step="1" :max="2147483647" :controls="false" :disabled="!drafts[s.row.id]!.enabled || !!saving" placeholder="设置数量" :aria-label="s.row.name+'预警值'" /></template></el-table-column>
      <el-table-column label="当前状态" width="115"><template #default="s"><el-tag :type="isLowStock(s.row)?'danger':'info'">{{!s.row.isActive?'商品已停用':isLowStock(s.row)?'库存告急':s.row.warningQuantity==null?'未配置':'正常'}}</el-tag></template></el-table-column><el-table-column label="操作" width="100" fixed="right"><template #default="s"><el-button type="primary" size="small" :loading="saving===s.row.id" :disabled="!!saving&&saving!==s.row.id" @click="save(s.row)">保存</el-button></template></el-table-column></el-table><el-pagination v-model:current-page="page" :page-size="20" :total="filtered.length" layout="total, prev, pager, next"/>
    <p class="muted" style="margin:18px 0 0">修改后点击对应商品的“保存”生效。关闭并保存可取消该商品预警；预警值 0 表示库存不会触发低库存提醒。</p>
  </section></div></template>
<style scoped>
.batch-toolbar{display:flex;gap:14px;align-items:center;flex-wrap:wrap;padding:16px;margin-bottom:18px;border:1px solid #bcd9e8;background:#f0f8fc;border-radius:12px}
.batch-toolbar strong{font-size:16px;color:#294a60}.batch-toolbar .el-input-number{width:160px}.batch-toolbar .el-button{margin-left:0}
</style>
