export interface ApiResponse<T> { data: T; traceId: string }
export interface ApiProblem { title?: string; errorCode?: string; traceId?: string }
export interface CurrentUser { id: string; userName: string; displayName: string; mustChangePassword: boolean }
export interface Product {
  id: string; code: string; name: string; productSpecification: string; rawMaterialSpecification: string;
  unit: string; quantity: number; version: number; note: string; isActive: boolean; warningQuantity: number | null
}
export interface Movement { id: number; productId: string; productName: string; unit: string; kind: string; quantityChange: number; quantityAfter: number; note: string; postedAt: string; userName: string }
export interface Paged<T> { items: T[]; total: number }
export interface RecentMovements extends Paged<Movement> { from: string; to: string; days: number; inbound: number; outbound: number; inboundCount: number; outboundCount: number }
export interface DailyMovement { date: string; productId: string; inbound: number; outbound: number; inboundCount: number; outboundCount: number }
export interface DailyMovements { from: string; to: string; days: number; entries: DailyMovement[] }
export interface AnalyticsRow { id: string; code: string; name: string; productSpecification: string; rawMaterialSpecification: string; unit: string; note: string; warningQuantity: number | null; isLowStock: boolean; inboundCount: number; outboundCount: number; inbound: number; outbound: number; adjustment: number; netChange: number; currentQuantity: number; isActive: boolean }
export interface Analytics { from: string; to: string; rows: AnalyticsRow[]; trend: { date: string; inbound: number; outbound: number; adjustment: number; inboundCount: number; outboundCount: number }[] }
export interface StocktakeSummary { id: string; status: string; createdAt: string; postedAt?: string; lineCount: number }
export interface StocktakeLine { productId: string; code: string; name: string; productSpecification: string; rawMaterialSpecification: string; unit: string; expectedQuantity: number; countedQuantity?: number; currentQuantity: number; hasChanged: boolean }
export interface Stocktake extends StocktakeSummary { lines: StocktakeLine[] }
export interface AuditLog { id: number; occurredAt: string; userName: string; action: string; ipAddress: string }
