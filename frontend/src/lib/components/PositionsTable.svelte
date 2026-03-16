<script lang="ts">
	import type { Position } from '$lib/api/client';
	import { formatCurrency, formatPercent, formatQuantity, pnlColor } from '$lib/utils/format';

	let { positions, displayCurrency }: { positions: Position[]; displayCurrency: string } = $props();
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
					<th class="py-3 px-3 font-medium">Type</th>
					<th class="py-3 px-3 font-medium text-right">Qty</th>
					<th class="py-3 px-3 font-medium text-right">Avg Cost</th>
					<th class="py-3 px-3 font-medium text-right">Price</th>
					<th class="py-3 px-3 font-medium text-right">Value ({displayCurrency})</th>
					<th class="py-3 px-3 font-medium text-right">P&L ({displayCurrency})</th>
					<th class="py-3 px-3 font-medium text-right">P&L %</th>
					<th class="py-3 px-3 font-medium text-right">Dividends</th>
				</tr>
			</thead>
			<tbody>
				{#each positions as p}
					<tr class="border-b border-border hover:bg-surface-alt transition-colors">
						<td class="py-3 px-3">
							<div class="font-semibold">{p.symbol}</div>
							<div class="text-xs text-text-muted truncate max-w-[160px]">{p.instrumentName}</div>
						</td>
						<td class="py-3 px-3">
							<span class="text-xs px-1.5 py-0.5 rounded {
								p.assetType === 'ETF' ? 'bg-purple-100 text-purple-700' :
								p.assetType === 'ETC' ? 'bg-amber-100 text-amber-700' :
								'bg-blue-100 text-blue-700'
							}">{p.assetType}</span>
						</td>
						<td class="py-3 px-3 text-right">{formatQuantity(p.quantity)}</td>
						<td class="py-3 px-3 text-right text-text-muted">
							{formatCurrency(p.averageCostBasis, p.nativeCurrency)}
						</td>
						<td class="py-3 px-3 text-right font-medium">
							{formatCurrency(p.currentPrice, p.nativeCurrency)}
						</td>
						<td class="py-3 px-3 text-right font-medium">
							{formatCurrency(p.currentValueDisplay, displayCurrency)}
						</td>
						<td class="py-3 px-3 text-right font-semibold {pnlColor(p.pnLDisplay)}">
							{formatCurrency(p.pnLDisplay, displayCurrency)}
						</td>
						<td class="py-3 px-3 text-right font-semibold {pnlColor(p.pnLPercentDisplay)}">
							{formatPercent(p.pnLPercentDisplay)}
						</td>
						<td class="py-3 px-3 text-right text-text-muted">
							{p.totalDividendsDisplay > 0 ? formatCurrency(p.totalDividendsDisplay, displayCurrency) : '—'}
						</td>
					</tr>
				{/each}
			</tbody>
		</table>
	</div>
{/if}
