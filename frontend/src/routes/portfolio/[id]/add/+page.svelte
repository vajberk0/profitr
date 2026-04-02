<script lang="ts">
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import { authStore } from '$lib/stores/auth.svelte';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';
	import {
		transactions,
		market,
		type TickerSearchResult,
		type CreateTransactionRequest
	} from '$lib/api/client';
	import TickerSearch from '$lib/components/TickerSearch.svelte';

	let portfolioId = $derived(page.params.id);
	let selectedTicker = $state<TickerSearchResult | null>(null);
	let txnType = $state<'Buy' | 'Sell'>('Buy');
	let quantity = $state<number>(0);
	let pricePerUnit = $state<number>(0);
	let txnDate = $state(new Date().toISOString().split('T')[0]);
	let notes = $state('');
	let nativeCurrency = $state('USD');
	let loadingPrice = $state(false);
	let submitting = $state(false);
	let error = $state('');

	onMount(() => {
		if (!authStore.isLoggedIn) goto('/');
	});

	function onTickerSelect(result: TickerSearchResult) {
		selectedTicker = result;
		// Auto-fetch price for today's date
		fetchPrice();
	}

	async function fetchPrice() {
		if (!selectedTicker) return;
		loadingPrice = true;
		try {
			const result = await market.historyPrice(selectedTicker.symbol, txnDate);
			pricePerUnit = result.closePrice;
			nativeCurrency = result.currency;
		} catch {
			// Try getting current quote as fallback
			try {
				const quotes = await market.quote([selectedTicker.symbol]);
				const q = quotes[selectedTicker.symbol.toUpperCase()];
				if (q) {
					pricePerUnit = q.price;
					nativeCurrency = q.currency;
				}
			} catch {
				// leave manual entry
			}
		} finally {
			loadingPrice = false;
		}
	}

	async function submit() {
		if (!selectedTicker || quantity <= 0 || pricePerUnit <= 0) {
			error = 'Please fill in all required fields.';
			return;
		}
		error = '';
		submitting = true;
		try {
			const data: CreateTransactionRequest = {
				type: txnType,
				symbol: selectedTicker.symbol,
				instrumentName: selectedTicker.name,
				assetType: selectedTicker.type,
				quantity,
				pricePerUnit,
				nativeCurrency,
				transactionDate: txnDate,
				notes: notes || undefined
			};
			await transactions.create(portfolioId, data);
			await portfolioStore.loadAll();
			goto('/dashboard');
		} catch (e: any) {
			error = e.message || 'Failed to create transaction.';
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>Add Transaction — Profitr</title>
</svelte:head>

<div class="max-w-2xl mx-auto px-3 sm:px-4 py-4 sm:py-8">
	<div class="mb-4 sm:mb-6">
		<a href="/dashboard" class="text-primary hover:underline text-sm">← Back to Dashboard</a>
		<h1 class="text-xl sm:text-2xl font-bold mt-2">Add Transaction</h1>
	</div>

	<div class="bg-surface rounded-xl border border-border p-4 sm:p-6 space-y-5 sm:space-y-6">
		<!-- Transaction Type -->
		<div class="flex gap-2">
			<button
				onclick={() => (txnType = 'Buy')}
				class="flex-1 py-2.5 rounded-lg font-medium text-sm transition-colors {
					txnType === 'Buy'
						? 'bg-green-600 text-white'
						: 'bg-surface-alt text-text-muted hover:bg-green-50'
				}"
			>
				Buy
			</button>
			<button
				onclick={() => (txnType = 'Sell')}
				class="flex-1 py-2.5 rounded-lg font-medium text-sm transition-colors {
					txnType === 'Sell'
						? 'bg-red-600 text-white'
						: 'bg-surface-alt text-text-muted hover:bg-red-50'
				}"
			>
				Sell
			</button>
		</div>

		<!-- Ticker Search -->
		<div>
			<label class="block text-sm font-medium mb-1.5">Instrument</label>
			<TickerSearch onselect={onTickerSelect} />
			{#if selectedTicker}
				<div class="mt-2 px-3 py-2 bg-blue-50 rounded-lg text-sm flex items-center justify-between">
					<span>
						<span class="font-semibold">{selectedTicker.symbol}</span>
						<span class="text-text-muted ml-2">{selectedTicker.name}</span>
					</span>
					<span class="text-xs px-1.5 py-0.5 rounded bg-blue-100 text-blue-700">
						{selectedTicker.type} · {selectedTicker.exchange}
					</span>
				</div>
			{/if}
		</div>

		<!-- Date -->
		<div>
			<label class="block text-sm font-medium mb-1.5" for="txn-date">Date</label>
			<div class="flex gap-2">
				<input
					id="txn-date"
					type="date"
					bind:value={txnDate}
					onchange={fetchPrice}
					class="flex-1 px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
				/>
			</div>
		</div>

		<!-- Quantity -->
		<div>
			<label class="block text-sm font-medium mb-1.5" for="qty">Quantity</label>
			<input
				id="qty"
				type="number"
				step="any"
				min="0"
				bind:value={quantity}
				placeholder="e.g. 10 or 0.5"
				class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
			/>
		</div>

		<!-- Price -->
		<div>
			<label class="block text-sm font-medium mb-1.5" for="price">
				Price per unit ({nativeCurrency})
				{#if loadingPrice}
					<span class="text-text-muted">(loading...)</span>
				{/if}
			</label>
			<input
				id="price"
				type="number"
				step="any"
				min="0"
				bind:value={pricePerUnit}
				placeholder="Auto-filled from market data"
				class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
			/>
			<p class="text-xs text-text-muted mt-1">
				Price is auto-filled from historical data. You can override it manually.
			</p>
		</div>

		<!-- Total -->
		{#if quantity > 0 && pricePerUnit > 0}
			<div class="px-4 py-3 bg-surface-alt rounded-lg">
				<p class="text-sm text-text-muted">Total</p>
				<p class="text-xl font-bold">
					{new Intl.NumberFormat('en-US', {
						style: 'currency',
						currency: nativeCurrency
					}).format(quantity * pricePerUnit)}
				</p>
			</div>
		{/if}

		<!-- Notes -->
		<div>
			<label class="block text-sm font-medium mb-1.5" for="notes">Notes (optional)</label>
			<input
				id="notes"
				type="text"
				bind:value={notes}
				placeholder="Any notes about this transaction"
				class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
			/>
		</div>

		{#if error}
			<div class="px-4 py-3 bg-red-50 border border-red-200 rounded-lg text-sm text-danger">
				{error}
			</div>
		{/if}

		<!-- Submit -->
		<button
			onclick={submit}
			disabled={submitting || !selectedTicker || quantity <= 0 || pricePerUnit <= 0}
			class="w-full py-3 bg-primary text-white rounded-lg font-medium hover:bg-primary-dark disabled:opacity-50 transition-colors"
		>
			{#if submitting}
				Saving...
			{:else}
				{txnType === 'Buy' ? 'Buy' : 'Sell'}
				{selectedTicker ? selectedTicker.symbol : 'Instrument'}
			{/if}
		</button>
	</div>
</div>
