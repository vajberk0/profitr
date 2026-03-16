<script lang="ts">
	import { market, type TickerSearchResult } from '$lib/api/client';

	let {
		onselect
	}: { onselect: (result: TickerSearchResult) => void } = $props();

	let query = $state('');
	let results = $state<TickerSearchResult[]>([]);
	let loading = $state(false);
	let showDropdown = $state(false);
	let debounceTimer: ReturnType<typeof setTimeout>;

	function onInput() {
		clearTimeout(debounceTimer);
		if (query.length < 1) {
			results = [];
			showDropdown = false;
			return;
		}
		debounceTimer = setTimeout(async () => {
			loading = true;
			try {
				results = await market.search(query);
				showDropdown = results.length > 0;
			} catch {
				results = [];
			} finally {
				loading = false;
			}
		}, 300);
	}

	function select(r: TickerSearchResult) {
		onselect(r);
		query = r.symbol;
		showDropdown = false;
	}

	function typeLabel(type: string) {
		switch (type) {
			case 'ETF': return 'ETF';
			case 'ETC': return 'ETC';
			default: return 'Stock';
		}
	}

	function typeBadgeColor(type: string) {
		switch (type) {
			case 'ETF': return 'bg-purple-100 text-purple-700';
			case 'ETC': return 'bg-amber-100 text-amber-700';
			default: return 'bg-blue-100 text-blue-700';
		}
	}
</script>

<div class="relative">
	<input
		type="text"
		bind:value={query}
		oninput={onInput}
		onfocus={() => results.length > 0 && (showDropdown = true)}
		placeholder="Search tickers (e.g., AAPL, SPY, SIE.DE)"
		class="w-full px-4 py-2.5 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary focus:border-transparent text-sm"
	/>
	{#if loading}
		<div class="absolute right-3 top-3">
			<div class="w-4 h-4 border-2 border-primary border-t-transparent rounded-full animate-spin"></div>
		</div>
	{/if}

	{#if showDropdown}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="fixed inset-0 z-40" onclick={() => (showDropdown = false)} onkeydown={() => {}}></div>
		<div class="absolute w-full mt-1 bg-surface border border-border rounded-lg shadow-lg z-50 max-h-64 overflow-y-auto">
			{#each results as r}
				<button
					onclick={() => select(r)}
					class="w-full text-left px-4 py-3 hover:bg-surface-alt border-b border-border last:border-0 flex items-center justify-between"
				>
					<div>
						<span class="font-semibold text-sm">{r.symbol}</span>
						<span class="text-text-muted text-sm ml-2">{r.name}</span>
					</div>
					<div class="flex items-center gap-2">
						<span class="text-xs text-text-muted">{r.exchange}</span>
						<span class="text-xs px-1.5 py-0.5 rounded {typeBadgeColor(r.type)}">{typeLabel(r.type)}</span>
					</div>
				</button>
			{/each}
		</div>
	{/if}
</div>
