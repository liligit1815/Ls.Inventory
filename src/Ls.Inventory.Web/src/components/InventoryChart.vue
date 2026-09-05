<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref, watch, nextTick } from 'vue'
import { init, use, type EChartsCoreOption, type ECharts } from 'echarts/core'
import { LineChart, PieChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, LegendComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
use([LineChart, PieChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer])
const props = defineProps<{ option: EChartsCoreOption; height?: number }>()
const element = ref<HTMLElement>()
let chart: ECharts | undefined, observer: ResizeObserver | undefined
onMounted(() => { if (!element.value) return; chart = init(element.value); chart.setOption(props.option); observer = new ResizeObserver(() => chart?.resize()); observer.observe(element.value) })
watch(() => props.option, value => chart?.setOption(value, true), { deep: true })
watch(() => props.height, async () => { await nextTick(); chart?.resize() })
onBeforeUnmount(() => { observer?.disconnect(); chart?.dispose() })
</script>
<template><div ref="element" class="chart" :style="{ height: `${height ?? 340}px` }" role="img" aria-label="库存统计图表，下方提供对应数据表" /></template>
