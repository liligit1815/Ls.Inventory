import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { Script } from 'node:vm'
import { webcrypto } from 'node:crypto'
import { test } from 'node:test'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
const require=createRequire(import.meta.url)
function evaluate(source,mocks={}){
  const module={exports:{}}
  const code=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText
  new Script(code).runInNewContext({module,exports:module.exports,require:name=>mocks[name]??require(name),console,crypto:webcrypto})
  return module.exports
}
const utils=evaluate(readFileSync(new URL('../src/utils/inventory.ts',import.meta.url),'utf8'))
const colors=evaluate(readFileSync(new URL('../src/utils/productColors.ts',import.meta.url),'utf8'))
const imports=evaluate(readFileSync(new URL('../src/utils/productImport.ts',import.meta.url),'utf8'))
const importRow=(overrides={})=>({name:'杯',productSpecification:'2.3',rawMaterialSpecification:'铝',unit:'箱',currentQuantity:'0',note:'',sources:['商品信息!2'],selected:true,allowEmptySpecifications:false,...overrides})

function stocktakeVm(post,confirm=async()=>{}) {
  const {descriptor}=parse(readFileSync(new URL('../src/views/StocktakesView.vue',import.meta.url),'utf8'))
  const component=evaluate(compileScript(descriptor,{id:'stocktake-navigation-test'}).content,{
    vue:{...vue,onMounted(){}},'element-plus':{ElMessage:{success(){},error(){}},ElMessageBox:{confirm}},
    '@/api/http':{api:{post,get:async()=>({data:{data:[{id:'stocktake',status:'Posted'}]}})},getErrorMessage:String},
    '@/components/PageHeader.vue':{},'@/utils/inventory':utils,'@/utils/productColors':colors,
  }).default
  const vm=component.setup({}, {expose(){}})
  vm.current.value={id:'stocktake',status:'Draft',lines:[{productId:'a',countedQuantity:3,hasChanged:false}]}
  return vm
}
for(const action of ['confirm','cancel']) test('stocktake '+action+' returns to refreshed list only after success',async()=>{
  const calls=[];const vm=stocktakeVm(async(url,payload)=>{calls.push({url,payload})})
  await vm[action]();assert.equal(vm.current.value,undefined);assert.equal(vm.records.value[0].id,'stocktake');assert.equal(calls[0].url,'/api/v1/stocktakes/stocktake/'+action)
})
test('stocktake errors and dismissed confirmations preserve current entry',async()=>{
  for(const action of ['confirm','cancel']) {
    const vm=stocktakeVm(async()=>{throw Error('failed')});await vm[action]();assert.equal(vm.current.value.id,'stocktake');assert.equal(vm.current.value.lines[0].countedQuantity,3)
    let calls=0;const cancelled=stocktakeVm(async()=>{calls++},async()=>{throw Error('dismissed')});await cancelled[action]();assert.equal(calls,0);assert.equal(cancelled.current.value.id,'stocktake');assert.equal(cancelled.busy.value,false)
  }
})
test('stocktake prevents duplicate confirmation requests',async()=>{
  let release,calls=0;const vm=stocktakeVm(async()=>{calls++;return new Promise(r=>{release=r})})
  const first=vm.confirm();await Promise.resolve();await vm.confirm();await vm.cancel();assert.equal(calls,1);release();await first;assert.equal(vm.current.value,undefined)
})
test('password form uses six character minimum without complexity restrictions',()=>{
  const source=readFileSync(new URL('../src/components/ChangePasswordDialog.vue',import.meta.url),'utf8')
  const {descriptor}=parse(source)
  const component=evaluate(compileScript(descriptor,{id:'password-test'}).content,{
    vue,'element-plus':{ElMessage:{}},'@/api/http':{},'@/stores/auth':{useAuthStore:()=>({})},
  }).default
  const vm=component.setup({}, {expose(){}});assert.equal(vm.rules.newPassword[1].min,6);assert.equal(vm.rules.newPassword[1].max,128)
  assert.ok(source.includes('支持纯数字'));assert.ok(!source.includes('大小写'))
})

test('import stock requires explicit non-negative quantity and missing specs confirmation',()=>{
  assert.equal(imports.importIssue(importRow()),'')
  for(const value of ['', '-1', 'abc', '1.5', '1.1234567', '2147483648', 'Infinity']) assert.ok(imports.importIssue(importRow({currentQuantity:value})))
  for(const value of ['0','2147483647','12.000']) assert.equal(imports.importIssue(importRow({currentQuantity:value})), '')
  assert.ok(imports.importIssue(importRow({productSpecification:''})))
  assert.equal(imports.importIssue(importRow({productSpecification:'',allowEmptySpecifications:true})), '')
})
test('import keeps different specifications and units separate and rejects duplicate stock rows',()=>{
  const result=imports.prepareImport([importRow(),importRow({unit:'个'}),importRow({productSpecification:'2.4'}),importRow({currentQuantity:'3'})],[])
  assert.equal(result[1].duplicate,false);assert.equal(result[2].duplicate,false);assert.equal(result[3].duplicate,true);assert.ok(result[3].issue)
})
test('existing import is skipped even with different quantity and note',()=>{
  const result=imports.prepareImport([importRow({currentQuantity:'3'})],[{...importRow(),id:'product',quantity:10,version:2,isActive:true}])[0]
  assert.equal(result.difference,0);assert.equal(result.product.id,'product');assert.ok(result.status.includes('自动跳过'));assert.equal(result.issue,'')
})
function dashboard(post){
  const source=readFileSync(new URL('../src/views/DashboardView.vue',import.meta.url),'utf8')
  const {descriptor}=parse(source)
  const compiled=compileScript(descriptor,{id:'lite-dashboard-test'})
  const component=evaluate(compiled.content,{
    vue:{...vue,onMounted(){},onBeforeUnmount(){}},'element-plus':{ElMessage:{success(){},warning(){},error(){}}},
    '@/api/http':{api:{post,get:async url=>({data:{data:url.includes('products')?[]:{items:[]}}})},getErrorMessage:e=>String(e)},
    '@/components/PageHeader.vue':{},'@/components/InventoryChart.vue':{},'@/utils/inventory':utils,'@/utils/productColors':colors,
  }).default
  const vm=component.setup({}, {expose(){}})
  vm.productId.value='product';vm.quantity.value=3
  return vm
}

function importDialog(post) {
  const {descriptor}=parse(readFileSync(new URL('../src/components/ProductImportDialog.vue',import.meta.url),'utf8'))
  const compiled=compileScript(descriptor,{id:'import-dialog-test'})
  const component=evaluate(compiled.content,{
    vue,'element-plus':{ElMessage:{success(){},warning(){},error(){}},ElMessageBox:{confirm:async()=>true}},
    '@/api/http':{api:{post},getErrorMessage:e=>String(e)},'@/utils/productImport':imports,'@/utils/inventory':utils,'@/utils/productColors':colors,
  }).default
  const vm=component.setup({}, {expose(){},emit(){}})
  vm.rows.value=[importRow({currentQuantity:'12'})]; vm.fileName.value='商品信息及现有库存模板.xlsx'
  return vm
}
test('import confirmation sends current balance and no historical movements',async()=>{
  const calls=[];const vm=importDialog(async(url,payload)=>{calls.push({url,payload});return{data:{data:{created:1,skipped:0}}}})
  await vm.confirm();assert.equal(calls.length,1);assert.equal(calls[0].payload.rows[0].currentQuantity,12)
  assert.ok(calls[0].payload.requestId);assert.equal(calls[0].payload.rows[0].kind,undefined)
})
test('all existing rows cannot be submitted even if selected',async()=>{
  let calls=0;const vm=importDialog(async()=>{calls++})
  vm.existing.value=[{...importRow(),id:'existing',quantity:50,version:1,isActive:false}]
  vm.rows.value[0].currentQuantity='';vm.rows.value[0].note='different'
  assert.equal(vm.skippedCount.value,1);assert.equal(vm.candidates.value.length,0);assert.equal(vm.unresolved.value.length,0)
  await vm.confirm();assert.equal(calls,0)
})
test('mixed import submits only new rows with their notes and stock',async()=>{
  const calls=[];const vm=importDialog(async(_url,payload)=>{calls.push(payload);return{data:{data:{created:1,skipped:0}}}})
  vm.existing.value=[{...importRow(),id:'existing',quantity:50,version:1,isActive:true}]
  vm.rows.value.push(importRow({unit:'个',note:'新备注',currentQuantity:'4'}))
  await vm.confirm();assert.equal(calls.length,1);assert.equal(calls[0].rows.length,1);assert.equal(calls[0].rows[0].unit,'个');assert.equal(calls[0].rows[0].note,'新备注');assert.equal(calls[0].rows[0].currentQuantity,4)
})
test('uncertain import retries immutable payload, even if local fields change',async()=>{
  const calls=[];const vm=importDialog(async(_url,payload)=>{calls.push(JSON.parse(JSON.stringify(payload)));if(calls.length===1)throw Error('network');return{data:{data:{created:1,skipped:0}}}})
  await vm.confirm();assert.ok(vm.pending.value);vm.rows.value[0].currentQuantity='200';await vm.confirm()
  assert.equal(calls.length,2);assert.deepEqual(calls[0],calls[1]);assert.equal(vm.pending.value,undefined)
})
test('invalid import data cannot reach confirm API',async()=>{
  let count=0;const vm=importDialog(async()=>{count++});vm.rows.value[0].currentQuantity='';await vm.confirm();assert.equal(count,0)
})
test('concurrent import clicks issue a single request',async()=>{
  let release;let count=0;const vm=importDialog(()=>{count++;return new Promise(r=>{release=r})})
  const first=vm.confirm();await vm.confirm();assert.equal(count,1);release({data:{data:{created:1,skipped:0}}});await first
})
test('lightweight navigation includes the requested stock warnings page',()=>{
  const source=readFileSync(new URL('../src/router/index.ts',import.meta.url),'utf8')
  for(const route of ['dashboard','analytics','stocktakes','products','stock-warnings','audit-logs'])assert.ok(source.includes(`path: '${route}'`))
  for(const route of ['excel-import','warehouses','receipts','issues','transfers','inventory-alerts','base-data'])assert.ok(!source.includes(`path: '${route}'`))
})
test('quantity validation accepts explicit zero only for stocktake',()=>{
  assert.equal(utils.validQuantity(0),false);assert.equal(utils.validQuantity(0,true),true)
  for(const invalid of [undefined,null,-1,NaN,Infinity,0.0000001])assert.equal(utils.validQuantity(invalid),false)
  assert.equal(utils.validQuantity(1.123456),false)
  assert.equal(utils.validQuantity(1.5),false);assert.equal(utils.validQuantity(2147483648),false);assert.equal(utils.validQuantity(2147483647),true)
})
test('product label includes both specifications and unit',()=>{
  assert.equal(utils.productLabel({name:'杯',productSpecification:'2.3',rawMaterialSpecification:'铝',unit:'箱'}),'杯 · 2.3 · 铝 · 箱')
})
test('confirm sends one immediate posted movement',async()=>{
  const calls=[];const vm=dashboard(async(url,payload)=>{calls.push({url,payload});return{data:{data:{quantityAfter:3,alreadyPosted:false}}}})
  await vm.submit();assert.equal(calls.length,1);assert.equal(calls[0].url,'/api/v1/movements');assert.equal(calls[0].payload.quantity,3)
  assert.equal(vm.quantity.value,undefined);assert.equal(vm.pending.value,undefined)
})
test('double submit while busy sends only once',async()=>{
  let release;let calls=0;const vm=dashboard(()=>{calls++;return new Promise(resolve=>{release=resolve})})
  const first=vm.submit();await vm.submit();assert.equal(calls,1)
  release({data:{data:{quantityAfter:3}}});await first
})
test('uncertain network response retries exact same request id',async()=>{
  const calls=[];const vm=dashboard(async(_url,payload)=>{calls.push({...payload});if(calls.length===1)throw new Error('network');return{data:{data:{quantityAfter:3,alreadyPosted:true}}}})
  await vm.submit();assert.ok(vm.pending.value);await vm.submit()
  assert.equal(calls.length,2);assert.equal(calls[0].requestId,calls[1].requestId);assert.equal(vm.pending.value,undefined)
})
test('invalid quantity never sends a request',async()=>{
  let calls=0;const vm=dashboard(async()=>{calls++});vm.quantity.value=-1;await vm.submit();assert.equal(calls,0)
})
test('Enter and confirm share one native form submit handler',()=>{
  const source=readFileSync(new URL('../src/views/DashboardView.vue',import.meta.url),'utf8')
  assert.ok(source.includes('@submit.prevent="submit"'));assert.ok(source.includes('native-type="submit"'));assert.ok(!source.includes('@keyup.enter'))
})
test('product notes are included in searchable selection labels',()=>{
  assert.ok(utils.productLabel({...importRow(),note:'鼎胜料'}).includes('鼎胜料'))
  assert.ok(!utils.productLabel({...importRow(),note:'鼎胜料'}).includes('备注：'))
})

test('product fuzzy search combines words and normalizes fullwidth punctuation',()=>{
  const p={...importRow(),name:'84/125（100*20）蓝色不热封',note:'鼎胜料'}
  for(const q of ['蓝色 100×20','鼎胜 2.3','８４／１２５','']) assert.equal(utils.matchesProduct(p,q),true)
  assert.equal(utils.matchesProduct(p,'粉色'),false)
})

test('workbench options and selected product are single-line without note prefixes',()=>{
  const source=readFileSync(new URL('../src/views/DashboardView.vue',import.meta.url),'utf8')
  assert.ok(source.includes(':filter-method="filterProducts"'))
  assert.ok(!source.includes('备注：'));assert.ok(!source.includes('选商品、填数量，确认或按回车即可完成出入库。'))
  assert.ok(source.includes('<b class="product-unit">{{p.unit}}</b>'));assert.ok(source.includes('selected-identity'))
  assert.ok(source.includes('<el-button type="primary" @click="navigate">设置预警值'))
  assert.ok(!source.includes('formatQuantity(p.quantity)}} {{p.unit}}'))
})
test('stock warning is strictly below threshold, excludes disabled and unset rules',()=>{
  const p={isActive:true,quantity:5,warningQuantity:5}
  assert.equal(utils.isLowStock(p),false);assert.equal(utils.isLowStock({...p,quantity:4}),true)
  assert.equal(utils.isLowStock({...p,quantity:0,warningQuantity:0}),false)
  assert.equal(utils.isLowStock({...p,quantity:0,isActive:false}),false)
  assert.equal(utils.isLowStock({...p,warningQuantity:null}),false)
})
test('product colors are deterministic and black-gold takes precedence over gold',()=>{
  assert.equal(colors.productTone('黑金色不热封').label,'黑金');assert.equal(colors.productTone('金色热封').label,'金色')
  assert.notEqual(colors.productTone('蓝色').tint,colors.productTone('粉色').tint)
  assert.equal(colors.productRowStyle({row:{name:'蓝色'}}).backgroundColor,colors.productRowStyle({row:{row:{name:'蓝色'}}}).backgroundColor)
  assert.equal(colors.productRowStyle({row:{productName:'蓝色'}}).backgroundColor,colors.productStyle('蓝色').backgroundColor)
})
test('dashboard removes old overview and ledger, and shows selected stock plus warnings',()=>{
  const source=readFileSync(new URL('../src/views/DashboardView.vue',import.meta.url),'utf8')
  for(const removed of ['每种商品的当前库存','最近出入库与盘库流水','确认后即时过账，无需审批'])assert.ok(!source.includes(removed))
  assert.ok(source.includes('selected.quantity'));assert.ok(source.includes('库存告急，请及时处理'));assert.ok(source.includes('entry-submit'))
})
test('audit log UI does not expose raw details or trace identifiers',()=>{
  const source=readFileSync(new URL('../src/views/AuditLogsView.vue',import.meta.url),'utf8')
  for(const removed of ['traceId','s.row.detail','type="expand"'])assert.ok(!source.includes(removed))
})
function analyticsVm(post=async()=>({data:{data:{rows:[],trend:[]}}})) {
  const {descriptor}=parse(readFileSync(new URL('../src/views/DataDashboardView.vue',import.meta.url),'utf8'))
  const component=evaluate(compileScript(descriptor,{id:'analytics-test'}).content,{
    vue:{...vue,onMounted(){}},'element-plus':{ElMessage:{error(){}}},'@/api/http':{api:{post},getErrorMessage:String},
    '@/components/PageHeader.vue':{},'@/components/InventoryChart.vue':{},'@/utils/inventory':utils,'@/utils/productColors':colors,
  }).default
  return component.setup({}, {expose(){}})
}
test('analytics switches to operation counts when units differ',()=>{
  const vm=analyticsVm()
  const a={...importRow(),id:'a',inbound:4,outbound:2,netChange:2,currentQuantity:12,inboundCount:1,outboundCount:1,isLowStock:false,isActive:true}
  vm.data.value={rows:[a,{...a,id:'b',unit:'个'}],trend:[]}
  assert.equal(vm.oneUnit.value,false);assert.equal(vm.metrics.value[2].unit,'次');assert.equal(vm.metrics.value[2].value,2)
  vm.data.value={rows:[a],trend:[]};assert.equal(vm.oneUnit.value,true);assert.equal(vm.metrics.value[0].value,12);assert.equal(vm.metrics.value[2].value,2)
})

test('analytics submits all selected products, and clearing selection queries all',async()=>{
  const calls=[];const vm=analyticsVm(async(url,payload)=>{calls.push({url,payload});return{data:{data:{rows:[],trend:[]}}}})
  vm.productIds.value=['a','b'];await vm.load();assert.deepEqual(Array.from(calls[0].payload.productIds),['a','b']);assert.equal(calls[0].url,'/api/v1/analytics/query');assert.equal(calls[0].payload.unit,undefined)
  vm.productIds.value=[];await vm.load();assert.equal(calls[1].payload.productIds.length,0)
  const source=readFileSync(new URL('../src/views/DataDashboardView.vue',import.meta.url),'utf8');assert.ok(!source.includes('aria-label="单位筛选"'));assert.ok(source.includes('v-model="productIds" multiple'))
})

function warningVm(put,confirm=async()=>{}) {
  const {descriptor}=parse(readFileSync(new URL('../src/views/StockWarningsView.vue',import.meta.url),'utf8'))
  const component=evaluate(compileScript(descriptor,{id:'warning-test'}).content,{
    vue:{...vue,onMounted(){}},'element-plus':{ElMessage:{success(){},warning(){},error(){}},ElMessageBox:{confirm}},'@/api/http':{api:{put},getErrorMessage:String},
    '@/components/PageHeader.vue':{},'@/utils/inventory':utils,'@/utils/productColors':colors,
  }).default
  const vm=component.setup({}, {expose(){}})
  const a={...importRow(),id:'a',quantity:20,version:2,isActive:true,warningQuantity:null}
  vm.products.value=[a,{...a,id:'b'},{...a,id:'untouched'}];vm.selectProducts(vm.products.value.slice(0,2));vm.batchQuantity.value=50
  return vm
}
test('all product surfaces share the product typography rules',()=>{
  for(const file of ['views/ProductsView.vue','views/DashboardView.vue','views/StockWarningsView.vue','views/StocktakesView.vue','views/DataDashboardView.vue','components/ProductImportDialog.vue']) {
    const source=readFileSync(new URL('../src/'+file,import.meta.url),'utf8')
    for(const style of ['product-name','product-spec','product-unit'])assert.ok(source.includes(style),file+' missing '+style)
  }
  const css=readFileSync(new URL('../src/styles/product-typography.css',import.meta.url),'utf8')
  assert.ok(css.includes('--product-text-size:16px'));assert.ok(css.includes('--product-unit-size:17px'))
})
test('batch warnings submit one selected set and preserve unselected drafts',async()=>{
  const calls=[];const vm=warningVm(async(url,payload)=>{calls.push({url,payload});return{data:{data:vm.products.value.slice(0,2).map(p=>({...p,warningQuantity:50,version:3}))}}})
  vm.drafts.untouched={enabled:true,quantity:77};await vm.saveBatch()
  assert.equal(calls.length,1);assert.equal(calls[0].url,'/api/v1/products/warnings/batch');assert.equal(calls[0].payload.products.length,2);assert.equal(calls[0].payload.warningQuantity,50)
  assert.equal(vm.selection.value.length,0);assert.equal(vm.drafts.untouched.quantity,77);assert.equal(vm.products.value[2].warningQuantity,null)
})
test('batch warnings guard empty and invalid values and allow cancellation',async()=>{
  let calls=0;const vm=warningVm(async()=>{calls++},async()=>{throw Error('cancel')})
  await vm.saveBatch();assert.equal(calls,0);assert.equal(vm.saving.value,'')
  vm.batchQuantity.value=1.5;await vm.saveBatch();assert.equal(calls,0)
  vm.selection.value=[];vm.batchQuantity.value=1;await vm.saveBatch();assert.equal(calls,0)
})
test('batch warnings freeze duplicate submits and clear rules with null',async()=>{
  let release,calls=0;const vm=warningVm(async(_url,payload)=>{calls++;assert.equal(payload.warningQuantity,null);return new Promise(r=>{release=r})})
  vm.batchEnabled.value=false;const first=vm.saveBatch();await Promise.resolve();await vm.saveBatch();assert.equal(calls,1)
  release({data:{data:[]}});await first;assert.equal(vm.saving.value,'')
})
