<script lang="ts">
	import { market, type TickerSearchResult } from '$lib/api/client';

	let { onselect }: { onselect: (result: TickerSearchResult) => void } = $props();

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
		query = '';
		results = [];
		showDropdown = false;
	}

	function onKeydown(e: KeyboardEvent) {
		if (e.key === 'Escape') {
			showDropdown = false;
			query = '';
		}
	}
</script>

<div class="relative">
	<div
		class="flex items-center gap-1.5 border border-dashed border-border rounded-lg px-2.5 py-1 focus-within:border-primary focus-within:ring-1 focus-within:ring-primary transition-colors bg-surface"
	>
		<svg
			class="w-3.5 h-3.5 text-text-muted flex-shrink-0"
			fill="none"
			stroke="currentColor"
			viewBox="0 0 24 24"
		>
			<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
		</svg>
		<input
			type="text"
			bind:value={query}
			oninput={onInput}
			onkeydown={onKeydown}
			onfocus={() => results.length > 0 && (showDropdown = true)}
			placeholder="Compare symbol…"
			class="bg-transparent text-xs w-28 focus:outline-none placeholder-text-muted text-text"
		/>
		{#if loading}
			<div
				class="w-3 h-3 border-2 border-primary border-t-transparent rounded-full animate-spin flex-shrink-0"
			></div>
		{/if}
	</div>

	{#if showDropdown}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="fixed inset-0 z-40" onclick={() => (showDropdown = false)} onkeydown={() => {}}></div>
		<div
			class="absolute right-0 w-72 mt-1 bg-surface border border-border rounded-lg shadow-lg z-50 max-h-64 overflow-y-auto"
		>
			{#each results as r}
				<button
					onclick={() => select(r)}
					class="w-full text-left px-3 py-2.5 hover:bg-surface-alt border-b border-border last:border-0 flex items-center justify-between gap-2"
				>
					<div class="min-w-0">
						<span class="font-semibold text-sm">{r.symbol}</span>
						<span class="text-text-muted text-xs ml-2 truncate">{r.name}</span>
					</div>
					<span class="text-xs text-text-muted flex-shrink-0">{r.exchangeDisplay}</span>
				</button>
			{/each}
		</div>
	{/if}
</div>
