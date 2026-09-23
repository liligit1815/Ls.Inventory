import type { DailyMovement, Product } from '@/types/api'

export function calendarDates(from: string, to: string) {
  const dates: string[] = []
  for (let time = Date.parse(from + 'T00:00:00Z'), end = Date.parse(to + 'T00:00:00Z'); time <= end; time += 86400000)
    dates.push(new Date(time).toISOString().slice(0, 10))
  return dates
}
export function calendarPadding(firstDate?: string) {
  return firstDate ? (new Date(firstDate + 'T00:00:00Z').getUTCDay() + 6) % 7 : 0
}
export function dateLabel(date: string) {
  const value = new Date(date + 'T00:00:00Z')
  return `${value.getUTCMonth() + 1}月${value.getUTCDate()}日`
}
export function overviewProducts(products: Product[], entries: DailyMovement[], selectedIds: string[], onlyWithActivity: boolean) {
  const activeIds = new Set(entries.map(x => x.productId)), chosen = new Set(selectedIds)
  return products.filter(p => (!chosen.size || chosen.has(p.id)) && (!onlyWithActivity || activeIds.has(p.id)))
    .sort((a, b) => a.name.localeCompare(b.name, 'zh-CN') || a.productSpecification.localeCompare(b.productSpecification) || a.id.localeCompare(b.id))
}
export function movementIndex(entries: DailyMovement[]) {
  const byDate = new Map<string, Map<string, DailyMovement>>()
  for (const entry of entries) {
    if (!byDate.has(entry.date)) byDate.set(entry.date, new Map())
    byDate.get(entry.date)!.set(entry.productId, entry)
  }
  return byDate
}

export function summarizeDay(items: { product: Pick<Product, 'unit'>; movement: DailyMovement }[]) {
  const units = new Map<string, { unit: string; inbound: number; outbound: number; netChange: number }>()
  let inboundCount = 0, outboundCount = 0
  for (const { product, movement } of items) {
    const total = units.get(product.unit) ?? { unit: product.unit, inbound: 0, outbound: 0, netChange: 0 }
    total.inbound += movement.inbound; total.outbound += movement.outbound
    total.netChange += movement.inbound - movement.outbound
    units.set(product.unit, total)
    inboundCount += movement.inboundCount; outboundCount += movement.outboundCount
  }
  return { units: [...units.values()], inboundCount, outboundCount }
}
