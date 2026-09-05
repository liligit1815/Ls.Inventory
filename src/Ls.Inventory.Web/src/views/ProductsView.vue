<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, Product } from '@/types/api'
import { productRowStyle } from '@/utils/productColors'
import PageHeader from '@/components/PageHeader.vue'
import ProductImportDialog from '@/components/ProductImportDialog.vue'
import { productLabel, formatQuantity } from '@/utils/inventory'
const products=ref<Product[]>([]), search=ref(''), page=ref(1), loading=ref(false), saving=ref(false), dialog=ref(false)
const importDialog = ref<InstanceType<typeof ProductImportDialog>>()
const form=reactive({ id:'', name:'', productSpecification:'', rawMaterialSpecification:'', unit:'箱', note:'', isActive:true, version:0 })
const filtered=computed(()=>products.value.filter(p=>productLabel(p).includes(search.value)||p.code.includes(search.value)))
async function load(){ loading.value=true; try{ products.value=(await api.get<ApiResponse<Product[]>>('/api/v1/products/')).data.data }catch(e){ElMessage.error(getErrorMessage(e))}finally{loading.value=false} }
function edit(p?:Product){Object.assign(form,p??{id:'',name:'',productSpecification:'',rawMaterialSpecification:'',unit:'箱',note:'',isActive:true,version:0});dialog.value=true}
async function save(){if(saving.value)return;if(!form.name.trim()||!form.unit.trim()){ElMessage.warning('请填写商品名称和单位');return} saving.value=true;try{if(form.id)await api.put(`/api/v1/products/${form.id}`,form);else await api.post('/api/v1/products/',form);dialog.value=false;ElMessage.success('商品已保存');await load()}catch(e){ElMessage.error(getErrorMessage(e))}finally{saving.value=false}}
onMounted(load)
</script>
<template><div class="page-shell"><PageHeader title="商品档案" description="商品名称、产品规格、原料规格、单位共同区分商品。"><el-button type="primary" @click="importDialog?.open()">导入商品及库存</el-button><el-button type="primary" @click="edit()">新增商品</el-button></PageHeader>
  <ProductImportDialog ref="importDialog" @imported="load" />
  <section class="panel products-panel" v-loading="loading"><div class="toolbar"><el-input v-model="search" placeholder="搜索商品名称、编码或规格" clearable @input="page=1" /><span class="muted">{{ products.length }} 种商品 · 已有库存流水的商品不能变更身份信息</span></div>
    <el-table class="products-table product-typography" :data="filtered.slice((page-1)*20,page*20)" :row-style="productRowStyle" empty-text="暂无商品，点击右上角新增"><el-table-column prop="code" label="编码" width="160" /><el-table-column prop="name" class-name="product-name" label="商品名称" min-width="280" /><el-table-column prop="productSpecification" class-name="product-spec" label="产品规格" width="120" /><el-table-column prop="rawMaterialSpecification" class-name="product-spec" label="原料规格" min-width="140" /><el-table-column prop="unit" label="单位" width="120" class-name="product-unit" /><el-table-column prop="note" class-name="product-note" label="备注" min-width="140" /><el-table-column label="库存" width="100"><template #default="s">{{formatQuantity(s.row.quantity)}}</template></el-table-column><el-table-column label="状态" width="85"><template #default="s"><el-tag :type="s.row.isActive?'success':'info'">{{s.row.isActive?'启用':'停用'}}</el-tag></template></el-table-column><el-table-column label="操作" width="96" fixed="right"><template #default="s"><el-button type="primary" @click="edit(s.row)">编辑</el-button></template></el-table-column></el-table>
    <el-pagination v-model:current-page="page" :page-size="20" :total="filtered.length" layout="total, prev, pager, next" />
  </section>
  <el-dialog v-model="dialog" :title="form.id?'编辑商品':'新增商品'" width="620px" :close-on-click-modal="false"><el-form class="product-typography" label-position="top" @submit.prevent="save"><div class="form-grid">
    <el-form-item label="商品名称（必填）" class="wide"><el-input class="product-name" v-model="form.name" maxlength="200" /></el-form-item><el-form-item label="产品规格"><el-input class="product-spec" v-model="form.productSpecification" maxlength="100" /></el-form-item><el-form-item label="原料规格"><el-input class="product-spec" v-model="form.rawMaterialSpecification" maxlength="100" /></el-form-item><el-form-item label="单位（必填）"><el-input class="product-unit" v-model="form.unit" maxlength="30" placeholder="如：箱、个" /></el-form-item><el-form-item label="状态"><el-switch v-model="form.isActive" active-text="启用" inactive-text="停用" /></el-form-item><el-form-item label="备注" class="wide"><el-input class="product-note" v-model="form.note" type="textarea" maxlength="500" /></el-form-item>
    </div><p class="muted">新商品库存为零，请在工作台入库；库存不在商品档案中直接修改。</p><el-button type="primary" native-type="submit" :loading="saving">保存商品</el-button></el-form></el-dialog>
</div></template>
<style scoped>
.products-panel{min-width:0}
.products-panel .toolbar .muted{font-size:15px;font-weight:600;color:#49677b}
.products-panel :deep(.el-input__inner){font-size:15px;font-weight:600}
.products-table :deep(.el-tag){font-size:14px;font-weight:700}
.products-table :deep(.el-button){font-size:14px;font-weight:700}
.products-panel :deep(.el-pagination){font-size:15px;font-weight:600}
</style>
