<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { api, getErrorMessage } from '@/api/http'
import type { ApiResponse, Product } from '@/types/api'
import { identityKey, prepareImport, type ImportRow } from '@/utils/productImport'
import { productRowStyle } from '@/utils/productColors'
import { formatQuantity } from '@/utils/inventory'

const emit = defineEmits<{ imported: [] }>()
const visible = ref(false), busy = ref(false), saving = ref(false), fileName = ref(''), error = ref(''), page = ref(1)
const input = ref<HTMLInputElement>(), rows = ref<ImportRow[]>([]), existing = ref<Product[]>([])
const sourceCount = ref(0), skippedSheets = ref<string[]>([])
const checked = computed(() => prepareImport(rows.value, existing.value))
const candidates = computed(() => checked.value.filter(x => x.row.selected && !x.product))
const unresolved = computed(() => candidates.value.filter(x => x.issue))
const createdCount = computed(() => candidates.value.filter(x => !x.product).length)
const skippedCount = computed(() => checked.value.filter(x => x.product).length)
type Payload = { requestId: string; fileName: string; rows: { name: string; productSpecification: string; rawMaterialSpecification: string; unit: string; currentQuantity: number; note: string; allowEmptySpecifications: boolean }[] }
const pending = ref<Payload>()
const canConfirm = computed(() => !busy.value && !saving.value && (!!pending.value || (candidates.value.length > 0 && unresolved.value.length === 0)))

function open() {
  rows.value = []; fileName.value = ''; error.value = ''; pending.value = undefined; page.value = 1; visible.value = true
}
defineExpose({ open })
async function choose(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (input.value) input.value.value = ''
  if (!file || busy.value || saving.value || pending.value) return
  rows.value = []; fileName.value = ''; error.value = ''
  if (!file.name.toLowerCase().endsWith('.xlsx')) { error.value = '仅支持 .xlsx，旧版 .xls 请另存为 .xlsx'; return }
  if (file.size > 10 * 1024 * 1024) { error.value = 'Excel 文件不能超过 10 MB'; return }
  busy.value = true
  try {
    const form = new FormData(); form.append('file', file)
    const [preview, products] = await Promise.all([
      api.post<ApiResponse<{ rows: Omit<ImportRow, 'selected' | 'allowEmptySpecifications'>[]; sourceRowCount: number; skippedSheets: string[] }>>('/api/v1/products/import/preview', form, { timeout: 60000 }),
      api.get<ApiResponse<Product[]>>('/api/v1/products/'),
    ])
    existing.value = products.data.data
    const keys = new Set(existing.value.map(identityKey))
    rows.value = preview.data.data.rows.map(r => ({ ...r, selected: !keys.has(identityKey(r)), allowEmptySpecifications: false }))
    sourceCount.value = preview.data.data.sourceRowCount; skippedSheets.value = preview.data.data.skippedSheets
    fileName.value = file.name; page.value = 1
  } catch (e) { error.value = getErrorMessage(e) } finally { busy.value = false }
}
async function confirm() {
  if (!canConfirm.value) return
  saving.value = true; error.value = ''
  try {
    if (!pending.value) {
      pending.value = { requestId: crypto.randomUUID(), fileName: fileName.value,
        rows: candidates.value.map(({ row }) => ({ name: row.name, productSpecification: row.productSpecification,
          rawMaterialSpecification: row.rawMaterialSpecification, unit: row.unit, currentQuantity: Number(row.currentQuantity), note: row.note,
          allowEmptySpecifications: row.allowEmptySpecifications })) }
    }
    const result = (await api.post<ApiResponse<{ created: number; skipped: number; alreadyPosted: boolean }>>('/api/v1/products/import/confirm', pending.value, { timeout: 60000 })).data.data
    ElMessage.success(`导入完成：新增 ${result.created} 种商品，跳过 ${skippedCount.value + result.skipped} 条已有商品；已有库存和备注未修改`)
    pending.value = undefined; visible.value = false; emit('imported')
  } catch (e) {
    const status = (e as { response?: { status?: number } }).response?.status
    if (status && status >= 400 && status < 500) pending.value = undefined
    error.value = getErrorMessage(e) + (pending.value ? '。结果尚未确认，请点击“重试确认”，不会重复记账。' : '')
  } finally { saving.value = false }
}
function selectNew() { for (const item of checked.value) item.row.selected = !item.product }
function clearSelection() { rows.value.forEach(r => r.selected = false) }
function rowClass({ row }: { row: ReturnType<typeof prepareImport>[number] }) { return row.row.selected && row.issue ? 'needs-completion' : '' }
async function beforeClose(done: () => void) {
  if (busy.value || saving.value) return
  if (pending.value) { ElMessage.warning('上次导入结果尚未确认，请先重试确认'); return }
  if (rows.value.length) {
    try { await ElMessageBox.confirm('关闭后将丢弃本次预览和修改，数据尚未导入。', '关闭导入', { confirmButtonText: '关闭', cancelButtonText: '继续核对' }) } catch { return }
  }
  done()
}
</script>
<template>
  <el-dialog v-model="visible" title="导入商品及现有库存" width="min(1500px, 96vw)" :close-on-click-modal="false" :before-close="beforeClose" :close-on-press-escape="!busy && !saving" :show-close="!busy && !saving">
    <p class="import-scope">选择模板 → 核对新商品和库存 → 确认导入。仅导入新商品及其备注、现有库存；已有相同商品自动跳过，不覆盖、不累加。</p>
    <div class="import-toolbar">
      <input ref="input" class="file-input" type="file" accept=".xlsx" aria-label="选择商品及库存 Excel 文件" @change="choose" />
      <el-button type="primary" :loading="busy" :disabled="saving || !!pending" @click="input?.click()">{{rows.length ? '重新选择 Excel' : '选择 Excel'}}</el-button>
      <el-button type="primary" tag="a" href="/api/v1/products/import/template">下载当前模板</el-button>
      <span>{{ fileName || '请使用“商品信息及现有库存模板.xlsx”（不超过 10 MB）' }}</span>
    </div>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon class="import-message" />
    <div v-if="rows.length">
      <p>读取 {{ sourceCount }} 条商品记录；将新增 {{ createdCount }} 种，自动跳过 {{ skippedCount }} 条已有商品，{{ unresolved.length }} 条待处理。</p>
      <el-alert v-if="skippedCount === rows.length" title="所有商品均已存在，无需导入，现有库存和备注保持不变。" type="info" :closable="false" show-icon />
      <p v-if="skippedSheets.length" class="muted">未读取：{{ skippedSheets.join('、') }}</p>
      <p class="muted">名称、两项规格、单位共同区分商品，备注作为说明。已有商品（含停用商品）自动跳过。新商品空白库存不能当成 0，重复商品不会累加。</p>
      <el-form :disabled="saving || !!pending">
        <div class="import-toolbar"><el-button type="primary" size="small" @click="selectNew">仅选择新商品</el-button><el-button type="primary" size="small" @click="clearSelection">全部取消</el-button></div>
        <el-table class="product-typography" :data="checked.slice((page - 1) * 20, page * 20)" max-height="430" :row-class-name="rowClass" :row-style="productRowStyle">
          <el-table-column label="导入" width="60"><template #default="s"><el-checkbox :model-value="s.row.row.selected && !s.row.product" :disabled="!!s.row.product" :aria-label="'选择商品 ' + s.row.row.name" @change="s.row.row.selected = !!$event" /></template></el-table-column>
          <el-table-column label="商品名称" min-width="240"><template #default="s"><el-input class="product-name" v-model="s.row.row.name" maxlength="200" aria-label="商品名称" /></template></el-table-column>
          <el-table-column label="产品规格" min-width="115"><template #default="s"><el-input class="product-spec" v-model="s.row.row.productSpecification" maxlength="100" aria-label="产品规格" @input="s.row.row.allowEmptySpecifications = false" /></template></el-table-column>
          <el-table-column label="原料规格" min-width="135"><template #default="s"><el-input class="product-spec" v-model="s.row.row.rawMaterialSpecification" maxlength="100" aria-label="原料规格" @input="s.row.row.allowEmptySpecifications = false" /></template></el-table-column>
          <el-table-column label="单位" width="110"><template #default="s"><el-input class="product-unit" v-model="s.row.row.unit" maxlength="30" aria-label="单位" /></template></el-table-column>
          <el-table-column label="导入现有库存" width="135"><template #default="s"><el-input v-model="s.row.row.currentQuantity" inputmode="numeric" aria-label="导入现有库存" /></template></el-table-column>
          <el-table-column label="系统库存 / 本次导入" width="155"><template #default="s">{{formatQuantity(s.row.product?.quantity ?? 0)}} / {{s.row.product ? '跳过' : s.row.row.currentQuantity.trim() && Number.isFinite(s.row.difference) ? formatQuantity(s.row.difference) : '待填写'}}</template></el-table-column>
          <el-table-column label="备注" min-width="140"><template #default="s"><el-input class="product-note" v-model="s.row.row.note" maxlength="500" aria-label="备注" /></template></el-table-column>
          <el-table-column label="检查结果" min-width="230"><template #default="s"><div>{{s.row.status}}</div><el-checkbox v-if="!s.row.product && (!s.row.row.productSpecification.trim() || !s.row.row.rawMaterialSpecification.trim())" v-model="s.row.row.allowEmptySpecifications">空白规格确认为无</el-checkbox></template></el-table-column>
          <el-table-column label="来源 / 行号" min-width="145" show-overflow-tooltip><template #default="s">{{s.row.row.sources.join('、')}}</template></el-table-column>
        </el-table>
      </el-form>
      <el-pagination v-model:current-page="page" :page-size="20" :total="rows.length" layout="total, prev, pager, next" />
    </div>
    <template #footer><span class="muted footer-note">新商品库存记录可追溯，已有商品保持不变。</span><el-button type="primary" :disabled="!canConfirm" :loading="saving" @click="confirm">{{pending ? '重试确认' : '确认导入商品及库存'}}</el-button></template>
  </el-dialog>
</template>
<style scoped>
.file-input{display:none}.import-toolbar{display:flex;gap:14px;align-items:center;flex-wrap:wrap;margin:16px 0}.import-scope{line-height:1.7;color:#345870}.import-message{margin:12px 0}.footer-note{display:inline-block;margin:0 16px 10px 0}:deep(.needs-completion){--el-table-tr-bg-color:#fff3ed}:deep(.el-table .el-input){min-width:70px}
</style>
