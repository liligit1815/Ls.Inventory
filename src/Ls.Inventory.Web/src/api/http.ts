import axios, { type AxiosError, type InternalAxiosRequestConfig } from 'axios'
import type { ApiProblem, ApiResponse } from '@/types/api'

export const api = axios.create({
  baseURL: '/',
  timeout: 20_000,
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
})

let csrfToken = ''
let csrfPromise: Promise<string> | null = null

export function resetCsrfToken() {
  csrfToken = ''
  csrfPromise = null
}

async function fetchCsrfToken(): Promise<string> {
  if (!csrfPromise) {
    csrfPromise = axios
      .get<ApiResponse<{ requestToken: string }>>('/api/v1/auth/csrf', { withCredentials: true })
      .then((response) => {
        csrfToken = response.data.data.requestToken
        return csrfToken
      })
      .finally(() => {
        csrfPromise = null
      })
  }
  return csrfPromise
}

api.interceptors.request.use(async (config: InternalAxiosRequestConfig) => {
  const method = config.method?.toUpperCase() ?? 'GET'
  if (config.data instanceof FormData) config.headers.delete('Content-Type')
  if (!['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(method)) {
    if (!csrfToken) await fetchCsrfToken()
    config.headers.set('X-XSRF-TOKEN', csrfToken)
  }
  return config
})

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ApiProblem>) => {
    if (error.response?.data?.errorCode === 'INVALID_CSRF_TOKEN') {
      resetCsrfToken()
    }
    const requestUrl = error.config?.url ?? ''
    if (error.response?.status === 401 && !requestUrl.includes('/api/v1/auth/login') && !requestUrl.includes('/api/v1/auth/me')) {
      resetCsrfToken()
      window.dispatchEvent(new CustomEvent('ls:session-expired'))
    }
    return Promise.reject(error)
  },
)

export function getErrorMessage(error: unknown): string {
  if (axios.isAxiosError<ApiProblem>(error)) {
    const title = error.response?.data?.title
    if (title) return title
    if (error.response?.status === 401) return '登录状态已失效，请重新登录'
    if (error.response?.status === 403) return '当前账号没有执行此操作的权限'
    if (error.response?.status === 413) return '文件或提交内容过大，请拆分后导入（Excel 不超过 10 MB）'
    if (error.response?.status === 429) return '操作过于频繁，请稍后再试'
    if (error.response) return `请求处理失败（${error.response.status}），请稍后重试`
    if (error.code === 'ECONNABORTED') return '请求超时，请稍后重试'
    return '无法连接系统服务，请确认系统已经启动'
  }
  return error instanceof Error ? error.message : '操作失败，请稍后重试'
}
