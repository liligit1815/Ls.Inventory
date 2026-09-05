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
