<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { House, DataAnalysis, Checked, Goods, Document, Fold, Expand, Bell, UserFilled, ArrowUp, SwitchButton } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import { getErrorMessage } from '@/api/http'
import ChangePasswordDialog from '@/components/ChangePasswordDialog.vue'
const auth = useAuthStore(), router = useRouter(), route = useRoute()
const collapsed = ref(false)
const accountMenuOpen = ref(false)
const menu = [{ path: '/dashboard', title: '工作台', icon: House }, { path: '/analytics', title: '数据看板', icon: DataAnalysis }, { path: '/stocktakes', title: '盘库', icon: Checked }, { path: '/products', title: '商品档案', icon: Goods }, { path: '/stock-warnings', title: '库存预警', icon: Bell }, { path: '/audit-logs', title: '操作日志', icon: Document }]
async function logout() { try { await auth.logout(); await router.replace('/login') } catch(e) { ElMessage.error(getErrorMessage(e)) } }
</script>
<template>
  <div class="app-layout" :class="{ collapsed }">
    <aside><div class="brand"><Goods /><div v-if="!collapsed"><strong>LS 库存</strong><small>轻量版 · 简单记，好管理</small></div></div>
      <nav><RouterLink v-for="item in menu" :key="item.path" :to="item.path" :title="item.title"><el-icon><component :is="item.icon" /></el-icon><span v-if="!collapsed">{{ item.title }}</span></RouterLink></nav>
      <div class="sidebar-footer">
        <div v-if="!collapsed" class="sidebar-note">专注商品与库存<br />每一次变化都有记录</div>
        <el-popover v-model:visible="accountMenuOpen" placement="top-start" trigger="click" :width="192" :offset="10" :show-arrow="false" popper-class="account-popover">
          <template #reference><button class="account-trigger" :class="{ 'is-open': accountMenuOpen }" type="button" aria-label="用户菜单" :aria-expanded="accountMenuOpen" :title="auth.user?.displayName || '用户菜单'">
            <span class="account-avatar"><el-icon><UserFilled /></el-icon></span>
            <span v-if="!collapsed" class="account-label"><strong>{{auth.user?.displayName}}</strong><small>{{auth.user?.userName}}</small></span>
            <el-icon v-if="!collapsed" class="account-arrow"><ArrowUp /></el-icon>
          </button></template>
          <div class="account-summary"><span class="account-avatar"><el-icon><UserFilled /></el-icon></span><div class="account-summary-text"><strong>{{auth.user?.displayName}}</strong><small>{{auth.user?.userName}}</small></div></div>
          <div class="account-divider" />
          <button class="account-logout" type="button" @click="logout"><span class="logout-icon"><el-icon><SwitchButton /></el-icon></span><span>退出登录</span></button>
        </el-popover>
      </div>
    </aside>
    <section class="workspace"><header class="workspace-header"><el-button class="menu-toggle" :icon="collapsed ? Expand : Fold" aria-label="切换菜单" @click="collapsed = !collapsed" /><div id="workspace-page-header"><span v-if="auth.user?.mustChangePassword">{{route.meta.title}}</span></div></header>
      <main><RouterView v-if="!auth.user?.mustChangePassword" /></main>
    </section><ChangePasswordDialog />
  </div>
</template>
<style scoped>
.app-layout{display:grid;grid-template-columns:220px minmax(0,1fr);min-height:100vh}.app-layout.collapsed{grid-template-columns:76px minmax(0,1fr)}aside{position:sticky;top:0;height:100vh;display:flex;flex-direction:column;padding:24px 14px;background:linear-gradient(165deg,#224a61,#17657f);color:#e7f6ff}.brand{display:flex;align-items:center;gap:12px;padding:4px 10px 34px}.brand>svg{width:33px;min-width:33px}.brand strong{font-size:21px}.brand small{display:block;font-size:10px;letter-spacing:1px;margin-top:7px;color:#b0d8eb}nav{display:grid;gap:8px}nav a{display:flex;gap:13px;align-items:center;padding:15px 14px;border-radius:12px;text-decoration:none;color:#c9e6f4;font-size:15px}nav a.router-link-active{background:#ffffff20;color:white;box-shadow:inset 3px 0 #a6e2ff}.sidebar-note{padding:12px 12px 18px;font-size:12px;line-height:1.8;color:#9ecbdc}.workspace{min-width:0}.workspace>header{min-height:90px;position:sticky;top:0;z-index:30;display:flex;align-items:center;gap:18px;padding:14px 28px;border-bottom:1px solid #d7e7f0;background:#f8fcfff2;backdrop-filter:blur(12px);font-weight:600}#workspace-page-header{flex:1;min-width:0}.menu-toggle{flex-shrink:0}.sidebar-footer{margin-top:auto;padding-top:20px}.collapsed .account-trigger{padding:7px 0;justify-content:center}main{padding:28px}@media(max-width:750px){.app-layout{grid-template-columns:76px minmax(0,1fr)}.brand div,nav a span,.sidebar-note,.account-label,.account-arrow{display:none}.account-trigger{padding:7px 0;justify-content:center}main{padding:16px}.workspace>header{padding:14px 16px;align-items:flex-start;gap:12px}.menu-toggle{margin-top:3px}}
.account-trigger {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 10px;
  border: 1px solid #ffffff26;
  border-radius: 14px;
  color: #f1f8fc;
  background: #ffffff0c;
  text-align: left;
  cursor: pointer;
  transition: background .18s ease, border-color .18s ease;
}
.account-trigger:hover, .account-trigger.is-open { background: #ffffff1c; border-color: #ffffff40; }
.account-trigger:focus-visible, .account-logout:focus-visible { outline: 2px solid #91c4e2; outline-offset: 3px; }
.account-avatar { display: flex; align-items: center; justify-content: center; width: 34px; height: 34px; flex-shrink: 0; border-radius: 50%; background: #e0edf5; color: #46677e; font-size: 18px; }
.account-label { display: grid; gap: 3px; min-width: 0; flex: 1; }
.account-label strong { font-size: 14px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.account-label small { color: #bed5e2; font-size: 12px; overflow-wrap: anywhere; }
.account-arrow { font-size: 11px; color: #bed5e2; transition: transform .18s ease; }
.is-open .account-arrow { transform: rotate(180deg); }
.account-summary { display: flex; align-items: center; gap: 11px; padding: 12px 8px 14px; color: #2f475b; }
.account-summary .account-avatar { width: 40px; height: 40px; background: linear-gradient(145deg, #eaf3f9, #dcebf4); box-shadow: inset 0 0 0 1px #d3e4ef; }
.account-summary-text { min-width: 0; }
.account-summary strong { display: block; font-size: 15px; font-weight: 650; line-height: 1.5; overflow-wrap: anywhere; }
.account-summary small { display: block; margin-top: 2px; color: #8293a1; font-size: 12px; line-height: 1.5; overflow-wrap: anywhere; }
.account-divider { height: 1px; margin: 0 8px 6px; background: #eaf0f4; }
.account-logout { display: flex; align-items: center; gap: 10px; width: 100%; min-height: 42px; padding: 8px; border: 0; border-radius: 10px; color: #8e5d5e; background: transparent; font: inherit; font-size: 13px; text-align: left; cursor: pointer; transition: background .18s ease, color .18s ease; }
.logout-icon { display: flex; align-items: center; justify-content: center; width: 28px; height: 28px; border-radius: 8px; background: #faf1f0; color: #ac7373; font-size: 15px; }
.account-logout:hover { color: #a54747; background: #fcf1f0; }
:global(.el-popover.el-popper.account-popover) { padding: 8px; border: 1px solid #e0e9ef; border-radius: 16px; background: #ffffff; box-shadow: 0 12px 36px #17384f24, 0 2px 8px #17384f0a; }
@media (max-width: 750px) { .account-trigger { padding: 7px 0; justify-content: center; } .account-label, .account-arrow { display: none; } }
@media (prefers-reduced-motion: reduce) { .account-trigger, .account-arrow, .account-logout { transition: none; } }
</style>
