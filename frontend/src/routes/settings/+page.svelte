<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { authStore } from '$lib/stores/auth.svelte';
	import { privacyStore } from '$lib/stores/privacy.svelte';
	import { themeStore, type Theme } from '$lib/stores/theme.svelte';
	import { fx, type CurrencyInfo } from '$lib/api/client';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';

	let currencies = $state<CurrencyInfo[]>([]);
	let selectedCurrency = $state('');
	let saving = $state(false);
	let saved = $state(false);

	onMount(async () => {
		if (!authStore.isLoggedIn) {
			goto('/');
			return;
		}
		currencies = await fx.currencies();
		selectedCurrency = authStore.user?.displayCurrency || 'EUR';
	});

	async function save() {
		saving = true;
		saved = false;
		try {
			await authStore.updateCurrency(selectedCurrency);
			// Reload portfolio data with new currency
			await portfolioStore.loadAll();
			saved = true;
			setTimeout(() => (saved = false), 3000);
		} finally {
			saving = false;
		}
	}
</script>

<svelte:head>
	<title>Settings — Profitr</title>
</svelte:head>

<div class="max-w-2xl mx-auto px-4 py-8">
	<h1 class="text-2xl font-bold mb-6">Settings</h1>

	<!-- Account Info -->
	<div class="bg-surface rounded-xl border border-border p-6 mb-6">
		<h2 class="text-lg font-semibold mb-4">Account</h2>
		<div class="flex items-center gap-4">
			{#if authStore.user?.avatarUrl}
				<img src={authStore.user.avatarUrl} alt="" class="w-16 h-16 rounded-full" />
			{/if}
			<div>
				<p class="font-medium">{authStore.user?.name}</p>
				<p class="text-sm text-text-muted">{authStore.user?.email}</p>
			</div>
		</div>
	</div>

	<!-- Display Currency -->
	<div class="bg-surface rounded-xl border border-border p-6 mb-6">
		<h2 class="text-lg font-semibold mb-4">Display Currency</h2>
		<p class="text-sm text-text-muted mb-4">
			All portfolio values and P&L will be shown in this currency.
			Historical exchange rates are used for accurate cost basis calculations.
		</p>

		<select
			bind:value={selectedCurrency}
			class="w-full px-3 py-2 border border-border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary text-sm mb-4"
		>
			{#each currencies as c}
				<option value={c.code}>{c.code} — {c.name}</option>
			{/each}
		</select>

		<div class="flex items-center gap-3">
			<button
				onclick={save}
				disabled={saving || selectedCurrency === authStore.user?.displayCurrency}
				class="px-4 py-2 bg-primary text-white rounded-lg font-medium hover:bg-primary-dark disabled:opacity-50 transition-colors text-sm"
			>
				{saving ? 'Saving...' : 'Save'}
			</button>
			{#if saved}
				<span class="text-sm text-success font-medium">✓ Saved</span>
			{/if}
		</div>
	</div>

	<!-- Theme -->
	<div class="bg-surface rounded-xl border border-border p-6 mb-6">
		<h2 class="text-lg font-semibold mb-1">Theme</h2>
		<p class="text-sm text-text-muted mb-4">
			Choose your preferred appearance. System follows your device’s light/dark setting.
		</p>
		<div class="flex gap-2">
			{#each [['light', '☀️', 'Light'], ['dark', '🌙', 'Dark'], ['system', '💻', 'System']] as [value, icon, label]}
				<button
					onclick={() => themeStore.set(value as Theme)}
					class="flex items-center gap-2 px-4 py-2 rounded-lg border text-sm font-medium transition-colors
						{themeStore.current === value
							? 'border-primary bg-primary/10 text-primary'
							: 'border-border hover:bg-surface-alt text-text-muted'}"
				>
					{icon} {label}
				</button>
			{/each}
		</div>
	</div>

	<!-- Privacy Mode -->
	<div class="bg-surface rounded-xl border border-border p-6 mb-6">
		<h2 class="text-lg font-semibold mb-1">Privacy Mode</h2>
		<p class="text-sm text-text-muted mb-4">
			Hides absolute monetary amounts (portfolio value, position sizes, P&L in currency) and
			switches the chart to percentage growth. Performance metrics like P&L % remain visible.
			Great for sharing your screen without revealing how much money you have.
		</p>
		<label class="flex items-center gap-3 cursor-pointer select-none">
			<div class="relative">
				<input
					type="checkbox"
					class="sr-only peer"
					checked={privacyStore.enabled}
					onchange={(e) => privacyStore.set((e.target as HTMLInputElement).checked)}
				/>
				<div
					class="w-11 h-6 rounded-full transition-colors
						{privacyStore.enabled ? 'bg-primary' : 'bg-border'}"
				></div>
				<div
					class="absolute top-0.5 left-0.5 w-5 h-5 bg-white dark:bg-gray-200 rounded-full shadow transition-transform
						{privacyStore.enabled ? 'translate-x-5' : 'translate-x-0'}"
				></div>
			</div>
			<span class="text-sm font-medium">
				{privacyStore.enabled ? 'Privacy Mode is ON' : 'Privacy Mode is OFF'}
			</span>
		</label>
	</div>

	<!-- Portfolio Management -->
	<div class="bg-surface rounded-xl border border-border p-6">
		<h2 class="text-lg font-semibold mb-4">Portfolios</h2>
		{#each portfolioStore.portfolios as p}
			<div class="flex items-center justify-between py-3 border-b border-border last:border-0">
				<div>
					<span class="font-medium">{p.name}</span>
					{#if p.isDefault}
						<span class="text-xs text-primary font-medium ml-2">(Default)</span>
					{/if}
				</div>
				<div class="flex gap-2">
					{#if !p.isDefault}
						<button
							onclick={() => portfolioStore.setDefault(p.id)}
							class="text-xs px-2 py-1 border border-border rounded hover:bg-surface-alt"
						>
							Set Default
						</button>
						<button
							onclick={() => {
								if (confirm(`Delete "${p.name}"? This will remove all its transactions.`))
									portfolioStore.deletePortfolio(p.id);
							}}
							class="text-xs px-2 py-1 border border-red-200 text-danger rounded hover:bg-red-50"
						>
							Delete
						</button>
					{/if}
				</div>
			</div>
		{/each}
	</div>
</div>
