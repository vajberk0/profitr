<script lang="ts">
	import { onMount } from 'svelte';
	import { createChart, type IChartApi, ColorType, AreaSeries } from 'lightweight-charts';
	import type { ChartDataPoint } from '$lib/api/client';

	let {
		data,
		currency,
		percentageMode = false
	}: { data: ChartDataPoint[]; currency: string; percentageMode?: boolean } = $props();

	let chartContainer: HTMLDivElement;
	let chart: IChartApi | null = null;

	/** Convert absolute data to % growth from the first point when percentageMode is on. */
	function toChartPoints(raw: ChartDataPoint[]) {
		const filtered = raw.filter((d) => d.value > 0);
		if (!percentageMode || filtered.length === 0) {
			return filtered.map((d) => ({ time: d.date.split('T')[0] as string, value: d.value }));
		}
		const baseline = filtered[0].value;
		return filtered.map((d) => ({
			time: d.date.split('T')[0] as string,
			value: ((d.value / baseline) - 1) * 100
		}));
	}

	function buildChart() {
		if (!chartContainer) return;
		if (chart) chart.remove();

		chart = createChart(chartContainer, {
			width: chartContainer.clientWidth,
			height: 300,
			layout: {
				attributionLogo: false,
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
			priceFormat: percentageMode
				? {
						type: 'custom',
						formatter: (price: number) => {
							const sign = price >= 0 ? '+' : '';
							return `${sign}${price.toFixed(2)}%`;
						}
					}
				: {
						type: 'custom',
						formatter: (price: number) => `${currency} ${price.toFixed(2)}`
					}
		});

		const chartPoints = toChartPoints(data);

		if (chartPoints.length > 0) {
			areaSeries.setData(chartPoints as any);
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
		// Rebuild when data or mode changes
		if ((data || percentageMode !== undefined) && chartContainer) buildChart();
	});
</script>

<div bind:this={chartContainer} class="w-full"></div>
