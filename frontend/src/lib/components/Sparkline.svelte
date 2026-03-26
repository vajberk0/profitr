<script lang="ts">
	import { onMount } from 'svelte';
	import { market } from '$lib/api/client';

	let { symbol, width = 100, height = 32 }: { symbol: string; width?: number; height?: number } = $props();

	let points: { date: string; close: number }[] = $state([]);
	let loading = $state(true);
	let error = $state(false);

	onMount(async () => {
		try {
			const result = await market.chart(symbol, '1mo');
			if (result?.points?.length) {
				points = result.points;
			}
		} catch {
			error = true;
		} finally {
			loading = false;
		}
	});

	const polyline = $derived.by(() => {
		if (points.length < 2) return '';
		const closes = points.map((p) => p.close);
		const min = Math.min(...closes);
		const max = Math.max(...closes);
		const range = max - min || 1;
		const pad = 2;
		const w = width - pad * 2;
		const h = height - pad * 2;
		return closes
			.map((v, i) => {
				const x = pad + (i / (closes.length - 1)) * w;
				const y = pad + h - ((v - min) / range) * h;
				return `${x.toFixed(1)},${y.toFixed(1)}`;
			})
			.join(' ');
	});

	const color = $derived.by(() => {
		if (points.length < 2) return '#94a3b8';
		const first = points[0].close;
		const last = points[points.length - 1].close;
		return last >= first ? '#22c55e' : '#ef4444';
	});

	const fillPoints = $derived.by(() => {
		if (points.length < 2) return '';
		const closes = points.map((p) => p.close);
		const min = Math.min(...closes);
		const max = Math.max(...closes);
		const range = max - min || 1;
		const pad = 2;
		const w = width - pad * 2;
		const h = height - pad * 2;
		const pts = closes.map((v, i) => {
			const x = pad + (i / (closes.length - 1)) * w;
			const y = pad + h - ((v - min) / range) * h;
			return `${x.toFixed(1)},${y.toFixed(1)}`;
		});
		// Close the polygon along the bottom for the fill
		const lastX = pad + w;
		const firstX = pad;
		const bottom = pad + h;
		return pts.join(' ') + ` ${lastX.toFixed(1)},${bottom.toFixed(1)} ${firstX.toFixed(1)},${bottom.toFixed(1)}`;
	});
</script>

{#if loading}
	<div class="flex items-center justify-center" style="width:{width}px;height:{height}px">
		<div class="w-4 h-4 border-2 border-border border-t-accent rounded-full animate-spin"></div>
	</div>
{:else if error || points.length < 2}
	<div class="flex items-center justify-center text-text-muted text-xs" style="width:{width}px;height:{height}px">
		—
	</div>
{:else}
	<svg {width} {height} viewBox="0 0 {width} {height}" class="block">
		<defs>
			<linearGradient id="fill-{symbol}" x1="0" y1="0" x2="0" y2="1">
				<stop offset="0%" stop-color={color} stop-opacity="0.25" />
				<stop offset="100%" stop-color={color} stop-opacity="0.02" />
			</linearGradient>
		</defs>
		<polygon points={fillPoints} fill="url(#fill-{symbol})" />
		<polyline
			points={polyline}
			fill="none"
			stroke={color}
			stroke-width="1.5"
			stroke-linecap="round"
			stroke-linejoin="round"
		/>
	</svg>
{/if}
