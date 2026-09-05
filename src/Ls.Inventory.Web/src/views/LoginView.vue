<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Box, Lock, User } from '@element-plus/icons-vue'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { getErrorMessage } from '@/api/http'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const formRef = ref<FormInstance>()
const form = reactive({ userName: '', password: '', rememberMe: false })
const rules: FormRules = {
  userName: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码', trigger: 'blur' }],
}

async function submit() {
  if (!(await formRef.value?.validate().catch(() => false))) return
  try {
    await auth.login(form.userName, form.password, form.rememberMe)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/dashboard'
    await router.replace(redirect)
  } catch (error) {
    ElMessage.error(getErrorMessage(error))
  }
}
</script>

<template>
  <main class="login-page">
    <section class="brand-panel">
      <div class="crystal crystal-one" />
      <div class="crystal crystal-two" />
      <div class="brand-content">
        <div class="brand-badge"><Box /></div>
        <p class="overline">LS INVENTORY FLOW</p>
        <h1>让每一次出入库<br />都有迹可循</h1>
        <p class="intro">选商品、填数量，轻松完成出入库。库存看得见，盘库更简单。</p>
        <div class="feature-row">
          <span><i />库存实时更新</span>
          <span><i />出入库即时记账</span>
          <span><i />盘库差异有记录</span>
        </div>
      </div>
      <div class="ice-orbit"><span /><span /><span /></div>
    </section>

    <section class="login-panel">
      <div class="login-card">
        <div class="mobile-brand"><Box /><strong>LS 库存云</strong></div>
        <p class="welcome">欢迎回来</p>
        <h2>登录进销存轻量版</h2>
        <p class="tip">使用管理员为你分配的账号登录</p>

        <el-form ref="formRef" :model="form" :rules="rules" label-position="top" @submit.prevent="submit">
          <el-form-item label="用户名" prop="userName">
            <el-input v-model="form.userName" size="large" placeholder="请输入用户名" autocomplete="username" :prefix-icon="User" />
          </el-form-item>
          <el-form-item label="密码" prop="password">
            <el-input v-model="form.password" size="large" type="password" placeholder="请输入密码" autocomplete="current-password" show-password :prefix-icon="Lock" />
          </el-form-item>
          <div class="form-options"><el-checkbox v-model="form.rememberMe">保持登录</el-checkbox><span>忘记密码请联系管理员</span></div>
          <el-button class="login-button" type="primary" size="large" native-type="submit" :loading="auth.loading">安全登录</el-button>
        </el-form>

        <div class="secure-note"><Lock />账号操作与库存变更均会记录安全日志</div>
      </div>
      <footer>LS 进销存 · 轻量版</footer>
    </section>
  </main>
</template>

<style scoped>
.login-page { display: grid; min-height: 100vh; grid-template-columns: minmax(420px, 1.08fr) minmax(440px, 0.92fr); }
.brand-panel { position: relative; display: flex; overflow: hidden; align-items: center; padding: clamp(50px, 8vw, 120px); color: white; background: radial-gradient(circle at 20% 20%, rgba(138, 226, 244, 0.35), transparent 24rem), linear-gradient(145deg, #16485f 0%, #116b8c 58%, #188eb0 100%); }
.brand-panel::after { position: absolute; right: -16%; bottom: -28%; width: 620px; height: 620px; border: 1px solid rgba(215, 249, 255, 0.15); border-radius: 50%; content: ''; box-shadow: 0 0 0 60px rgba(206,246,255,0.035), 0 0 0 130px rgba(206,246,255,0.028); }
.brand-content { position: relative; z-index: 2; max-width: 610px; }
.brand-badge { display: grid; width: 62px; height: 62px; place-items: center; margin-bottom: 28px; border: 1px solid rgba(224, 251, 255, 0.35); border-radius: 19px; background: rgba(226, 249, 255, 0.14); box-shadow: inset 0 1px rgba(255,255,255,.25), 0 20px 45px rgba(4,43,59,.17); font-size: 31px; backdrop-filter: blur(12px); }
.overline { margin: 0 0 13px; color: #99e1f2; font-size: 12px; font-weight: 800; letter-spacing: .2em; }
h1 { margin: 0; font-size: clamp(38px, 4vw, 62px); line-height: 1.19; letter-spacing: -.04em; }
.intro { max-width: 510px; margin: 27px 0 34px; color: #c9edf5; font-size: 16px; line-height: 1.9; }
.feature-row { display: flex; flex-wrap: wrap; gap: 20px; color: #e5faff; font-size: 13px; }
.feature-row span { display: flex; align-items: center; gap: 8px; }
.feature-row i { width: 7px; height: 7px; border-radius: 50%; background: #83e9d0; box-shadow: 0 0 0 4px rgba(131,233,208,.12); }
.crystal { position: absolute; width: 210px; height: 210px; border: 1px solid rgba(216, 249, 255, .11); transform: rotate(45deg); }
.crystal-one { top: -130px; right: 6%; }
.crystal-two { bottom: -155px; left: 4%; width: 270px; height: 270px; }
.ice-orbit { position: absolute; right: 8%; bottom: 11%; z-index: 1; }
.ice-orbit span { position: absolute; display: block; border-radius: 50%; background: rgba(198,245,255,.33); }
.ice-orbit span:nth-child(1) { width: 13px; height: 13px; }
.ice-orbit span:nth-child(2) { width: 7px; height: 7px; transform: translate(50px,-30px); }
.ice-orbit span:nth-child(3) { width: 20px; height: 20px; transform: translate(88px,20px); }
.login-panel { display: flex; min-height: 100vh; flex-direction: column; align-items: center; justify-content: center; padding: 45px; background: radial-gradient(circle at 80% 0%, rgba(114, 210, 235, .17), transparent 26rem), #f6fcff; }
.login-card { width: min(100%, 420px); padding: 42px; border: 1px solid rgba(199, 231, 243, .88); border-radius: 24px; background: rgba(255,255,255,.92); box-shadow: 0 30px 80px rgba(34,104,135,.15); backdrop-filter: blur(20px); }
.welcome { margin: 0 0 6px; color: var(--ice-600); font-size: 13px; font-weight: 800; letter-spacing: .1em; }
h2 { margin: 0; color: var(--ink-900); font-size: 26px; letter-spacing: -.02em; }
.tip { margin: 10px 0 30px; color: var(--ink-500); font-size: 13px; }
.form-options { display: flex; align-items: center; justify-content: space-between; margin: -2px 0 18px; color: var(--ink-500); font-size: 12px; }
.login-button { width: 100%; height: 46px; letter-spacing: .08em; }
.secure-note { display: flex; align-items: center; justify-content: center; margin-top: 24px; color: #7a9aa8; font-size: 11px; gap: 6px; }
.secure-note svg { width: 13px; }
.login-panel footer { margin-top: 25px; color: #91aab5; font-size: 11px; }
.mobile-brand { display: none; }
@media (max-width: 900px) {
  .login-page { grid-template-columns: 1fr; }
  .brand-panel { display: none; }
  .login-panel { padding: 22px; }
  .login-card { padding: 30px 24px; }
  .mobile-brand { display: flex; align-items: center; margin-bottom: 28px; color: var(--ice-700); font-size: 18px; gap: 9px; }
  .mobile-brand svg { width: 27px; }
}
</style>
