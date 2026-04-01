<script lang="ts">
	import { onMount } from 'svelte';
	import { createChart, type IChartApi, ColorType, AreaSeries, LineSeries } from 'lightweight-charts';
	import type { ChartDataPoint } from '$lib/api/client';
	import { themeStore } from '$lib/stores/theme.svelte';

	export interface ComparisonSeries {
		symbol: string;
		data: ChartDataPoint[];
		color: string;
	}

	let {
		data,
		currency,
		percentageMode = false,
		comparisonSeries = []
	}: {
		data: ChartDataPoint[];
		currency: string;
		percentageMode?: boolean;
		comparisonSeries?: ComparisonSeries[];
	} = $props();

	let chartContainer: HTMLDivElement;
	let chart: IChartApi | null = null;

	/**
	 * Convert raw data points to chart-ready {time, value} pairs.
	 * When asPercent is true, values are normalised to % growth from the first point.
	 */
	function toChartPoints(raw: ChartDataPoint[], asPercent: boolean) {
		const filtered = raw.filter((d) => d.value > 0);
		if (!asPercent || filtered.length === 0) {
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

		// Force percentage mode whenever comparison lines are present so all
		// series share the same scale.
		const hasComparisons = comparisonSeries.length > 0;
		const effectivePercentMode = percentageMode || hasComparisons;

		const percentFormatter = (price: number) => {
			const sign = price >= 0 ? '+' : '';
			return `${sign}${price.toFixed(2)}%`;
		};

		const dark = themeStore.isDark;

		chart = createChart(chartContainer, {
			width: chartContainer.clientWidth,
			height: 300,
			layout: {
				attributionLogo: false,
				background: { type: ColorType.Solid, color: dark ? '#1e293b' : '#ffffff' },
				textColor: dark ? '#94a3b8' : '#64748b',
				fontFamily: 'system-ui'
			},
			grid: {
				vertLines: { color: dark ? '#334155' : '#f1f5f9' },
				horzLines: { color: dark ? '#334155' : '#f1f5f9' }
			},
			rightPriceScale: {
				borderColor: dark ? '#334155' : '#e2e8f0'
			},
			timeScale: {
				borderColor: dark ? '#334155' : '#e2e8f0',
				timeVisible: false
			},
			crosshair: {
				vertLine: { labelBackgroundColor: '#2563eb' },
				horzLine: { labelBackgroundColor: '#2563eb' }
			}
		});

		// ── Main portfolio / position area series ────────────────────────────
		const areaSeries = chart.addSeries(AreaSeries, {
			lineColor: '#2563eb',
			topColor: 'rgba(37, 99, 235, 0.3)',
			bottomColor: 'rgba(37, 99, 235, 0.02)',
			lineWidth: 2,
			priceFormat: effectivePercentMode
				? { type: 'custom', formatter: percentFormatter }
				: {
						type: 'custom',
						formatter: (price: number) => `${currency} ${price.toFixed(2)}`
					}
		});

		const mainPoints = toChartPoints(data, effectivePercentMode);
		if (mainPoints.length > 0) {
			areaSeries.setData(mainPoints as any);
		}

		// ── Comparison line series ───────────────────────────────────────────
		for (const cs of comparisonSeries) {
			const lineSeries = chart.addSeries(LineSeries, {
				color: cs.color,
				lineWidth: 2,
				priceFormat: { type: 'custom', formatter: percentFormatter }
			});
			const points = toChartPoints(cs.data, true); // always % for comparisons
			if (points.length > 0) {
				lineSeries.setData(points as any);
			}
		}

		chart.timeScale().fitContent();
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
		// Explicitly reference all reactive inputs so Svelte 5 re-runs this
		// effect whenever any of them change.
		data;
		percentageMode;
		comparisonSeries;
		themeStore.isDark;
		if (chartContainer) buildChart();
	});
</script>

<div bind:this={chartContainer} class="w-full"></div>
