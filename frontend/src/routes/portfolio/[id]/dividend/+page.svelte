<script lang="ts">
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import { authStore } from '$lib/stores/auth.svelte';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';
	import { dividends, type CreateDividendRequest } from '$lib/api/client';

	let portfolioId = $derived(page.params.id);
	let symbol = $state('');
	let amountPerShare = $state<number>(0);
	let nativeCurrency = $state('USD');
	let exDate = $state(new Date().toISOString().split('T')[0]);
	let payDate = $state(new Date().toISOString().split('T')[0]);
	let notes = $state('');
	let submitting = $state(false);
	let error = $state('');

	// Get unique symbols from current positions
	let symbols = $derived(
		portfolioStore.summary?.positions.map((p) => ({
			symbol: p.symbol,
			name: p.instrumentName,
			currency: p.nativeCurrency
		})) || []
	);

	onMount(() => {
		if (!authStore.isLoggedIn) goto('/');
		if (!portfolioStore.summary) portfolioStore.loadAll();
	});

	function onSymbolSelect(s: string) {
		symbol = s;
		const pos = symbols.find((p) => p.symbol === s);
		if (pos) nativeCurrency = pos.currency;
	}

	async function submit() {
		if (!symbol || amountPerShare <= 0) {
			error = 'Please fill in all required fields.';
			return;
		}
		error = '';
		submitting = true;
		try {
			const data: CreateDividendRequest = {
				symbol,
				amountPerShare,
				nativeCurrency,
				exDate,
				payDate,
				notes: notes || undefined
			};
			await dividends.create(portfolioId, data);
			await portfolioStore.loadAll();
			goto('/dashboard');
		} catch (e: any) {
			error = e.message || 'Failed to record dividend.';
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>Record Dividend — Profitr</title>
</svelte:head>

<div class="max-w-2xl mx-auto px-4 py-8">
	<div class="mb-6">
		<a href="/dashboard" class="text-primary hover:underline text-sm">← Back to Dashboard</a>
		<h1 class="text-2xl font-bold mt-2">Record Dividend</h1>
	</div>

	<div class="bg-surface rounded-xl border border-border p-6 space-y-6">
		<!-- Symbol -->
		<div>
			<label class="block text-sm font-medium mb-1.5" for="symbol">Holding</label>
			{#if symbols.length > 0}
				<select
					id="symbol"
					bind:value={symbol}
					onchange={(e) => onSymbolSelect((e.target as HTMLSelectElement).value)}
					class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
				>
					<option value="">Select a holding...</option>
					{#each symbols as s}
						<option value={s.symbol}>{s.symbol} — {s.name} ({s.currency})</option>
					{/each}
				</select>
			{:else}
				<p class="text-text-muted text-sm">No positions yet. Add a transaction first.</p>
			{/if}
		</div>

		<!-- Amount per share -->
		<div>
			<label class="block text-sm font-medium mb-1.5" for="amount">
				Dividend per share ({nativeCurrency})
			</label>
			<input
				id="amount"
				type="number"
				step="any"
				min="0"
				bind:value={amountPerShare}
				placeholder="e.g. 0.82"
				class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
			/>
		</div>

		<!-- Dates -->
		<div class="grid grid-cols-2 gap-4">
			<div>
				<label class="block text-sm font-medium mb-1.5" for="ex-date">Ex-Date</label>
				<input
					id="ex-date"
					type="date"
					bind:value={exDate}
					class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
				/>
			</div>
			<div>
				<label class="block text-sm font-medium mb-1.5" for="pay-date">Pay Date</label>
				<input
					id="pay-date"
					type="date"
					bind:value={payDate}
					class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
				/>
			</div>
		</div>

		<!-- Notes -->
		<div>
			<label class="block text-sm font-medium mb-1.5" for="notes">Notes (optional)</label>
			<input
				id="notes"
				type="text"
				bind:value={notes}
				placeholder="Any notes"
				class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
			/>
		</div>

		{#if error}
			<div class="px-4 py-3 bg-red-50 border border-red-200 rounded-lg text-sm text-danger">
				{error}
			</div>
		{/if}

		<button
			onclick={submit}
			disabled={submitting || !symbol || amountPerShare <= 0}
			class="w-full py-3 bg-primary text-white rounded-lg font-medium hover:bg-primary-dark disabled:opacity-50 transition-colors"
		>
			{submitting ? 'Saving...' : 'Record Dividend'}
		</button>
	</div>
</div>
