<script lang="ts">
	import { onMount } from 'svelte';
	import { createChart, type IChartApi, ColorType, AreaSeries } from 'lightweight-charts';
	import type { ChartDataPoint } from '$lib/api/client';

	let { data, currency }: { data: ChartDataPoint[]; currency: string } = $props();

	let chartContainer: HTMLDivElement;
	let chart: IChartApi | null = null;

	function buildChart() {
		if (!chartContainer) return;
		if (chart) chart.remove();

		chart = createChart(chartContainer, {
			width: chartContainer.clientWidth,
			height: 300,
			attributionLogo: false,
			layout: {
				background: { type: ColorType.Solid, color: '#ffffff' },
				textColor: '#64748b',
				fontFamily: 'system-ui'
			},
			grid: {
				vertLines: { color: '#f1f5f9' },
				horzLines: { color: '#f1f5f9' }
			},
			rightPriceScale: {
				borderColor: '#e2e8f0'
			},
			timeScale: {
				borderColor: '#e2e8f0',
				timeVisible: false
			},
			crosshair: {
				vertLine: { labelBackgroundColor: '#2563eb' },
				horzLine: { labelBackgroundColor: '#2563eb' }
			}
		});

		const areaSeries = chart.addSeries(AreaSeries, {
			lineColor: '#2563eb',
			topColor: 'rgba(37, 99, 235, 0.3)',
			bottomColor: 'rgba(37, 99, 235, 0.02)',
			lineWidth: 2,
			priceFormat: {
				type: 'custom',
				formatter: (price: number) => `${currency} ${price.toFixed(2)}`
			}
		});

		const chartData = data
			.filter((d) => d.value > 0)
			.map((d) => ({
				time: d.date.split('T')[0] as string,
				value: d.value
			}));

		if (chartData.length > 0) {
			areaSeries.setData(chartData as any);
			chart.timeScale().fitContent();
		}
	}

	onMount(() => {
		buildChart();
		const ro = new ResizeObserver(() => {
			if (chart && chartContainer) {
				chart.applyOptions({ width: chartContainer.clientWidth });
			}
		});
		ro.observe(chartContainer);
		return () => {
			ro.disconnect();
			chart?.remove();
		};
	});

	$effect(() => {
		// Rebuild when data changes
		if (data && chartContainer) buildChart();
	});
</script>

<div bind:this={chartContainer} class="w-full"></div>
