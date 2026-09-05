<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { api,getErrorMessage } from '@/api/http'
import type { ApiResponse,AuditLog,Paged } from '@/types/api'
import PageHeader from '@/components/PageHeader.vue'
import { formatDateTime } from '@/utils/inventory'
const rows=ref<AuditLog[]>([]),total=ref(0),page=ref(1),search=ref(''),busy=ref(false)
async function load(){busy.value=true;try{const r=(await api.get<ApiResponse<Paged<AuditLog>>>('/api/v1/audit-logs',{params:{page:page.value,search:search.value}})).data.data;rows.value=r.items;total.value=r.total}catch(e){ElMessage.error(getErrorMessage(e))}finally{busy.value=false}}
onMounted(load)
</script>
<template><div class="page-shell"><PageHeader title="用户操作日志" description="记录登录、商品维护、出入库及盘库操作；日志仅可查看，不能修改或删除。" />
  <section class="panel" v-loading="busy"><form class="toolbar" @submit.prevent="page=1;load()"><el-input v-model="search" placeholder="搜索操作人或操作" clearable /><el-button type="primary" native-type="submit">查询</el-button></form><el-table :data="rows"><el-table-column label="时间" min-width="190"><template #default="s">{{formatDateTime(s.row.occurredAt)}}</template></el-table-column><el-table-column prop="userName" label="操作人" width="130" /><el-table-column prop="action" label="操作" width="160" /><el-table-column prop="ipAddress" label="来源地址" width="140" /></el-table><el-pagination v-model:current-page="page" :page-size="30" :total="total" layout="total, prev, pager, next" @current-change="load" /></section>
</div></template>
