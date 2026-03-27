<script lang="ts">
	import type { Position } from '$lib/api/client';
	import { formatCurrency, formatPercent, formatQuantity, pnlColor } from '$lib/utils/format';
	import Sparkline from './Sparkline.svelte';

	let {
		positions,
		displayCurrency,
		selectedSymbols = [],
		privacyMode = false,
		onselect
	}: {
		positions: Position[];
		displayCurrency: string;
		selectedSymbols?: string[];
		privacyMode?: boolean;
		onselect?: (symbols: string[]) => void;
	} = $props();

	function resolveAssetType(assetType: string, instrumentName: string): string {
		if (assetType !== 'ETC' && instrumentName.toUpperCase().includes(' ETC')) return 'ETC';
		return assetType;
	}

	function toggleSymbol(symbol: string) {
		if (!onselect) return;
		const newSelected = selectedSymbols.includes(symbol)
			? selectedSymbols.filter((s) => s !== symbol)
			: [...selectedSymbols, symbol];
		onselect(newSelected);
	}
</script>

{#if positions.length === 0}
	<div class="text-center py-12 text-text-muted">
		<p class="text-lg mb-2">No positions yet</p>
		<p class="text-sm">Add your first transaction to get started.</p>
	</div>
{:else}
	<div class="overflow-x-auto">
		<table class="w-full text-sm">
			<thead>
				<tr class="border-b border-border text-text-muted text-left">
					<th class="py-3 px-3 font-medium">Symbol</th>
					<th class="py-3 px-3 font-medium">1M</th>
					<th class="py-3 px-3 font-medium">Type</th>
					{#if !privacyMode}
						<th class="py-3 px-3 font-medium text-right">Qty</th>
					{/if}
					<th class="py-3 px-3 font-medium text-right">Avg Cost</th>
					<th class="py-3 px-3 font-medium text-right">Price</th>
					{#if !privacyMode}
						<th class="py-3 px-3 font-medium text-right">Value ({displayCurrency})</th>
						<th class="py-3 px-3 font-medium text-right">P&L ({displayCurrency})</th>
					{/if}
					<th class="py-3 px-3 font-medium text-right">P&L %</th>
					{#if !privacyMode}
						<th class="py-3 px-3 font-medium text-right">Dividends</th>
					{/if}
				</tr>
			</thead>
			<tbody>
				{#each positions as p}
					<tr
						class="border-b border-border transition-colors cursor-pointer select-none
							{selectedSymbols.includes(p.symbol)
								? 'bg-primary/10 hover:bg-primary/15 border-l-2 border-l-primary'
								: 'hover:bg-surface-alt'}"
						onclick={() => toggleSymbol(p.symbol)}
					>
						<td class="py-3 px-3">
							<a
								href="https://finance.yahoo.com/quote/{p.symbol}"
								target="_blank"
								rel="noopener noreferrer"
								class="font-semibold text-accent hover:underline"
								onclick={(e) => e.stopPropagation()}>{p.symbol}</a
							>
							<div class="text-xs text-text-muted truncate max-w-[160px]">
								{p.instrumentName}
							</div>
						</td>
						<td class="py-3 px-3">
							<Sparkline symbol={p.symbol} />
						</td>
						<td class="py-3 px-3">
							<span
								class="text-xs px-1.5 py-0.5 rounded {resolveAssetType(p.assetType, p.instrumentName) === 'ETF'
									? 'bg-purple-100 text-purple-700'
									: resolveAssetType(p.assetType, p.instrumentName) === 'ETC'
										? 'bg-amber-100 text-amber-700'
										: 'bg-blue-100 text-blue-700'}">{resolveAssetType(p.assetType, p.instrumentName)}</span
							>
						</td>
						{#if !privacyMode}
							<td class="py-3 px-3 text-right">{formatQuantity(p.quantity)}</td>
						{/if}
						<td class="py-3 px-3 text-right text-text-muted">
							{formatCurrency(p.averageCostBasis, p.nativeCurrency)}
						</td>
						<td class="py-3 px-3 text-right font-medium">
							{formatCurrency(p.currentPrice, p.nativeCurrency)}
						</td>
						{#if !privacyMode}
							<td class="py-3 px-3 text-right font-medium">
								{formatCurrency(p.currentValueDisplay, displayCurrency)}
							</td>
							<td class="py-3 px-3 text-right font-semibold {pnlColor(p.pnLDisplay)}">
								{formatCurrency(p.pnLDisplay, displayCurrency)}
							</td>
						{/if}
						<td class="py-3 px-3 text-right font-semibold {pnlColor(p.pnLPercentDisplay)}">
							{formatPercent(p.pnLPercentDisplay)}
						</td>
						{#if !privacyMode}
							<td class="py-3 px-3 text-right text-text-muted">
								{p.totalDividendsDisplay > 0
									? formatCurrency(p.totalDividendsDisplay, displayCurrency)
									: '—'}
							</td>
						{/if}
					</tr>
				{/each}
			</tbody>
		</table>
	</div>
{/if}
