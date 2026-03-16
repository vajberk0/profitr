<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { authStore } from '$lib/stores/auth.svelte';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';
	import PortfolioChart from '$lib/components/PortfolioChart.svelte';
	import PositionsTable from '$lib/components/PositionsTable.svelte';
	import TransactionList from '$lib/components/TransactionList.svelte';
	import { formatCurrency, formatPercent, pnlColor, pnlBgColor } from '$lib/utils/format';

	let activeTab = $state<'positions' | 'transactions' | 'dividends'>('positions');
	let refreshInterval: ReturnType<typeof setInterval>;
	const ranges = ['1w', '1m', '3m', '6m', '1y', 'all'];

	onMount(() => {
		if (!authStore.isLoggedIn) {
			goto('/');
			return;
		}
		portfolioStore.loadAll();

		// Auto-refresh every 60s
		refreshInterval = setInterval(() => {
			portfolioStore.loadSummary();
		}, 60000);

		return () => clearInterval(refreshInterval);
	});
</script>

<svelte:head>
	<title>Dashboard — Profitr</title>
</svelte:head>

<div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6">
	{#if portfolioStore.loading && !portfolioStore.summary}
		<div class="flex items-center justify-center py-24">
			<div class="w-8 h-8 border-4 border-primary border-t-transparent rounded-full animate-spin"></div>
		</div>
	{:else if portfolioStore.summary}
		{@const s = portfolioStore.summary}

		<!-- Summary Cards -->
		<div class="grid grid-cols-1 md:grid-cols-4 gap-4 mb-6">
			<div class="bg-surface rounded-xl border border-border p-5">
				<p class="text-sm text-text-muted mb-1">Total Value</p>
				<p class="text-2xl font-bold">{formatCurrency(s.totalValue, s.displayCurrency)}</p>
			</div>
			<div class="bg-surface rounded-xl border border-border p-5">
				<p class="text-sm text-text-muted mb-1">Cost Basis</p>
				<p class="text-2xl font-bold">{formatCurrency(s.totalCostBasis, s.displayCurrency)}</p>
			</div>
			<div class="rounded-xl border p-5 {pnlBgColor(s.totalPnL)}">
				<p class="text-sm text-text-muted mb-1">Total P&L</p>
				<p class="text-2xl font-bold {pnlColor(s.totalPnL)}">
					{formatCurrency(s.totalPnL, s.displayCurrency)}
					<span class="text-base">{formatPercent(s.totalPnLPercent)}</span>
				</p>
			</div>
			<div class="bg-surface rounded-xl border border-border p-5">
				<p class="text-sm text-text-muted mb-1">Total Dividends</p>
				<p class="text-2xl font-bold">{formatCurrency(s.totalDividends, s.displayCurrency)}</p>
			</div>
		</div>

		<!-- Chart -->
		<div class="bg-surface rounded-xl border border-border p-5 mb-6">
			<div class="flex items-center justify-between mb-4">
				<h2 class="text-lg font-semibold">Portfolio Value</h2>
				<div class="flex gap-1">
					{#each ranges as r}
						<button
							onclick={() => portfolioStore.loadHistory(r)}
							class="px-3 py-1 text-xs font-medium rounded-md transition-colors {
								portfolioStore.historyRange === r
									? 'bg-primary text-white'
									: 'text-text-muted hover:bg-surface-alt'
							}"
						>
							{r.toUpperCase()}
						</button>
					{/each}
				</div>
			</div>
			{#if portfolioStore.history.length > 0}
				<PortfolioChart data={portfolioStore.history} currency={s.displayCurrency} />
			{:else}
				<div class="h-[300px] flex items-center justify-center text-text-muted">
					Not enough data for chart. Add transactions to get started.
				</div>
			{/if}
		</div>

		<!-- Tabs -->
		<div class="bg-surface rounded-xl border border-border">
			<div class="flex border-b border-border">
				<button
					onclick={() => (activeTab = 'positions')}
					class="px-5 py-3 text-sm font-medium border-b-2 transition-colors {
						activeTab === 'positions'
							? 'border-primary text-primary'
							: 'border-transparent text-text-muted hover:text-text'
					}"
				>
					Positions ({s.positions.length})
				</button>
				<button
					onclick={() => (activeTab = 'transactions')}
					class="px-5 py-3 text-sm font-medium border-b-2 transition-colors {
						activeTab === 'transactions'
							? 'border-primary text-primary'
							: 'border-transparent text-text-muted hover:text-text'
					}"
				>
					Transactions ({portfolioStore.transactions.length})
				</button>
				<button
					onclick={() => (activeTab = 'dividends')}
					class="px-5 py-3 text-sm font-medium border-b-2 transition-colors {
						activeTab === 'dividends'
							? 'border-primary text-primary'
							: 'border-transparent text-text-muted hover:text-text'
					}"
				>
					Dividends ({portfolioStore.dividendsList.length})
				</button>

				<div class="flex-1"></div>

				{#if portfolioStore.activePortfolio}
					<div class="flex items-center gap-2 px-4">
						<a
							href="/portfolio/{portfolioStore.activePortfolio.id}/add"
							class="px-3 py-1.5 text-sm bg-primary text-white rounded-lg hover:bg-primary-dark transition-colors"
						>
							+ Add Transaction
						</a>
						<a
							href="/portfolio/{portfolioStore.activePortfolio.id}/dividend"
							class="px-3 py-1.5 text-sm border border-border rounded-lg hover:bg-surface-alt transition-colors"
						>
							+ Dividend
						</a>
					</div>
				{/if}
			</div>

			<div class="p-4">
				{#if activeTab === 'positions'}
					<PositionsTable positions={s.positions} displayCurrency={s.displayCurrency} />
				{:else if activeTab === 'transactions'}
					<TransactionList items={portfolioStore.transactions} />
				{:else}
					{#if portfolioStore.dividendsList.length === 0}
						<p class="text-text-muted text-center py-8">No dividends recorded.</p>
					{:else}
						<div class="overflow-x-auto">
							<table class="w-full text-sm">
								<thead>
									<tr class="border-b border-border text-text-muted text-left">
										<th class="py-2 px-3 font-medium">Symbol</th>
										<th class="py-2 px-3 font-medium text-right">Amount/Share</th>
										<th class="py-2 px-3 font-medium">Currency</th>
										<th class="py-2 px-3 font-medium">Ex-Date</th>
										<th class="py-2 px-3 font-medium">Pay Date</th>
										<th class="py-2 px-3 font-medium">Notes</th>
									</tr>
								</thead>
								<tbody>
									{#each portfolioStore.dividendsList as d}
										<tr class="border-b border-border hover:bg-surface-alt">
											<td class="py-2 px-3 font-medium">{d.symbol}</td>
											<td class="py-2 px-3 text-right">{formatCurrency(d.amountPerShare, d.nativeCurrency)}</td>
											<td class="py-2 px-3 text-text-muted">{d.nativeCurrency}</td>
											<td class="py-2 px-3">{new Date(d.exDate).toLocaleDateString()}</td>
											<td class="py-2 px-3">{new Date(d.payDate).toLocaleDateString()}</td>
											<td class="py-2 px-3 text-text-muted text-xs">{d.notes || ''}</td>
										</tr>
									{/each}
								</tbody>
							</table>
						</div>
					{/if}
				{/if}
			</div>
		</div>
	{:else}
		<div class="text-center py-24">
			<h2 class="text-2xl font-bold mb-4">Welcome to Profitr!</h2>
			<p class="text-text-muted mb-6">Create your first portfolio to get started.</p>
		</div>
	{/if}
</div>
