export interface TableColumn {
  key: string
  label: string
  prop?: string
  width?: number
  minWidth?: number
  className?: string
  fixed?: 'left' | 'right'
  required?: boolean
  hidden?: boolean
}

export interface TablePreferences {
  hidden: string[]
  widths: Record<string, number>
}

export function defaultTablePreferences(columns: readonly TableColumn[]): TablePreferences {
  return { hidden: columns.filter(c => c.hidden && !c.required).map(c => c.key), widths: {} }
}

export function readTablePreferences(columns: readonly TableColumn[], saved: string | null): TablePreferences {
  const defaults = defaultTablePreferences(columns)
  if (!saved) return defaults
  try {
    const value: unknown = JSON.parse(saved)
    if (!value || typeof value !== 'object') return defaults
    const { hidden, widths } = value as { hidden?: unknown; widths?: unknown }
    if (!Array.isArray(hidden) || !hidden.every(key => typeof key === 'string')) return defaults
    const result: TablePreferences = { hidden: columns.filter(c => !c.required && hidden.includes(c.key)).map(c => c.key), widths: {} }
    if (widths && typeof widths === 'object') {
      for (const column of columns) {
        const width = (widths as Record<string, unknown>)[column.key]
        if (typeof width === 'number' && Number.isFinite(width) && width >= (column.minWidth ?? 60) && width <= 2000)
          result.widths[column.key] = Math.round(width)
      }
    }
    if (columns.length && columns.every(c => result.hidden.includes(c.key))) result.hidden = defaults.hidden
    return result
  } catch { return defaults }
}

export function productTableColumns(prefix = ''): TableColumn[] {
  const prop = (key: string) => prefix + key
  return [
    { key: 'code', label: '编码', prop: prop('code'), width: 150, minWidth: 90, hidden: true },
    { key: 'category', label: '商品类目', prop: prop('category'), width: 200, minWidth: 90, hidden: true },
    { key: 'name', label: '商品名称', prop: prop('name'), minWidth: 260, className: 'product-name', required: true },
    { key: 'productSpecification', label: '产品规格', prop: prop('productSpecification'), width: 110, minWidth: 80, className: 'product-spec' },
    { key: 'rawMaterialSpecification', label: '原料规格', prop: prop('rawMaterialSpecification'), width: 135, minWidth: 80, className: 'product-spec' },
    { key: 'note', label: '备注', prop: prop('note'), width: 140, minWidth: 80, className: 'product-note' },
    { key: 'unit', label: '单位', prop: prop('unit'), width: 110, minWidth: 70, className: 'product-unit' },
  ]
}

export function tableCellValue(row: object, path?: string): unknown {
  const value = path?.split('.').reduce<unknown>((current, key) => current && typeof current === 'object' ? (current as Record<string, unknown>)[key] : undefined, row)
  return value == null || value === '' ? '—' : value
}
