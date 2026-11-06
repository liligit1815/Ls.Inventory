<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, Product } from '@/types/api'
import { productRowStyle } from '@/utils/productColors'
import PageHeader from '@/components/PageHeader.vue'
import ProductImportDialog from '@/components/ProductImportDialog.vue'
import ProductHistoryDialog from '@/components/ProductHistoryDialog.vue'
import ProductComparison from '@/components/ProductComparison.vue'
import { PRODUCT_PAGE_SIZE, productLabel, formatQuantity, categoryOptions, productsInCategory, sortProducts } from '@/utils/inventory'
import ProductTable from '@/components/ProductTable.vue'
import { productTableColumns, type TableColumn } from '@/utils/tableColumns'
const columns: TableColumn[] = [
  ...productTableColumns().map(column => ({ ...column, hidden: false })),
  { key: 'quantity', label: '库存', width: 90, minWidth: 70 },
  { key: 'status', label: '状态', width: 80, minWidth: 65 },
  { key: 'actions', label: '操作', width: 158, minWidth: 148, fixed: 'right' },
]
const products=ref<Product[]>([]), search=ref(''), page=ref(1), loading=ref(false), saving=ref(false), dialog=ref(false)
const importDialog = ref<InstanceType<typeof ProductImportDialog>>()
const historyProduct = ref<Product | null>(null)
const original = ref<Product | null>(null), pending = ref<Product>(), reviewing = ref(false)
const category = ref<string | null>(null)
const status = ref<'all' | 'active' | 'inactive'>('all')
const categories = computed(() => categoryOptions(products.value))
const blank: Product = { id:'', code:'', category:'', name:'', productSpecification:'', rawMaterialSpecification:'', unit:'箱', note:'', isActive:true, version:0, quantity:0, warningQuantity:null }
const form = reactive<Product>({...blank})
const filtered=computed(()=>sortProducts(productsInCategory(products.value, category.value).filter(p=>
  (status.value==='all' || p.isActive===(status.value==='active')) && (productLabel(p).includes(search.value)||p.code.includes(search.value)))))
watch([category, search, status], () => { page.value=1 })
watch(filtered, rows => { page.value=Math.min(page.value, Math.max(1, Math.ceil(rows.length/PRODUCT_PAGE_SIZE))) })
async function load(){ loading.value=true; try{ products.value=(await api.get<ApiResponse<Product[]>>('/api/v1/products/', { params: { includeInactive: true } })).data.data }catch(e){ElMessage.error(getErrorMessage(e))}finally{loading.value=false} }
function edit(p?: Product) {
  original.value = p ? {...p} : null
  Object.assign(form, p ?? blank)
  dialog.value = true
}
function review() {
  if (saving.value || reviewing.value) return
  const payload = { ...form, name: form.name.trim(), code: form.code.trim(), unit: form.unit.trim(),
    productSpecification: form.productSpecification.trim(), rawMaterialSpecification: form.rawMaterialSpecification.trim(),
    note: form.note.trim(), category: (form.category || '').trim(), warningQuantity: form.warningQuantity ?? null }
  if (!payload.name || !payload.unit) { ElMessage.warning('请填写商品名称和单位'); return }
  if (payload.id && !payload.code) { ElMessage.warning('请填写商品编码'); return }
  if (payload.category.length > 100) { ElMessage.warning('商品类目不能超过100字'); return }
  if (!payload.isActive && payload.quantity !== 0) { ElMessage.warning('仍有库存的商品不能停用'); return }
  pending.value = payload
  reviewing.value = true
}
async function save() {
  const payload = pending.value
  if (!reviewing.value || !payload || saving.value) return
  saving.value = true
  try {
    if (payload.id) await api.put(`/api/v1/products/${payload.id}`, { ...payload, confirmChanges: true })
    else await api.post('/api/v1/products/', payload)
    reviewing.value = false
    dialog.value = false
    ElMessage.success('商品已保存')
    await load()
  } catch (e) {
    ElMessage.error(getErrorMessage(e))
    reviewing.value = false
    await load()
  } finally { saving.value = false }
}
onMounted(load)
</script>
<template><div class="page-shell"><PageHeader title="商品档案"><el-button type="primary" @click="importDialog?.open()">导入商品及库存</el-button><el-button type="primary" @click="edit()">新增商品</el-button></PageHeader>
  <ProductImportDialog ref="importDialog" @imported="load" />
  <ProductHistoryDialog v-if="historyProduct" :product="historyProduct" @close="historyProduct=null" />
  <section class="panel products-panel" v-loading="loading"><div class="toolbar">
    <el-select v-model="category" :empty-values="[undefined]" aria-label="商品类目筛选" placeholder="全部分类" style="width:200px"><el-option :value="null" label="全部分类" /><el-option value="" label="未分类" /><el-option v-for="item in categories" :key="item" :value="item" :label="item" /></el-select>
    <el-select v-model="status" aria-label="商品状态筛选" style="width:160px"><el-option value="all" label="全部状态" /><el-option value="active" label="启用" /><el-option value="inactive" label="停用" /></el-select>
    <el-input v-model="search" placeholder="搜索商品名称、编码或规格" clearable />
    <span class="muted">{{ filtered.length }} 种商品</span>
  </div>
    <ProductTable class="products-table" storage-key="products" :columns="columns" :data="filtered.slice((page-1)*PRODUCT_PAGE_SIZE,page*PRODUCT_PAGE_SIZE)" row-key="id" :row-style="productRowStyle" :empty-text="products.length?'没有符合筛选条件的商品':'暂无商品，点击右上角新增'">
      <template #category="{row}">{{row.category || '未分类'}}</template>
      <template #quantity="{row}">{{formatQuantity(row.quantity)}}</template>
      <template #status="{row}"><el-tag :type="row.isActive?'success':'info'">{{row.isActive?'启用':'停用'}}</el-tag></template>
      <template #actions="{row}"><div class="row-actions"><el-button link @click="edit(row)">编辑</el-button><el-button link @click="historyProduct=row">变更历史</el-button></div></template>
    </ProductTable>
    <el-pagination v-model:current-page="page" :page-size="PRODUCT_PAGE_SIZE" :total="filtered.length" layout="total, prev, pager, next" />
  </section>
  <el-dialog v-model="dialog" :title="form.id?'编辑商品':'新增商品'" width="620px" :close-on-click-modal="false"><el-form class="product-typography" label-position="top" @submit.prevent="review"><div class="form-grid">
    <el-form-item label="商品编码"><el-input v-model="form.code" maxlength="80" placeholder="留空自动生成" /></el-form-item>
    <el-form-item label="预警数量"><el-input-number v-model="form.warningQuantity" :min="0" :max="2147483647" :precision="0" :controls="false" placeholder="留空为未设置" /></el-form-item>
    <el-form-item label="商品类目" class="wide"><el-select v-model="form.category" filterable allow-create default-first-option clearable placeholder="选择已有分类或输入新分类，留空为未分类" style="width:100%"><el-option v-for="item in categories" :key="item" :value="item" :label="item" /></el-select></el-form-item><el-form-item label="商品名称（必填）" class="wide"><el-input class="product-name" v-model="form.name" maxlength="200" /></el-form-item><el-form-item label="产品规格"><el-input class="product-spec" v-model="form.productSpecification" maxlength="100" /></el-form-item><el-form-item label="原料规格"><el-input class="product-spec" v-model="form.rawMaterialSpecification" maxlength="100" /></el-form-item><el-form-item label="单位（必填）"><el-input class="product-unit" v-model="form.unit" maxlength="30" placeholder="如：箱、个" /></el-form-item><el-form-item label="状态"><el-switch v-model="form.isActive" active-text="启用" inactive-text="停用" /></el-form-item><el-form-item label="备注" class="wide"><el-input class="product-note" v-model="form.note" type="textarea" maxlength="500" /></el-form-item>
    </div><p class="muted">库存由出入库和盘库记录更新。保存前可以核对全部商品资料。</p><el-button type="primary" native-type="submit">保存商品</el-button></el-form></el-dialog>
  <el-dialog v-model="reviewing" title="确认保存商品资料" width="720px" top="6vh" :close-on-click-modal="false" :show-close="!saving" :close-on-press-escape="!saving">
    <template v-if="pending">
      <p class="review-note">{{ original ? '请核对左右两列，箭头标出本次修改。' : '本次为新增商品，请核对右列的建档信息。' }}</p>
      <el-alert v-if="original && original.unit!==pending.unit" type="warning" :closable="false" show-icon
        :title="`单位将由「${original.unit}」改为「${pending.unit}」。账面库存仍为 ${formatQuantity(pending.quantity)}，数量不自动换算。流水列表使用新单位。`"/>
      <ProductComparison :before="original" :after="pending" :quantity="pending.quantity" all />
    </template>
    <template #footer><el-button :disabled="saving" @click="reviewing=false">返回编辑</el-button><el-button type="primary" :loading="saving" @click="save">确认保存</el-button></template>
  </el-dialog>
</div></template>
<style scoped>
.products-panel{min-width:0}
.products-panel .toolbar .muted{font-size:15px;font-weight:600;color:#49677b}
.products-panel :deep(.el-input__inner){font-size:15px;font-weight:600}
.products-table :deep(.el-tag){font-size:14px;font-weight:700}
.products-table :deep(.el-button){font-size:14px;font-weight:700}
.row-actions{display:flex;align-items:center;gap:12px}
.row-actions .el-button{margin:0;color:var(--ice-600)}
.review-note{color:#4c697e;margin-bottom:12px}.review-note+.el-alert{margin-bottom:12px}
.products-panel :deep(.el-pagination){font-size:15px;font-weight:600}
</style>
