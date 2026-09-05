<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, Stocktake, StocktakeSummary } from '@/types/api'
import { productRowStyle } from '@/utils/productColors'
import PageHeader from '@/components/PageHeader.vue'
import { formatDateTime, statusLabel, validQuantity, formatQuantity } from '@/utils/inventory'
const records=ref<StocktakeSummary[]>([]), current=ref<Stocktake>(), busy=ref(false), loading=ref(false)
const missing=computed(()=>current.value?.lines.filter(x=>!validQuantity(x.countedQuantity,true)).length??0)
const changed=computed(()=>current.value?.lines.some(x=>x.hasChanged)??false)
async function list(){try{records.value=(await api.get<ApiResponse<StocktakeSummary[]>>('/api/v1/stocktakes/')).data.data}catch(e){ElMessage.error(getErrorMessage(e))}}
async function open(id:string){loading.value=true;try{const s=(await api.get<ApiResponse<Stocktake>>(`/api/v1/stocktakes/${id}`)).data.data;s.lines.forEach(l=>{if(l.countedQuantity==null)l.countedQuantity=undefined});current.value=s}catch(e){ElMessage.error(getErrorMessage(e))}finally{loading.value=false}}
async function start(){if(busy.value)return;busy.value=true;try{const r=await api.post<ApiResponse<{id:string}>>('/api/v1/stocktakes/');await list();await open(r.data.data.id)}catch(e){ElMessage.error(getErrorMessage(e))}finally{busy.value=false}}
async function confirm(){
  if(busy.value||!current.value||missing.value||changed.value)return
  const id=current.value.id, payload={counts:current.value.lines.map(x=>({productId:x.productId,quantity:x.countedQuantity}))}
  busy.value=true
  try{
    try{await ElMessageBox.confirm('确认后将以实盘数量更新库存，差异自动记录为盘盈或盘亏。是否继续？','确认盘库',{confirmButtonText:'确认盘库',cancelButtonText:'继续核对',type:'warning'})}catch{return}
    await api.post(`/api/v1/stocktakes/${id}/confirm`,payload)
    current.value=undefined
    ElMessage.success('盘库已确认，库存与流水已更新')
    await list()
  }catch(e){ElMessage.error(getErrorMessage(e))}finally{busy.value=false}
}
async function cancel(){
  if(busy.value||!current.value)return
  const id=current.value.id
  busy.value=true
  try{
    try{await ElMessageBox.confirm('取消本次盘库？库存不会发生变化。','取消盘库',{confirmButtonText:'确认取消',cancelButtonText:'返回'})}catch{return}
    await api.post(`/api/v1/stocktakes/${id}/cancel`)
    current.value=undefined
    ElMessage.success('本次盘库已取消')
    await list()
  }catch(e){ElMessage.error(getErrorMessage(e))}finally{busy.value=false}
}
onMounted(list)
</script>
<template><div class="page-shell"><PageHeader title="盘库" description="清点实物，填写实际数量，确认后自动处理库存差异。"><el-button type="primary" :loading="busy" @click="start">开始盘库</el-button></PageHeader>
  <section v-if="current" class="panel stocktake-detail" v-loading="loading"><div class="section-title"><h2>{{statusLabel(current.status)}} · {{formatDateTime(current.createdAt)}}</h2><el-button :disabled="busy" @click="current=undefined">返回列表</el-button></div>
    <el-alert v-if="current.status==='Draft'" :type="changed?'warning':'info'" :closable="false" :title="changed?'部分商品已发生变化，请取消后重新开始盘库':'请填写每种商品的实盘数量，零库存请明确填 0。填写内容在确认前不修改库存；离开页面前请完成确认。'" />
    <el-table class="product-typography" :data="current.lines" :row-style="productRowStyle" max-height="620"><el-table-column label="商品" min-width="260"><template #default="s"><span class="product-name">{{s.row.name}}</span><div class="spec product-spec">{{s.row.productSpecification}} / {{s.row.rawMaterialSpecification}}</div></template></el-table-column><el-table-column prop="unit" label="单位" width="120" class-name="product-unit" /><el-table-column prop="expectedQuantity" label="盘库时账面库存" width="175" /><el-table-column label="实盘数量" width="190"><template #default="s"><el-input-number v-if="current.status==='Draft'" v-model="s.row.countedQuantity" :min="0" :precision="0" :step="1" :max="2147483647" :controls="false" :disabled="busy" placeholder="请填写" /><span v-else>{{s.row.countedQuantity??'—'}}</span></template></el-table-column><el-table-column label="差异" width="110"><template #default="s"><span v-if="s.row.countedQuantity!=null" :class="s.row.countedQuantity<s.row.expectedQuantity?'negative':'positive'">{{formatQuantity(s.row.countedQuantity-s.row.expectedQuantity)}}</span><span v-else>待填写</span></template></el-table-column></el-table>
    <div v-if="current.status==='Draft'" class="toolbar" style="margin-top:22px;margin-bottom:0"><span class="muted">{{missing?`还有 ${missing} 种商品待填写`:'已填写完整，可确认盘库'}}</span><el-button class="right" :disabled="busy" @click="cancel">取消盘库</el-button><el-button type="primary" :disabled="missing>0||changed" :loading="busy" @click="confirm">确认并更新库存</el-button></div>
  </section>
  <section v-else class="panel"><h2>盘库记录</h2><el-table :data="records" empty-text="暂无盘库记录"><el-table-column label="开始时间" min-width="190"><template #default="s">{{formatDateTime(s.row.createdAt)}}</template></el-table-column><el-table-column label="状态" width="130"><template #default="s">{{statusLabel(s.row.status)}}</template></el-table-column><el-table-column prop="lineCount" label="商品数" width="110" /><el-table-column label="完成时间" min-width="190"><template #default="s">{{formatDateTime(s.row.postedAt)}}</template></el-table-column><el-table-column label="操作" width="100"><template #default="s"><el-button type="primary" size="small" @click="open(s.row.id)">{{s.row.status==='Draft'?'继续盘库':'查看'}}</el-button></template></el-table-column></el-table><p class="muted">展示最近 100 次记录。一次仅保留一个进行中的盘库任务。</p></section>
</div></template>
<style scoped>
.stocktake-detail{min-width:0;font-size:16px;font-weight:600}
.stocktake-detail h2{font-size:22px;font-weight:800}
.stocktake-detail :deep(.el-input__inner){font-size:18px;font-weight:700}
.stocktake-detail :deep(.el-alert__title),.stocktake-detail .muted{font-size:15px;font-weight:600;line-height:1.7}
.stocktake-detail :deep(.el-button){font-size:15px;font-weight:700}
</style>
