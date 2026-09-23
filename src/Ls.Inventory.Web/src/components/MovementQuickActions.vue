<script setup lang="ts">
import type { Product } from '@/types/api'
defineProps<{ product: Product; disabled?: boolean }>()
defineEmits<{ entry: [kind: 'Inbound' | 'Outbound', productId: string] }>()
</script>

<template>
  <div class="movement-actions">
    <template v-if="product.isActive">
      <el-button size="small" plain :disabled="disabled" :aria-label="product.name+'，记入库'" @click="$emit('entry', 'Inbound', product.id)">入库</el-button>
      <el-button size="small" plain :disabled="disabled" :aria-label="product.name+'，记出库'" @click="$emit('entry', 'Outbound', product.id)">出库</el-button>
    </template>
    <span v-else class="muted">已停用</span>
  </div>
</template>

<style scoped>
.movement-actions{display:flex;gap:6px;margin-top:9px;flex-wrap:wrap}.movement-actions .el-button{margin:0;font-size:12px;padding:5px 9px;min-height:28px}.movement-actions .el-button:first-child{color:#247256;border-color:#b3d6c5}.movement-actions .el-button:nth-child(2){color:#955625;border-color:#dfc5b0}.movement-actions>span{font-size:12px}
</style>
