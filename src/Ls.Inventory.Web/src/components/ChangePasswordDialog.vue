<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { api, getErrorMessage, resetCsrfToken } from '@/api/http'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const formRef = ref<FormInstance>()
const saving = ref(false)
const form = reactive({ currentPassword: '', newPassword: '', confirmPassword: '' })
const validateConfirm = (_rule: unknown, value: string, callback: (error?: Error) => void) => {
  callback(value === form.newPassword ? undefined : new Error('两次输入的新密码不一致'))
}
const rules: FormRules = {
  currentPassword: [{ required: true, message: '请输入当前密码', trigger: 'blur' }],
  newPassword: [
    { required: true, message: '请输入新密码', trigger: 'blur' },
    { min: 6, max: 128, message: '密码需为 6—128 位，支持纯数字', trigger: 'blur' },
  ],
  confirmPassword: [{ validator: validateConfirm, trigger: 'blur' }],
}

async function submit() {
  if (saving.value) return
  if (!(await formRef.value?.validate().catch(() => false))) return
  saving.value = true
  try {
    await api.post('/api/v1/auth/change-password', {
      currentPassword: form.currentPassword,
      newPassword: form.newPassword,
    })
    resetCsrfToken()
    await auth.loadCurrentUser()
    ElMessage.success('密码已更新')
  } catch (error) {
    ElMessage.error(getErrorMessage(error))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <el-dialog
    :model-value="auth.user?.mustChangePassword ?? false"
    title="首次登录，请设置新密码"
    width="460px"
    :close-on-click-modal="false"
    :close-on-press-escape="false"
    :show-close="false"
  >
    <p class="password-tip">请先修改初始密码。新密码需为 6—128 位，支持纯数字。</p>
    <el-form ref="formRef" :model="form" :rules="rules" label-position="top">
      <el-form-item label="当前密码" prop="currentPassword">
        <el-input v-model="form.currentPassword" type="password" show-password autocomplete="current-password" />
      </el-form-item>
      <el-form-item label="新密码" prop="newPassword">
        <el-input v-model="form.newPassword" type="password" show-password autocomplete="new-password" />
      </el-form-item>
      <el-form-item label="确认新密码" prop="confirmPassword">
        <el-input v-model="form.confirmPassword" type="password" show-password autocomplete="new-password" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button type="primary" :loading="saving" @click="submit">保存新密码并继续</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.password-tip {
  margin: 0 0 20px;
  padding: 13px 15px;
  border-radius: 10px;
  color: #35687d;
  background: #edf9fe;
  font-size: 13px;
  line-height: 1.7;
}
</style>
