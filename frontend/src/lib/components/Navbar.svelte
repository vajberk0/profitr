<script lang="ts">
	import { authStore } from '$lib/stores/auth.svelte';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';
	import PortfolioSwitcher from './PortfolioSwitcher.svelte';

	let showUserMenu = $state(false);
</script>

<nav class="bg-surface border-b border-border sticky top-0 z-50">
	<div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
		<div class="flex justify-between h-16">
			<div class="flex items-center gap-6">
				<a href="/dashboard" class="text-xl font-bold text-primary flex items-center gap-2">
					📈 Profitr
				</a>
				{#if authStore.isLoggedIn}
					<a
						href="/dashboard"
						class="text-text-muted hover:text-text font-medium transition-colors"
					>
						Dashboard
					</a>
				{/if}
			</div>

			<div class="flex items-center gap-4">
				{#if authStore.isLoggedIn}
					<PortfolioSwitcher />

					<div class="relative">
						<button
							onclick={() => (showUserMenu = !showUserMenu)}
							class="flex items-center gap-2 p-1.5 rounded-lg hover:bg-surface-alt transition-colors"
						>
							{#if authStore.user?.avatarUrl}
								<img
									src={authStore.user.avatarUrl}
									alt=""
									class="w-8 h-8 rounded-full"
								/>
							{:else}
								<div
									class="w-8 h-8 rounded-full bg-primary text-white flex items-center justify-center text-sm font-bold"
								>
									{authStore.user?.name?.[0] || '?'}
								</div>
							{/if}
						</button>

						{#if showUserMenu}
							<!-- svelte-ignore a11y_no_static_element_interactions -->
							<div
								class="fixed inset-0 z-40"
								onclick={() => (showUserMenu = false)}
								onkeydown={() => {}}
							></div>
							<div
								class="absolute right-0 top-12 w-56 bg-surface rounded-lg shadow-lg border border-border z-50 py-1"
							>
								<div class="px-4 py-2 border-b border-border">
									<p class="font-medium text-sm">{authStore.user?.name}</p>
									<p class="text-xs text-text-muted">{authStore.user?.email}</p>
								</div>
								<a
									href="/settings"
									class="block px-4 py-2 text-sm hover:bg-surface-alt"
									onclick={() => (showUserMenu = false)}>Settings</a
								>
								<button
									onclick={() => authStore.logout()}
									class="w-full text-left px-4 py-2 text-sm hover:bg-surface-alt text-danger"
								>
									Sign out
								</button>
							</div>
						{/if}
					</div>
				{/if}
			</div>
		</div>
	</div>
</nav>
