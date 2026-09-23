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
function dashboard(post,confirm=async()=>{}){
  const source=readFileSync(new URL('../src/views/DashboardView.vue',import.meta.url),'utf8')
  const {descriptor}=parse(source)
  const compiled=compileScript(descriptor,{id:'lite-dashboard-test'})
  const component=evaluate(compiled.content,{
    vue:{...vue,onMounted(){},onBeforeUnmount(){}},'element-plus':{ElMessage:{success(){},warning(){},error(){}},ElMessageBox:{confirm}},
    '@/api/http':{api:{post,get:async url=>({data:{data:url.includes('products')?[]:{items:[]}}})},getErrorMessage:e=>String(e)},
    '@/components/PageHeader.vue':{},'@/components/InventoryChart.vue':{},'@/components/RecentMovementsOverview.vue':{},'@/utils/inventory':utils,'@/utils/productColors':colors,
  }).default
  const vm=component.setup({}, {expose(){}})
  vm.products.value=[{...importRow(),id:'product',quantity:20,isActive:true}];vm.productId.value='product';vm.quantity.value=3
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
  assert.equal(vm.historyRefresh.value,1)
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

test('workbench picker keeps compact labels and drawer shows complete product identity',()=>{
  const source=readFileSync(new URL('../src/views/DashboardView.vue',import.meta.url),'utf8')
  assert.ok(source.includes(':filter-method="filterProducts"'))
  assert.ok(!source.includes('备注：'));assert.ok(!source.includes('选商品、填数量，确认或按回车即可完成出入库。'))
  assert.ok(source.includes('<b class="product-unit">{{p.unit}}</b>'));assert.ok(source.includes('selected-stock'))
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

const overview=evaluate(readFileSync(new URL('../src/utils/movementOverview.ts',import.meta.url),'utf8'))
function recentHistoryVm(get, overrides={}) {
  const {descriptor}=parse(readFileSync(new URL('../src/components/RecentMovementsOverview.vue',import.meta.url),'utf8'))
  const component=evaluate(compileScript(descriptor,{id:'recent-history-test'}).content,{
    vue:{...vue,onBeforeUnmount(){}},'@/api/http':{api:{get},getErrorMessage:String},'@/utils/inventory':utils,'@/utils/productColors':colors,'@/utils/movementOverview':overview,'@/components/MovementQuickActions.vue':{},
  }).default
  const props=vue.reactive({products:[],refreshKey:0,...overrides})
  const scope=vue.effectScope()
  const vm=scope.run(()=>component.setup(props,{expose(){},emit:(...args)=>overrides.onEntry?.(...args)}))
  return {vm,props,stop:()=>scope.stop()}
}
const historyResponse=(overrides={})=>({data:{data:{from:'2026-09-14',to:'2026-09-20',days:7,entries:[],...overrides}}})
async function flushHistory(){await vue.nextTick();await new Promise(resolve=>setImmediate(resolve))}

test('overview defaults to 7 days and preserves display choices on refresh',async t=>{
  const calls=[]
  const {vm,props,stop}=recentHistoryVm(async(url,config)=>{calls.push({url,...config.params});return historyResponse()})
  t.after(stop);await flushHistory()
  assert.equal(calls[0].url,'/api/v1/movements/daily');assert.equal(calls[0].days,7);assert.equal(vm.view.value,'calendar')
  vm.view.value='table';vm.selectedIds.value=['a'];await flushHistory();assert.equal(calls.length,1)
  vm.days.value=30;await flushHistory();assert.equal(calls.at(-1).days,30)
  props.refreshKey++;await flushHistory();assert.equal(calls.length,3);assert.equal(vm.view.value,'table');assert.deepEqual(Array.from(vm.selectedIds.value),['a'])
  vm.view.value='cards';props.refreshKey++;await flushHistory();assert.equal(vm.view.value,'cards');assert.equal(calls.length,4)
})

test('overview ignores late results and errors from previous ranges',async t=>{
  const requests=[]
  const {vm,props,stop}=recentHistoryVm(()=>new Promise((resolve,reject)=>requests.push({resolve,reject})))
  t.after(stop);vm.days.value=30;await vue.nextTick()
  requests[1].resolve(historyResponse({days:30}));await flushHistory();assert.equal(vm.data.value.days,30)
  requests[0].resolve(historyResponse());await flushHistory();assert.equal(vm.data.value.days,30)
  vm.days.value=14;await vue.nextTick();assert.equal(vm.data.value,undefined)
  vm.days.value=1;await vue.nextTick();requests[2].reject(Error('old request failed'));await flushHistory()
  assert.equal(vm.error.value,'');assert.equal(vm.loading.value,true)
  requests[3].resolve(historyResponse({days:1}));await flushHistory();assert.equal(vm.data.value.days,1);assert.equal(vm.loading.value,false)
})

test('overview clears stale data on failures and supports retry',async t=>{
  let fail=false
  const {vm,stop}=recentHistoryVm(async()=>{if(fail)throw Error('network');return historyResponse()})
  t.after(stop);await flushHistory();assert.equal(vm.data.value.days,7)
  fail=true;await vm.loadHistory();assert.equal(vm.data.value,undefined);assert.ok(vm.error.value.includes('network'));assert.equal(vm.loading.value,false)
  fail=false;await vm.loadHistory();assert.equal(vm.data.value.days,7);assert.equal(vm.error.value,'')
})

test('overview rejects empty, fractional and out-of-range days without sending queries',async t=>{
  let calls=0
  const {vm,stop}=recentHistoryVm(async()=>{calls++;return historyResponse()})
  t.after(stop);await flushHistory();assert.equal(calls,1)
  for(const invalid of [undefined,0,-1,1.5,368,NaN]) {
    vm.days.value=invalid;await flushHistory();assert.equal(calls,1);assert.equal(vm.data.value,undefined);assert.ok(vm.error.value.includes('1～367'))
  }
  for(const valid of [1,367]){vm.days.value=valid;await flushHistory();assert.equal(vm.error.value,'')}
  assert.equal(calls,3)
})

test('calendar dates include empty days, cross months and years, and align Monday to Sunday',()=>{
  assert.deepEqual(Array.from(overview.calendarDates('2025-12-30','2026-01-02')),['2025-12-30','2025-12-31','2026-01-01','2026-01-02'])
  assert.equal(overview.calendarPadding('2026-09-14'),0);assert.equal(overview.calendarPadding('2026-09-20'),6)
  assert.equal(overview.calendarDates('2024-02-28','2024-03-01').length,3)
})

test('calendar and matrix share product-date values and keep mixed units separate',async t=>{
  const products=[{...importRow(),id:'a',name:'蓝色杯',isActive:true},{...importRow(),id:'b',name:'蓝色杯',unit:'个',isActive:false},{...importRow(),id:'c',name:'无记录商品',isActive:true}]
  const entries=[{date:'2026-09-20',productId:'a',inbound:100,outbound:30,inboundCount:2,outboundCount:1},{date:'2026-09-20',productId:'b',inbound:50,outbound:0,inboundCount:1,outboundCount:0}]
  const {vm,stop}=recentHistoryVm(async()=>historyResponse({entries}),{products});t.after(stop);await flushHistory()
  assert.equal(vm.shownProducts.value.length,2);assert.equal(vm.dates.value.length,7);assert.equal(vm.calendarDays.value[0].items.length,0)
  assert.equal(vm.index.value.get('2026-09-20').get('a').inbound,100);assert.equal(vm.calendarDays.value[6].items.length,2)
  assert.equal(vm.counts.value.inbound,3);assert.equal(vm.counts.value.outbound,1)
  assert.deepEqual(Array.from(vm.summaryDays.value,day=>day.date),Array.from(vm.dates.value));assert.equal(vm.summaryDays.value[6].date,'2026-09-20');assert.equal(vm.summaryDays.value[6].summary.units.length,2)
  assert.equal(vm.summaryDays.value[0].items.length,0);assert.equal(vm.summaryDays.value[0].summary.inboundCount,0)
  vm.selectedIds.value=['b'];assert.equal(vm.shownProducts.value[0].unit,'个');assert.equal(vm.calendarDays.value[6].items[0].movement.inbound,50)
  assert.equal(vm.summaryDays.value[6].summary.units.length,1);assert.equal(vm.summaryDays.value[6].summary.units[0].unit,'个')
  vm.showDate('2026-09-20');assert.equal(vm.details.value.length,1);assert.equal(vm.detailDate.value,'2026-09-20')
  vm.selectedIds.value=[];vm.onlyWithActivity.value=false;assert.equal(vm.shownProducts.value.length,3)
  vm.selectedIds.value=['c'];assert.equal(vm.index.value.get('2026-09-20').get('c'),undefined);assert.equal(vm.calendarDays.value[6].items.length,0)
})

test('daily cards total each unit separately and retain negative and zero net changes',()=>{
  const item=(unit,inbound,outbound)=>({product:{unit},movement:{date:'2026-09-20',productId:unit,inbound,outbound,inboundCount:1,outboundCount:1}})
  const result=overview.summarizeDay([item('箱',100,30),item('箱',80,100),item('个',2,9),item('件',7,7)])
  assert.equal(result.units.length,3)
  const boxes=result.units.find(x=>x.unit==='箱');assert.equal(boxes.inbound,180);assert.equal(boxes.outbound,130);assert.equal(boxes.netChange,50)
  assert.equal(result.units.find(x=>x.unit==='个').netChange,-7);assert.equal(result.units.find(x=>x.unit==='件').netChange,0)
  assert.equal(result.inboundCount,4);assert.equal(result.outboundCount,4)
  const empty=overview.summarizeDay([]);assert.equal(empty.units.length,0);assert.equal(empty.inboundCount,0);assert.equal(empty.outboundCount,0)
})


test('entry opened from history carries only product and direction, never historical quantity',()=>{
  const vm=dashboard(async()=>{})
  vm.quantity.value=7;vm.note.value='old note'
  vm.openEntry('Outbound','product')
  assert.equal(vm.entryOpen.value,true);assert.equal(vm.productId.value,'product');assert.equal(vm.kind.value,'Outbound')
  assert.equal(vm.quantity.value,undefined);assert.equal(vm.note.value,'')
  vm.quantity.value=5;assert.equal(vm.projectedQuantity.value,15)
  vm.openEntry('Inbound');assert.equal(vm.productId.value,'');assert.equal(vm.projectedQuantity.value,undefined)
})

test('entry guards negative stock, overflow, disabled and missing products before posting',async()=>{
  let calls=0;const vm=dashboard(async()=>{calls++})
  vm.kind.value='Outbound';vm.quantity.value=21;assert.equal(vm.projectedQuantity.value,-1);await vm.submit();assert.equal(calls,0)
  vm.kind.value='Inbound';vm.quantity.value=2147483647;await vm.submit();assert.equal(calls,0)
  vm.quantity.value=1;vm.products.value[0].isActive=false;await vm.submit();assert.equal(calls,0)
  vm.products.value=[];await vm.submit();assert.equal(calls,0)
})

test('uncertain entry cannot be replaced or closed and retry uses its original payload',async()=>{
  const calls=[];const vm=dashboard(async(_url,payload)=>{calls.push({...payload});if(calls.length===1)throw Error('network');return{data:{data:{quantityAfter:23,alreadyPosted:true}}}})
  vm.openEntry('Inbound','product');vm.quantity.value=3;vm.note.value='补货';await vm.submit()
  vm.openEntry('Outbound','other');let closed=false;await vm.closeEntry(()=>{closed=true})
  assert.equal(closed,false);assert.equal(vm.productId.value,'product');assert.equal(vm.kind.value,'Inbound');assert.equal(vm.note.value,'补货')
  await vm.submit();assert.deepEqual(calls[0],calls[1]);assert.equal(vm.receipt.value.quantityAfter,23);assert.equal(vm.receipt.value.quantity,3)
})

test('history entry uses the single filter as a default and explicit product takes precedence',async t=>{
  const events=[];const {vm,props,stop}=recentHistoryVm(async()=>historyResponse(),{onEntry:(...args)=>events.push(args)})
  t.after(stop);await flushHistory()
  vm.selectedIds.value=['a'];vm.productDetailOpen.value=true;vm.startEntry('Inbound')
  assert.deepEqual(events[0],['entry','Inbound','a']);assert.equal(vm.productDetailOpen.value,false)
  vm.startEntry('Outbound','b');assert.deepEqual(events[1],['entry','Outbound','b'])
  vm.selectedIds.value=['a','b'];vm.startEntry('Inbound');assert.equal(events[2][2],undefined)
  props.entryLocked=true;vm.startEntry('Inbound');assert.equal(events.length,3)
})

test('viewing the posted receipt reveals today even when another product was filtered',async t=>{
  const {vm,stop}=recentHistoryVm(async()=>historyResponse())
  t.after(stop);await flushHistory()
  vm.view.value='cards';vm.selectedIds.value=['old'];vm.days.value=30
  vm.focusToday('posted');assert.equal(vm.days.value,1);assert.deepEqual(Array.from(vm.selectedIds.value),['posted'])
  assert.equal(vm.onlyWithActivity.value,false);assert.equal(vm.view.value,'cards')
})


test('closing a draft preserves input when cancelled and clears only when confirmed',async()=>{
  let discard=false,closed=0
  const vm=dashboard(async()=>{},async()=>{if(!discard)throw Error('cancel')})
  vm.note.value='本次补货';await vm.closeEntry(()=>closed++)
  assert.equal(closed,0);assert.equal(vm.quantity.value,3);assert.equal(vm.note.value,'本次补货')
  discard=true;await vm.closeEntry(()=>closed++)
  assert.equal(closed,1);assert.equal(vm.quantity.value,undefined);assert.equal(vm.note.value,'')
})

test('background refresh retains the rendered range until replacement arrives',async t=>{
  let release,calls=0
  const {vm,stop}=recentHistoryVm(async()=>{if(++calls===1)return historyResponse();return new Promise(r=>{release=r})})
  t.after(stop);await flushHistory();const original=vm.data.value
  const refresh=vm.loadHistory();assert.equal(vm.data.value,original);assert.equal(vm.loading.value,true)
  release(historyResponse({entries:[{date:'2026-09-20',productId:'a',inbound:1,outbound:0,inboundCount:1,outboundCount:0}]}));await refresh
  assert.equal(vm.data.value.entries.length,1);assert.equal(vm.loading.value,false)
})


test('date overview defaults to the latest day, selects empty dates and preserves date across refresh',async t=>{
  const {vm,props,stop}=recentHistoryVm(async()=>historyResponse())
  t.after(stop);await flushHistory();assert.equal(vm.detailDate.value,'2026-09-20')
  vm.showDate('2026-09-18');assert.equal(vm.details.value.length,0)
  props.refreshKey++;await flushHistory();assert.equal(vm.detailDate.value,'2026-09-18')
  vm.view.value='cards';await flushHistory();assert.equal(vm.detailDate.value,'2026-09-18')
  vm.days.value=1;await flushHistory()
})

test('day details paginate independently and filter totals cover the full selected day',async t=>{
  const products=Array.from({length:19},(_,i)=>({...importRow(),id:'p'+i,name:'商品'+String(i).padStart(2,'0'),unit:i%2?'个':'箱',isActive:true}))
  const entries=products.map(p=>({date:'2026-09-20',productId:p.id,inbound:10,outbound:2,inboundCount:1,outboundCount:1}))
  const {vm,stop}=recentHistoryVm(async()=>historyResponse({entries}),{products})
  t.after(stop);await flushHistory()
  assert.equal(vm.details.value.length,19);assert.equal(vm.pagedDetails.value.length,8);assert.equal(vm.detailSummary.value.inboundCount,19)
  assert.equal(vm.detailSummary.value.units.find(u=>u.unit==='箱').inbound,100)
  vm.detailPage.value=3;assert.equal(vm.pagedDetails.value.length,3)
  vm.selectedIds.value=['p0'];await flushHistory();assert.equal(vm.detailPage.value,1);assert.equal(vm.pagedDetails.value[0].product.id,'p0')
  vm.selectedIds.value=[];await flushHistory();vm.detailPage.value=3
  vm.showDate('2026-09-19');assert.equal(vm.detailPage.value,1);assert.equal(vm.pagedDetails.value.length,0)
})

test('date selection falls back when the range excludes it and receipt selects today',async t=>{
  let narrow=false
  const {vm,props,stop}=recentHistoryVm(async()=>historyResponse(narrow?{from:'2026-09-20',to:'2026-09-20',days:1}:{}))
  t.after(stop);await flushHistory();vm.showDate('2026-09-14')
  narrow=true;vm.days.value=1;await flushHistory();assert.equal(vm.detailDate.value,'2026-09-20')
  vm.detailPage.value=2;vm.focusToday('product');assert.equal(vm.detailDate.value,utils.localDate());assert.equal(vm.detailPage.value,1)
})

test('full product identity is available on click and entry retains selected date',async t=>{
  const product={...importRow(),id:'a',name:'很长的商品名称',note:'完整的商品备注',quantity:25,isActive:true}
  const events=[];const {vm,stop}=recentHistoryVm(async()=>historyResponse(),{products:[product],onEntry:(...args)=>events.push(args)})
  t.after(stop);await flushHistory();vm.showDate('2026-09-19');vm.showProduct(product)
  assert.equal(vm.productDetailOpen.value,true);assert.equal(vm.detailProduct.value.note,product.note)
  vm.startEntry('Inbound',product.id);assert.equal(vm.productDetailOpen.value,false);assert.equal(vm.detailDate.value,'2026-09-19')
  assert.deepEqual(events[0],['entry','Inbound','a'])
})
