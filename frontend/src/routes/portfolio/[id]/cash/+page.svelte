<script lang="ts">
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import { authStore } from '$lib/stores/auth.svelte';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';
	import { cash, type CreateCashTransactionRequest } from '$lib/api/client';

	let portfolioId = $derived(page.params.id);
	let type = $state<'Deposit' | 'Withdrawal'>('Deposit');
	let amount = $state<number>(0);
	let currency = $state('EUR');
	let transactionDate = $state(new Date().toISOString().split('T')[0]);
	let notes = $state('');
	let submitting = $state(false);
	let error = $state('');

	const currencies = [
		'AUD', 'BRL', 'CAD', 'CHF', 'CNY', 'CZK', 'DKK', 'EUR', 'GBP',
		'HKD', 'HUF', 'IDR', 'ILS', 'INR', 'ISK', 'JPY', 'KRW', 'MXN',
		'MYR', 'NOK', 'NZD', 'PHP', 'PLN', 'RON', 'SEK', 'SGD', 'THB',
		'TRY', 'USD', 'ZAR'
	];

	onMount(() => {
		if (!authStore.isLoggedIn) goto('/');
		if (!portfolioStore.summary) portfolioStore.loadAll();
		// Default to user's display currency
		if (authStore.user?.displayCurrency) {
			currency = authStore.user.displayCurrency;
		}
	});

	async function submit() {
		if (amount <= 0) {
			error = 'Amount must be greater than zero.';
			return;
		}
		error = '';
		submitting = true;
		try {
			const data: CreateCashTransactionRequest = {
				type,
				amount,
				currency,
				transactionDate,
				notes: notes || undefined
			};
			await cash.create(portfolioId, data);
			await portfolioStore.loadAll();
			goto('/dashboard');
		} catch (e: any) {
			error = e.message || 'Failed to record cash transaction.';
		} finally {
			submitting = false;
		}
	}
</script>

<svelte:head>
	<title>Deposit / Withdraw — Profitr</title>
</svelte:head>

<div class="max-w-2xl mx-auto px-4 py-8">
	<div class="mb-6">
		<a href="/dashboard" class="text-primary hover:underline text-sm">← Back to Dashboard</a>
		<h1 class="text-2xl font-bold mt-2">Deposit or Withdraw Cash</h1>
		<p class="text-text-muted text-sm mt-1">
			Record cash moving into or out of your portfolio.
		</p>
	</div>

	<div class="bg-surface rounded-xl border border-border p-6 space-y-6">
		<!-- Type toggle -->
		<div>
			<label class="block text-sm font-medium mb-2">Transaction Type</label>
			<div class="flex gap-2">
				<button
					onclick={() => (type = 'Deposit')}
					class="flex-1 py-2.5 rounded-lg text-sm font-medium border-2 transition-colors {
						type === 'Deposit'
							? 'border-green-500 bg-green-50 text-green-700 dark:bg-green-900/30 dark:text-green-400'
							: 'border-border text-text-muted hover:bg-surface-alt'
					}"
				>
					↓ Deposit
				</button>
				<button
					onclick={() => (type = 'Withdrawal')}
					class="flex-1 py-2.5 rounded-lg text-sm font-medium border-2 transition-colors {
						type === 'Withdrawal'
							? 'border-red-500 bg-red-50 text-red-700 dark:bg-red-900/30 dark:text-red-400'
							: 'border-border text-text-muted hover:bg-surface-alt'
					}"
				>
					↑ Withdrawal
				</button>
			</div>
		</div>

		<!-- Amount and Currency -->
		<div class="grid grid-cols-3 gap-4">
			<div class="col-span-2">
				<label class="block text-sm font-medium mb-1.5" for="amount">Amount</label>
				<input
					id="amount"
					type="number"
					step="any"
					min="0"
					bind:value={amount}
					placeholder="e.g. 10000"
					class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
				/>
			</div>
			<div>
				<label class="block text-sm font-medium mb-1.5" for="currency">Currency</label>
				<select
					id="currency"
					bind:value={currency}
					class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
				>
					{#each currencies as c}
						<option value={c}>{c}</option>
					{/each}
				</select>
			</div>
		</div>

		<!-- Date -->
		<div>
			<label class="block text-sm font-medium mb-1.5" for="date">Date</label>
			<input
				id="date"
				type="date"
				bind:value={transactionDate}
				class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm"
			/>
		</div>

		<!-- Notes -->
		<div>
			<label class="block text-sm font-medium mb-1.5" for="notes">Notes (optional)</label>
			<input
				id="notes"
				type="text"
				bind:value={notes}
				placeholder="e.g. Bank transfer, broker funding"
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
			disabled={submitting || amount <= 0}
			class="w-full py-3 rounded-lg font-medium transition-colors disabled:opacity-50 {
				type === 'Deposit'
					? 'bg-green-600 text-white hover:bg-green-700'
					: 'bg-red-600 text-white hover:bg-red-700'
			}"
		>
			{submitting ? 'Saving...' : type === 'Deposit' ? 'Record Deposit' : 'Record Withdrawal'}
		</button>
	</div>
</div>
