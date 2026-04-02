<script lang="ts">
	import { portfolioStore } from '$lib/stores/portfolio.svelte';

	let { onswitch }: { onswitch?: () => void } = $props();

	let open = $state(false);
	let newName = $state('');
	let creating = $state(false);

	async function switchTo(id: string) {
		await portfolioStore.switchPortfolio(id);
		open = false;
		onswitch?.();
	}

	async function createNew() {
		if (!newName.trim()) return;
		creating = true;
		try {
			const p = await portfolioStore.createPortfolio(newName.trim());
			await portfolioStore.switchPortfolio(p.id);
			newName = '';
			open = false;
		} finally {
			creating = false;
		}
	}
</script>

<div class="relative">
	<button
		onclick={() => (open = !open)}
		class="flex items-center gap-2 px-3 py-1.5 rounded-lg border border-border hover:bg-surface-alt transition-colors text-sm"
	>
		<span class="font-medium">{portfolioStore.activePortfolio?.name || 'No portfolio'}</span>
		<svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
			<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7" />
		</svg>
	</button>

	{#if open}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="fixed inset-0 z-40" onclick={() => (open = false)} onkeydown={() => {}}></div>
		<div
			class="absolute right-0 top-10 w-64 bg-surface rounded-lg shadow-lg border border-border z-50 py-1"
		>
			<div class="px-3 py-2 text-xs font-semibold text-text-muted uppercase">Portfolios</div>
			{#each portfolioStore.portfolios as p}
				<button
					onclick={() => switchTo(p.id)}
					class="w-full text-left px-3 py-2 text-sm hover:bg-surface-alt flex items-center justify-between"
					class:bg-blue-50={p.id === portfolioStore.activePortfolio?.id}
				>
					<span>{p.name}</span>
					{#if p.isDefault}
						<span class="text-xs text-primary font-medium">Default</span>
					{/if}
				</button>
			{/each}
			<div class="border-t border-border mt-1 pt-1 px-3 pb-2">
				<div class="flex gap-2 mt-1">
					<input
						type="text"
						placeholder="New portfolio name"
						bind:value={newName}
						onkeydown={(e) => e.key === 'Enter' && createNew()}
						class="flex-1 px-2 py-1 text-sm border border-border rounded focus:outline-none focus:ring-1 focus:ring-primary"
					/>
					<button
						onclick={createNew}
						disabled={creating || !newName.trim()}
						class="px-2 py-1 text-sm bg-primary text-white rounded hover:bg-primary-dark disabled:opacity-50"
					>
						Add
					</button>
				</div>
			</div>
		</div>
	{/if}
</div>
