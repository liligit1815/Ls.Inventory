// More specific names precede shorter matches (黑金色 must not become 金色).
const tones = [
  { names: ['黑金'], label: '黑金', tint: '#f3f0e6', accent: '#807047' },
  { names: ['金色'], label: '金色', tint: '#fff8e7', accent: '#bd933b' },
  { names: ['蓝色'], label: '蓝色', tint: '#edf5ff', accent: '#548ac0' },
  { names: ['粉色'], label: '粉色', tint: '#fff0f5', accent: '#cc7c9b' },
  { names: ['奶牛'], label: '奶牛纹', tint: '#f1f3f6', accent: '#707c8d' },
  { names: ['红色'], label: '红色', tint: '#fff0ed', accent: '#c9766b' },
  { names: ['绿色'], label: '绿色', tint: '#edf8f0', accent: '#57956c' },
  { names: ['紫色'], label: '紫色', tint: '#f5efff', accent: '#9474bd' },
  { names: ['黄色'], label: '黄色', tint: '#fffbe5', accent: '#b59b34' },
  { names: ['黑色'], label: '黑色', tint: '#edf0f4', accent: '#596778' },
  { names: ['白色', '透明'], label: '白色 / 透明', tint: '#f7fafc', accent: '#7895a5' },
]
export function productTone(name = '') { return tones.find(t => t.names.some(n => name.includes(n))) ?? { label: '默认', tint: '#ffffff', accent: '#539cba' } }
export function productStyle(name = ''): Record<string, string> {
  const t = productTone(name)
  return { '--product-tint': t.tint, '--product-accent': t.accent, '--el-table-tr-bg-color': t.tint,
    '--el-table-row-hover-bg-color': t.tint, backgroundColor: t.tint }
}
type ColorRow = { name?: string; productName?: string; row?: ColorRow }
export function productRowStyle({row}: {row: ColorRow}) { const p = row.row ?? row; return productStyle(p.name ?? p.productName) }
