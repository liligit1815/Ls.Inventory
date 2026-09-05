import type { Product } from '@/types/api'
export interface ImportRow {
  name: string
  productSpecification: string
  rawMaterialSpecification: string
  unit: string
  currentQuantity: string
  note: string
  sources: string[]
  selected: boolean
  allowEmptySpecifications: boolean
}
type Identity = Pick<ImportRow, 'name' | 'productSpecification' | 'rawMaterialSpecification' | 'unit'>
export function identityKey(row: Identity) {
  return JSON.stringify([row.name, row.productSpecification, row.rawMaterialSpecification, row.unit].map(s => s.trim()))
}
export function importIssue(row: ImportRow) {
  if (!row.name.trim()) return '请补全商品名称'
  if (!row.unit.trim()) return '请补全单位'
  if (row.name.trim().length > 200 || row.productSpecification.trim().length > 100 || row.rawMaterialSpecification.trim().length > 100 || row.unit.trim().length > 30 || row.note.trim().length > 500) return '字段过长，请缩短后导入'
  if ((!row.productSpecification.trim() || !row.rawMaterialSpecification.trim()) && !row.allowEmptySpecifications) return '请补全规格或确认无规格'
  if (!/^(0|[1-9]\d*)(\.0+)?$/.test(row.currentQuantity.trim()) || Number(row.currentQuantity) > 2147483647) return '现有库存必填，须为 0 至 2147483647 的整数'
  return ''
}
export function prepareImport(rows: ImportRow[], existing: Product[]) {
  const known = new Map(existing.map(p => [identityKey(p), p])), seen = new Set<string>()
  return rows.map(row => {
    const key = identityKey(row), product = known.get(key)
    const duplicate = !product && row.selected && seen.has(key)
    if (!product && row.selected) seen.add(key)
    const issue = product ? '' : duplicate ? '同一新商品重复，请只保留一行（库存不相加）' : importIssue(row)
    return { row, product, duplicate, issue,
      difference: product ? 0 : Number(row.currentQuantity),
      status: product ? '已有相同商品，自动跳过（库存及备注不变）' : issue || '新商品，导入备注与现有库存' }
  })
}
