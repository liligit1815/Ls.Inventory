export const PRODUCT_PAGE_SIZE = 30

export const formatQuantity = (n: number) => new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 0 }).format(n)
export const formatDateTime = (s?: string) => s ? new Date(s).toLocaleString('zh-CN', { hour12: false, timeZone: 'Asia/Shanghai' }) : '—'
export const kindLabel = (s: string) => ({ Inbound: '入库', Outbound: '出库', StockGain: '盘盈', StockLoss: '盘亏' }[s] ?? s)
export const statusLabel = (s: string) => ({ Draft: '待盘库', Posted: '已完成', Cancelled: '已取消' }[s] ?? s)
export function localDate(d = new Date()) { return new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Shanghai' }).format(d) }
export function validQuantity(n: unknown, allowZero = false): n is number {
  return typeof n === 'number' && Number.isInteger(n) && n <= 2147483647 && (allowZero ? n >= 0 : n > 0)
}
export function productLabel(p: { name: string; productSpecification: string; rawMaterialSpecification: string; unit: string; note?: string }) {
  return [p.name, p.productSpecification, p.rawMaterialSpecification, p.unit, p.note || ''].filter(Boolean).join(' · ')
}
export function matchesProduct(p: Parameters<typeof productLabel>[0], query: string) {
  const normalize = (value: string) => value.normalize('NFKC').toLocaleLowerCase().replace(/[×＊]/g, '*').replace(/[\s()]/g, '')
  const haystack = normalize(productLabel(p))
  return query.trim().split(/\s+/).every(word => haystack.includes(normalize(word)))
}
export function isLowStock(p: {isActive: boolean; quantity: number; warningQuantity?: number | null}) {
  return p.isActive && p.warningQuantity != null && p.quantity < p.warningQuantity
}

// null selects every category; an empty string selects unclassified products.
export function categoryOptions(products: { category?: string }[]) {
  return [...new Set(products.map(p => p.category || '').filter(Boolean))].sort((a, b) => a.localeCompare(b, 'zh-CN'))
}
export function productsInCategory<T extends { category?: string }>(products: T[], category: string | null) {
  return products.filter(p => category === null || (p.category || '') === category)
}

const productOrder = new Intl.Collator('zh-CN-u-co-pinyin', { numeric: true, sensitivity: 'base' })
type SortableProduct = { category?: string; name: string; productSpecification?: string; rawMaterialSpecification?: string; unit?: string; code?: string }
const sortText = (value = '') => value.normalize('NFKC').replace(/[×＊]/g, '*').replace(/\s+/g, '').trim()
function nameWithoutCategory(product: SortableProduct) {
  const name = sortText(product.name), category = sortText(product.category)
  return category && name.startsWith(category) ? name.slice(category.length) : name
}
// Keep the row objects so editing a sorted stocktake still updates its original counts.
export function sortProducts<T extends SortableProduct>(products: readonly T[]): T[] {
  return [...products].sort((a, b) => {
    const aCategory = sortText(a.category), bCategory = sortText(b.category)
    if (!aCategory !== !bCategory) return aCategory ? -1 : 1
    return productOrder.compare(aCategory, bCategory)
      || productOrder.compare(nameWithoutCategory(a), nameWithoutCategory(b))
      || productOrder.compare(sortText(a.productSpecification), sortText(b.productSpecification))
      || productOrder.compare(sortText(a.rawMaterialSpecification), sortText(b.rawMaterialSpecification))
      || productOrder.compare(sortText(a.unit), sortText(b.unit))
      || productOrder.compare(a.code || '', b.code || '')
  })
}
