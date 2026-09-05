<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { House, DataAnalysis, Checked, Goods, Document, Fold, Expand, Bell } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import { getErrorMessage } from '@/api/http'
import ChangePasswordDialog from '@/components/ChangePasswordDialog.vue'
const auth = useAuthStore(), router = useRouter(), route = useRoute()
const collapsed = ref(false)
const menu = [{ path: '/dashboard', title: '工作台', icon: House }, { path: '/analytics', title: '数据看板', icon: DataAnalysis }, { path: '/stocktakes', title: '盘库', icon: Checked }, { path: '/products', title: '商品档案', icon: Goods }, { path: '/stock-warnings', title: '库存预警', icon: Bell }, { path: '/audit-logs', title: '操作日志', icon: Document }]
async function logout() { try { await auth.logout(); await router.replace('/login') } catch(e) { ElMessage.error(getErrorMessage(e)) } }
</script>
<template>
  <div class="app-layout" :class="{ collapsed }">
    <aside><div class="brand"><Goods /><div v-if="!collapsed"><strong>LS 库存</strong><small>轻量版 · 简单记，好管理</small></div></div>
      <nav><RouterLink v-for="item in menu" :key="item.path" :to="item.path" :title="item.title"><el-icon><component :is="item.icon" /></el-icon><span v-if="!collapsed">{{ item.title }}</span></RouterLink></nav>
      <div v-if="!collapsed" class="sidebar-note">专注商品与库存<br />每一次变化都有记录</div>
    </aside>
    <section class="workspace"><header><el-button :icon="collapsed ? Expand : Fold" aria-label="切换菜单" @click="collapsed = !collapsed" /><span>{{ route.meta.title }}</span><div class="account"><span>{{ auth.user?.displayName }}</span><el-button @click="logout">退出登录</el-button></div></header>
      <main><RouterView v-if="!auth.user?.mustChangePassword" /></main>
    </section><ChangePasswordDialog />
  </div>
</template>
<style scoped>
.app-layout{display:grid;grid-template-columns:220px minmax(0,1fr);min-height:100vh}.app-layout.collapsed{grid-template-columns:76px minmax(0,1fr)}aside{position:sticky;top:0;height:100vh;display:flex;flex-direction:column;padding:24px 14px;background:linear-gradient(165deg,#224a61,#17657f);color:#e7f6ff}.brand{display:flex;align-items:center;gap:12px;padding:4px 10px 34px}.brand>svg{width:33px;min-width:33px}.brand strong{font-size:21px}.brand small{display:block;font-size:10px;letter-spacing:1px;margin-top:7px;color:#b0d8eb}nav{display:grid;gap:8px}nav a{display:flex;gap:13px;align-items:center;padding:15px 14px;border-radius:12px;text-decoration:none;color:#c9e6f4;font-size:15px}nav a.router-link-active{background:#ffffff20;color:white;box-shadow:inset 3px 0 #a6e2ff}.sidebar-note{margin-top:auto;padding:20px 12px;font-size:12px;line-height:1.8;color:#9ecbdc}.workspace{min-width:0}.workspace>header{height:76px;position:sticky;top:0;z-index:30;display:flex;align-items:center;gap:18px;padding:0 28px;border-bottom:1px solid #d7e7f0;background:#f8fcfff2;backdrop-filter:blur(12px);font-weight:600}.account{margin-left:auto;display:flex;align-items:center;gap:18px;font-size:13px}main{padding:28px}@media(max-width:750px){.app-layout{grid-template-columns:76px minmax(0,1fr)}.brand div,nav a span,.sidebar-note{display:none}main{padding:16px}.workspace>header{padding:0 16px}.account>span{display:none}}
</style>
