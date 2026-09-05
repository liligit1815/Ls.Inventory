import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus, { ElMessage } from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
import './styles/theme.css'
import './styles/product-typography.css'
import App from './App.vue'
import router from './router'
import { useAuthStore } from './stores/auth'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(router)
app.use(ElementPlus, { locale: zhCn })

let handlingExpiredSession = false
window.addEventListener('ls:session-expired', async () => {
  if (handlingExpiredSession || router.currentRoute.value.name === 'login') return
  handlingExpiredSession = true
  const redirect = router.currentRoute.value.fullPath
  useAuthStore(pinia).clearSession()
  try {
    await router.replace({ name: 'login', query: { redirect } })
    ElMessage.warning('登录状态已失效，请重新登录后继续')
  } finally {
    handlingExpiredSession = false
  }
})

app.mount('#app')
