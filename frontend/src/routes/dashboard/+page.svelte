<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { authStore } from '$lib/stores/auth.svelte';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';
	import { privacyStore } from '$lib/stores/privacy.svelte';
	import { market, fx, type ChartDataPoint } from '$lib/api/client';
	import PortfolioChart from '$lib/components/PortfolioChart.svelte';
	import PositionsTable from '$lib/components/PositionsTable.svelte';
	import TransactionList from '$lib/components/TransactionList.svelte';
	import { formatCurrency, formatPercent, pnlColor, pnlBgColor } from '$lib/utils/format';

	let activeTab = $state<'positions' | 'transactions' | 'dividends' | 'cash'>('positions');
	let refreshInterval: ReturnType<typeof setInterval>;
	const ranges = ['1w', '1m', '3m', '6m', '1y', 'all'];

	// Position selection state
	let selectedSymbols = $state<string[]>([]);
	let positionChartData = $state<ChartDataPoint[]>([]);
	let positionChartLoading = $state(false);

	// Which data to show on the chart
	const chartData = $derived(selectedSymbols.length > 0 ? positionChartData : portfolioStore.history);
	const chartLabel = $derived(
		selectedSymbols.length > 0
			? selectedSymbols.join(' + ') + (privacyStore.enabled ? ' (% growth)' : '')
			: privacyStore.enabled
				? 'Portfolio Growth (%)'
				: 'Portfolio Value'
	);

	const rangeMap: Record<string, string> = {
		'1w': '5d',
		'1m': '1mo',
		'3m': '3mo',
		'6m': '6mo',
		'1y': '1y',
		all: 'max'
	};

	async function handlePositionSelect(symbols: string[]) {
		selectedSymbols = symbols;
		if (symbols.length > 0) {
			await loadPositionCharts();
		} else {
			// Reload portfolio history for current range when clearing
			await portfolioStore.loadHistory();
		}
	}

	function handleRangeChange(range: string) {
		portfolioStore.historyRange = range;
		if (selectedSymbols.length > 0) {
			loadPositionCharts();
		} else {
			portfolioStore.loadHistory(range);
		}
	}

	async function loadPositionCharts() {
		if (!portfolioStore.summary) return;
		positionChartLoading = true;
		try {
			const displayCurrency = portfolioStore.summary.displayCurrency;
			const yahooRange = rangeMap[portfolioStore.historyRange] || '1y';

			const positions = portfolioStore.summary.positions.filter((p) =>
				selectedSymbols.includes(p.symbol)
			);
			if (positions.length === 0) {
				positionChartData = [];
				return;
			}

			// Fetch chart data and FX rates in parallel
			const [charts, rates] = await Promise.all([
				Promise.all(positions.map((p) => market.chart(p.symbol, yahooRange))),
				Promise.all(
					positions.map((p) =>
						p.nativeCurrency === displayCurrency
							? Promise.resolve(1)
							: fx.latest(p.nativeCurrency, displayCurrency).then((r) => r.rate)
					)
				)
			]);

			// Build per-position date→value maps
			const positionMaps: Map<string, number>[] = positions.map((pos, i) => {
				const map = new Map<string, number>();
				const chartResult = charts[i];
				const fxRate = rates[i];
				if (!chartResult?.points) return map;
				for (const point of chartResult.points) {
					const date = point.date.split('T')[0];
					map.set(date, point.close * pos.quantity * fxRate);
				}
				return map;
			});

			// Collect all dates and sort
			const allDates = new Set<string>();
			for (const map of positionMaps) {
				for (const date of map.keys()) allDates.add(date);
			}
			const sortedDates = [...allDates].sort();

			// Sum values per date with forward-fill for missing dates
			const lastValues = new Array(positions.length).fill(0);
			positionChartData = sortedDates.map((date) => {
				let total = 0;
				for (let i = 0; i < positionMaps.length; i++) {
					if (positionMaps[i].has(date)) {
						lastValues[i] = positionMaps[i].get(date)!;
					}
					total += lastValues[i];
				}
				return { date, value: total };
			});
		} catch (e) {
			console.error('Failed to load position charts', e);
			positionChartData = [];
		} finally {
			positionChartLoading = false;
		}
	}

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
			<div
				class="w-8 h-8 border-4 border-primary border-t-transparent rounded-full animate-spin"
			></div>
		</div>
	{:else if portfolioStore.summary}
		{@const s = portfolioStore.summary}

		<!-- Summary Cards -->
		<div class="grid grid-cols-1 md:grid-cols-5 gap-4 mb-6">
			<div class="bg-surface rounded-xl border border-border p-5">
				<p class="text-sm text-text-muted mb-1">Total Value</p>
				<p class="text-2xl font-bold">
					{privacyStore.enabled ? '——' : formatCurrency(s.totalValue, s.displayCurrency)}
				</p>
			</div>
			<div class="bg-surface rounded-xl border border-border p-5">
				<p class="text-sm text-text-muted mb-1">Cost Basis</p>
				<p class="text-2xl font-bold">
					{privacyStore.enabled ? '——' : formatCurrency(s.totalCostBasis, s.displayCurrency)}
				</p>
			</div>
			<div class="rounded-xl border p-5 {pnlBgColor(s.totalPnL)}">
				<p class="text-sm text-text-muted mb-1">Total P&L</p>
				<p class="text-2xl font-bold {pnlColor(s.totalPnL)}">
					{#if privacyStore.enabled}
						<span class="text-2xl">{formatPercent(s.totalPnLPercent)}</span>
					{:else}
						{formatCurrency(s.totalPnL, s.displayCurrency)}
						<span class="text-base">{formatPercent(s.totalPnLPercent)}</span>
					{/if}
				</p>
			</div>
			<div class="bg-surface rounded-xl border border-border p-5">
				<p class="text-sm text-text-muted mb-1">Total Dividends</p>
				<p class="text-2xl font-bold">
					{privacyStore.enabled ? '——' : formatCurrency(s.totalDividends, s.displayCurrency)}
				</p>
			</div>
			<div
				class="rounded-xl border p-5 {!privacyStore.enabled && s.cashBalance < 0
					? 'bg-red-50 border-red-200 dark:bg-red-950/30 dark:border-red-800'
					: 'bg-surface border-border'}"
			>
				<p class="text-sm text-text-muted mb-1">Cash Balance</p>
				<p class="text-2xl font-bold {privacyStore.enabled ? '' : pnlColor(s.cashBalance)}">
					{privacyStore.enabled ? '——' : formatCurrency(s.cashBalance, s.displayCurrency)}
				</p>
				{#if !privacyStore.enabled && s.cashBalance < 0}
					<p class="text-xs text-text-muted mt-1">Margin / unrecorded deposits</p>
				{/if}
			</div>
		</div>

		<!-- Chart -->
		<div class="bg-surface rounded-xl border border-border p-5 mb-6">
			<div class="flex items-center justify-between mb-4">
				<div class="flex items-center gap-3">
					<h2 class="text-lg font-semibold">{chartLabel}</h2>
					{#if selectedSymbols.length > 0}
						<div class="flex items-center gap-1.5">
							{#each selectedSymbols as sym}
								<button
									onclick={() =>
										handlePositionSelect(selectedSymbols.filter((s) => s !== sym))}
									class="inline-flex items-center gap-1 text-xs bg-primary/10 text-primary px-2 py-0.5 rounded-full hover:bg-primary/20 transition-colors"
									title="Remove {sym}"
								>
									{sym}
									<svg
										class="w-3 h-3"
										fill="none"
										stroke="currentColor"
										viewBox="0 0 24 24"
									>
										<path
											stroke-linecap="round"
											stroke-linejoin="round"
											stroke-width="2"
											d="M6 18L18 6M6 6l12 12"
										/>
									</svg>
								</button>
							{/each}
							<button
								onclick={() => handlePositionSelect([])}
								class="text-xs text-text-muted hover:text-danger transition-colors ml-1"
							>
								Clear all
							</button>
						</div>
					{/if}
				</div>
				<div class="flex gap-1">
					{#each ranges as r}
						<button
							onclick={() => handleRangeChange(r)}
							class="px-3 py-1 text-xs font-medium rounded-md transition-colors {portfolioStore.historyRange ===
							r
								? 'bg-primary text-white'
								: 'text-text-muted hover:bg-surface-alt'}"
						>
							{r.toUpperCase()}
						</button>
					{/each}
				</div>
			</div>
			{#if positionChartLoading}
				<div class="h-[300px] flex items-center justify-center">
					<div
						class="w-8 h-8 border-4 border-primary border-t-transparent rounded-full animate-spin"
					></div>
				</div>
			{:else if chartData.length > 0}
				<PortfolioChart data={chartData} currency={s.displayCurrency} percentageMode={privacyStore.enabled} />
			{:else}
				<div class="h-[300px] flex items-center justify-center text-text-muted">
					{selectedSymbols.length > 0
						? 'No chart data available for the selected positions.'
						: 'Not enough data for chart. Add transactions to get started.'}
				</div>
			{/if}
		</div>

		<!-- Tabs -->
		<div class="bg-surface rounded-xl border border-border">
			<div class="flex border-b border-border">
				<button
					onclick={() => (activeTab = 'positions')}
					class="px-5 py-3 text-sm font-medium border-b-2 transition-colors {activeTab ===
					'positions'
						? 'border-primary text-primary'
						: 'border-transparent text-text-muted hover:text-text'}"
				>
					Positions ({s.positions.length})
				</button>
				<button
					onclick={() => (activeTab = 'transactions')}
					class="px-5 py-3 text-sm font-medium border-b-2 transition-colors {activeTab ===
					'transactions'
						? 'border-primary text-primary'
						: 'border-transparent text-text-muted hover:text-text'}"
				>
					Transactions ({portfolioStore.transactions.length})
				</button>
				<button
					onclick={() => (activeTab = 'dividends')}
					class="px-5 py-3 text-sm font-medium border-b-2 transition-colors {activeTab ===
					'dividends'
						? 'border-primary text-primary'
						: 'border-transparent text-text-muted hover:text-text'}"
				>
					Dividends ({portfolioStore.dividendsList.length})
				</button>
				<button
					onclick={() => (activeTab = 'cash')}
					class="px-5 py-3 text-sm font-medium border-b-2 transition-colors {activeTab ===
					'cash'
						? 'border-primary text-primary'
						: 'border-transparent text-text-muted hover:text-text'}"
				>
					Cash ({portfolioStore.cashTransactions.length})
				</button>

				<div class="flex-1"></div>

				{#if portfolioStore.activePortfolio}
					<div class="flex items-center gap-2 px-4">
						<a
							href="/portfolio/{portfolioStore.activePortfolio.id}/import"
							class="px-3 py-1.5 text-sm border border-border rounded-lg hover:bg-surface-alt transition-colors"
						>
							↑ Import CSV
						</a>
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
						<a
							href="/portfolio/{portfolioStore.activePortfolio.id}/cash"
							class="px-3 py-1.5 text-sm border border-border rounded-lg hover:bg-surface-alt transition-colors"
						>
							💰 Deposit/Withdraw
						</a>
					</div>
				{/if}
			</div>

			<div class="p-4">
				{#if activeTab === 'positions'}
					<PositionsTable
						positions={s.positions}
						displayCurrency={s.displayCurrency}
						{selectedSymbols}
						privacyMode={privacyStore.enabled}
						onselect={handlePositionSelect}
					/>
				{:else if activeTab === 'transactions'}
					<TransactionList items={portfolioStore.transactions} />
				{:else if activeTab === 'dividends'}
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
											<td class="py-2 px-3 text-right"
												>{formatCurrency(d.amountPerShare, d.nativeCurrency)}</td
											>
											<td class="py-2 px-3 text-text-muted">{d.nativeCurrency}</td>
											<td class="py-2 px-3"
												>{new Date(d.exDate).toLocaleDateString()}</td
											>
											<td class="py-2 px-3"
												>{new Date(d.payDate).toLocaleDateString()}</td
											>
											<td class="py-2 px-3 text-text-muted text-xs"
												>{d.notes || ''}</td
											>
										</tr>
									{/each}
								</tbody>
							</table>
						</div>
					{/if}
				{:else if activeTab === 'cash'}
					{#if portfolioStore.cashTransactions.length === 0}
						<p class="text-text-muted text-center py-8">
							No cash deposits or withdrawals recorded.
						</p>
					{:else}
						<div class="overflow-x-auto">
							<table class="w-full text-sm">
								<thead>
									<tr class="border-b border-border text-text-muted text-left">
										<th class="py-2 px-3 font-medium">Type</th>
										<th class="py-2 px-3 font-medium text-right">Amount</th>
										<th class="py-2 px-3 font-medium">Currency</th>
										<th class="py-2 px-3 font-medium">Date</th>
										<th class="py-2 px-3 font-medium">Notes</th>
										<th class="py-2 px-3 font-medium"></th>
									</tr>
								</thead>
								<tbody>
									{#each portfolioStore.cashTransactions as ct}
										<tr class="border-b border-border hover:bg-surface-alt">
											<td class="py-2 px-3">
												<span
													class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium {ct.type ===
													'Deposit'
														? 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400'
														: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400'}"
												>
													{ct.type === 'Deposit' ? '↓ Deposit' : '↑ Withdrawal'}
												</span>
											</td>
											<td
												class="py-2 px-3 text-right font-medium {ct.type === 'Deposit'
													? 'text-green-600 dark:text-green-400'
													: 'text-red-600 dark:text-red-400'}"
											>
												{ct.type === 'Deposit' ? '+' : '−'}{formatCurrency(
													ct.amount,
													ct.currency
												)}
											</td>
											<td class="py-2 px-3 text-text-muted">{ct.currency}</td>
											<td class="py-2 px-3"
												>{new Date(ct.transactionDate).toLocaleDateString()}</td
											>
											<td class="py-2 px-3 text-text-muted text-xs"
												>{ct.notes || ''}</td
											>
											<td class="py-2 px-3">
												<button
													onclick={async () => {
														if (confirm('Delete this cash transaction?')) {
															await (await import('$lib/api/client')).cash.delete(ct.id);
															await portfolioStore.loadAll();
														}
													}}
													class="text-text-muted hover:text-danger text-xs"
												>
													✕
												</button>
											</td>
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
