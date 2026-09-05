import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api, resetCsrfToken } from '@/api/http'
import type { ApiResponse, CurrentUser } from '@/types/api'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<CurrentUser | null>(null)
  const initialized = ref(false)
  const loading = ref(false)
  const isAuthenticated = computed(() => user.value !== null)

  async function loadCurrentUser() {
    try {
      const response = await api.get<ApiResponse<CurrentUser>>('/api/v1/auth/me')
      user.value = response.data.data
    } catch {
      user.value = null
    } finally {
      initialized.value = true
    }
  }

  async function login(userName: string, password: string, rememberMe: boolean) {
    loading.value = true
    try {
      const response = await api.post<ApiResponse<CurrentUser>>('/api/v1/auth/login', {
        userName,
        password,
        rememberMe,
      })
      user.value = response.data.data
      initialized.value = true
      resetCsrfToken()
    } finally {
      loading.value = false
    }
  }

  async function logout() {
    await api.post('/api/v1/auth/logout')
    clearSession()
  }

  function clearSession() {
    user.value = null
    initialized.value = true
    resetCsrfToken()
  }

  return {
    user,
    initialized,
    loading,
    isAuthenticated,
    loadCurrentUser,
    login,
    logout,
    clearSession,
  }
})
