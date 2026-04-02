<script lang="ts">
	import { transactions as txnApi, type Transaction } from '$lib/api/client';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';
	import { formatCurrency, formatQuantity, formatDate } from '$lib/utils/format';

	let { items }: { items: Transaction[] } = $props();

	async function deleteTxn(id: string) {
		if (!confirm('Delete this transaction?')) return;
		await txnApi.delete(id);
		await portfolioStore.loadAll();
	}
</script>

{#if items.length === 0}
	<p class="text-text-muted text-center py-8">No transactions recorded.</p>
{:else}
	<!-- Mobile card layout -->
	<div class="sm:hidden space-y-2">
		{#each items as t}
			<div class="rounded-lg border border-border p-3">
				<div class="flex items-center justify-between mb-1.5">
					<div class="flex items-center gap-2">
						<span class="text-xs font-semibold px-1.5 py-0.5 rounded {
							t.type === 'Buy' ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
						}">{t.type}</span>
						<a href="https://finance.yahoo.com/quote/{t.symbol}" target="_blank" rel="noopener noreferrer" class="font-semibold text-accent hover:underline text-sm">{t.symbol}</a>
					</div>
					<button
						onclick={() => deleteTxn(t.id)}
						class="text-text-muted hover:text-danger transition-colors p-1"
						title="Delete"
					>
						<svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
							<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
								d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
						</svg>
					</button>
				</div>
				<div class="flex items-center justify-between text-xs">
					<span class="text-text-muted">{formatDate(t.transactionDate)}</span>
					<span class="text-text-muted">
						{formatQuantity(t.quantity)} × {formatCurrency(t.pricePerUnit, t.nativeCurrency)}
					</span>
				</div>
				<div class="flex items-center justify-between mt-1">
					<span class="text-xs text-text-muted truncate max-w-[60%]">{t.notes || ''}</span>
					<span class="text-sm font-medium">
						{formatCurrency(t.quantity * t.pricePerUnit, t.nativeCurrency)}
					</span>
				</div>
			</div>
		{/each}
	</div>

	<!-- Desktop table layout -->
	<div class="hidden sm:block overflow-x-auto">
		<table class="w-full text-sm">
			<thead>
				<tr class="border-b border-border text-text-muted text-left">
					<th class="py-2 px-3 font-medium">Date</th>
					<th class="py-2 px-3 font-medium">Type</th>
					<th class="py-2 px-3 font-medium">Symbol</th>
					<th class="py-2 px-3 font-medium text-right">Qty</th>
					<th class="py-2 px-3 font-medium text-right">Price</th>
					<th class="py-2 px-3 font-medium text-right">Total</th>
					<th class="py-2 px-3 font-medium">Notes</th>
					<th class="py-2 px-3"></th>
				</tr>
			</thead>
			<tbody>
				{#each items as t}
					<tr class="border-b border-border hover:bg-surface-alt">
						<td class="py-2 px-3">{formatDate(t.transactionDate)}</td>
						<td class="py-2 px-3">
							<span class="text-xs font-semibold px-1.5 py-0.5 rounded {
								t.type === 'Buy' ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
							}">{t.type}</span>
						</td>
						<td class="py-2 px-3 font-medium"><a href="https://finance.yahoo.com/quote/{t.symbol}" target="_blank" rel="noopener noreferrer" class="text-accent hover:underline">{t.symbol}</a></td>
						<td class="py-2 px-3 text-right">{formatQuantity(t.quantity)}</td>
						<td class="py-2 px-3 text-right">{formatCurrency(t.pricePerUnit, t.nativeCurrency)}</td>
						<td class="py-2 px-3 text-right font-medium">
							{formatCurrency(t.quantity * t.pricePerUnit, t.nativeCurrency)}
						</td>
						<td class="py-2 px-3 text-text-muted text-xs max-w-[120px] truncate">{t.notes || ''}</td>
						<td class="py-2 px-3">
							<button
								onclick={() => deleteTxn(t.id)}
								class="text-text-muted hover:text-danger transition-colors"
								title="Delete"
							>
								<svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
									<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
										d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
								</svg>
							</button>
						</td>
					</tr>
				{/each}
			</tbody>
		</table>
	</div>
{/if}
