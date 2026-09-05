import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
const router = createRouter({ history: createWebHistory(), routes: [
  { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue'), meta: { public: true, title: '登录' } },
  { path: '/', component: () => import('@/layouts/AppLayout.vue'), children: [
    { path: '', redirect: '/dashboard' },
    { path: 'dashboard', name: 'dashboard', component: () => import('@/views/DashboardView.vue'), meta: { title: '工作台' } },
    { path: 'analytics', component: () => import('@/views/DataDashboardView.vue'), meta: { title: '数据看板' } },
    { path: 'stocktakes', component: () => import('@/views/StocktakesView.vue'), meta: { title: '盘库' } },
    { path: 'products', component: () => import('@/views/ProductsView.vue'), meta: { title: '商品档案' } },
    { path: 'stock-warnings', component: () => import('@/views/StockWarningsView.vue'), meta: { title: '库存预警' } },
    { path: 'audit-logs', component: () => import('@/views/AuditLogsView.vue'), meta: { title: '操作日志' } },
  ] },
  { path: '/:pathMatch(.*)*', redirect: '/dashboard' },
] })
router.beforeEach(async to => {
  const auth = useAuthStore()
  if (!auth.initialized) await auth.loadCurrentUser()
  if (!to.meta.public && !auth.isAuthenticated) return { name: 'login', query: { redirect: to.fullPath } }
  if (to.name === 'login' && auth.isAuthenticated) return { name: 'dashboard' }
  document.title = `${String(to.meta.title ?? '库存')} · LS 轻量版`
})
export default router
