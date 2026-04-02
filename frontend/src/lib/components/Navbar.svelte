<script lang="ts">
	import { authStore } from '$lib/stores/auth.svelte';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';
	import { privacyStore } from '$lib/stores/privacy.svelte';
	import { themeStore } from '$lib/stores/theme.svelte';
	import PortfolioSwitcher from './PortfolioSwitcher.svelte';

	let showUserMenu = $state(false);
	let mobileMenuOpen = $state(false);

	const themeLabel: Record<string, string> = {
		light: 'Light mode',
		dark: 'Dark mode',
		system: 'System theme'
	};
</script>

<nav class="bg-surface border-b border-border sticky top-0 z-50">
	<div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
		<div class="flex justify-between h-14 sm:h-16">
			<div class="flex items-center gap-4 sm:gap-6">
				<a href="/dashboard" class="text-lg sm:text-xl font-bold text-primary flex items-center gap-2">
					📈 Profitr
				</a>
				{#if authStore.isLoggedIn}
					<a
						href="/dashboard"
						class="hidden sm:block text-text-muted hover:text-text font-medium transition-colors"
					>
						Dashboard
					</a>
				{/if}
			</div>

			{#if authStore.isLoggedIn}
				<!-- Desktop nav items -->
				<div class="hidden md:flex items-center gap-4">
					<PortfolioSwitcher />

					<!-- Theme toggle -->
					<button
						onclick={() => themeStore.cycle()}
						title="{themeLabel[themeStore.current]} — click to change"
						class="p-1.5 rounded-lg transition-colors text-text-muted hover:bg-surface-alt hover:text-text"
					>
						{#if themeStore.current === 'dark'}
							<svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
							</svg>
						{:else if themeStore.current === 'light'}
							<svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />
							</svg>
						{:else}
							<svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
							</svg>
						{/if}
					</button>

					<!-- Privacy toggle -->
					<button
						onclick={() => privacyStore.set(!privacyStore.enabled)}
						title={privacyStore.enabled ? 'Privacy Mode ON — click to disable' : 'Privacy Mode OFF — click to enable'}
						class="p-1.5 rounded-lg transition-colors {privacyStore.enabled
							? 'bg-primary/15 text-primary hover:bg-primary/25'
							: 'text-text-muted hover:bg-surface-alt hover:text-text'}"
					>
						{#if privacyStore.enabled}
							<svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
									d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88L6.59 6.59m7.532 7.532l3.128 3.128M3 3l18 18" />
							</svg>
						{:else}
							<svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
									d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
									d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
							</svg>
						{/if}
					</button>

					<!-- User menu -->
					<div class="relative">
						<button
							onclick={() => (showUserMenu = !showUserMenu)}
							class="flex items-center gap-2 p-1.5 rounded-lg hover:bg-surface-alt transition-colors"
						>
							{#if authStore.user?.avatarUrl}
								<img src={authStore.user.avatarUrl} alt="" class="w-8 h-8 rounded-full" />
							{:else}
								<div class="w-8 h-8 rounded-full bg-primary text-white flex items-center justify-center text-sm font-bold">
									{authStore.user?.name?.[0] || '?'}
								</div>
							{/if}
						</button>

						{#if showUserMenu}
							<!-- svelte-ignore a11y_no_static_element_interactions -->
							<div class="fixed inset-0 z-40" onclick={() => (showUserMenu = false)} onkeydown={() => {}}></div>
							<div class="absolute right-0 top-12 w-56 bg-surface rounded-lg shadow-lg border border-border z-50 py-1">
								<div class="px-4 py-2 border-b border-border">
									<p class="font-medium text-sm">{authStore.user?.name}</p>
									<p class="text-xs text-text-muted">{authStore.user?.email}</p>
								</div>
								<a href="/settings" class="block px-4 py-2 text-sm hover:bg-surface-alt" onclick={() => (showUserMenu = false)}>Settings</a>
								<button onclick={() => authStore.logout()} class="w-full text-left px-4 py-2 text-sm hover:bg-surface-alt text-danger">
									Sign out
								</button>
							</div>
						{/if}
					</div>
				</div>

				<!-- Mobile: quick icons + hamburger -->
				<div class="flex md:hidden items-center gap-1">
					<!-- Privacy toggle (keep visible on mobile for quick access) -->
					<button
						onclick={() => privacyStore.set(!privacyStore.enabled)}
						class="p-2 rounded-lg transition-colors {privacyStore.enabled
							? 'bg-primary/15 text-primary'
							: 'text-text-muted'}"
					>
						{#if privacyStore.enabled}
							<svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
									d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88L6.59 6.59m7.532 7.532l3.128 3.128M3 3l18 18" />
							</svg>
						{:else}
							<svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
									d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2"
									d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
							</svg>
						{/if}
					</button>

					<!-- Hamburger -->
					<button
						onclick={() => (mobileMenuOpen = !mobileMenuOpen)}
						class="p-2 rounded-lg text-text-muted hover:bg-surface-alt transition-colors"
					>
						{#if mobileMenuOpen}
							<svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
							</svg>
						{:else}
							<svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
							</svg>
						{/if}
					</button>
				</div>
			{/if}
		</div>
	</div>

	<!-- Mobile menu panel -->
	{#if mobileMenuOpen && authStore.isLoggedIn}
		<!-- svelte-ignore a11y_no_static_element_interactions -->
		<div class="fixed inset-0 top-14 z-40 bg-black/20" onclick={() => (mobileMenuOpen = false)} onkeydown={() => {}}></div>
		<div class="md:hidden absolute left-0 right-0 bg-surface border-b border-border shadow-lg z-50">
			<div class="px-4 py-3 space-y-3">
				<!-- User info -->
				<div class="flex items-center gap-3 pb-3 border-b border-border">
					{#if authStore.user?.avatarUrl}
						<img src={authStore.user.avatarUrl} alt="" class="w-10 h-10 rounded-full" />
					{:else}
						<div class="w-10 h-10 rounded-full bg-primary text-white flex items-center justify-center text-sm font-bold">
							{authStore.user?.name?.[0] || '?'}
						</div>
					{/if}
					<div class="min-w-0">
						<p class="font-medium text-sm truncate">{authStore.user?.name}</p>
						<p class="text-xs text-text-muted truncate">{authStore.user?.email}</p>
					</div>
				</div>

				<!-- Portfolio switcher -->
				<div>
					<p class="text-xs font-semibold text-text-muted uppercase mb-1.5">Portfolio</p>
					<PortfolioSwitcher onswitch={() => (mobileMenuOpen = false)} />
				</div>

				<!-- Nav links -->
				<div class="flex flex-col gap-1">
					<a href="/dashboard" class="px-3 py-2.5 rounded-lg text-sm font-medium hover:bg-surface-alt transition-colors" onclick={() => (mobileMenuOpen = false)}>
						Dashboard
					</a>
					<a href="/settings" class="px-3 py-2.5 rounded-lg text-sm font-medium hover:bg-surface-alt transition-colors" onclick={() => (mobileMenuOpen = false)}>
						Settings
					</a>
				</div>

				<!-- Theme selector -->
				<div class="flex items-center gap-2 pt-2 border-t border-border">
					<span class="text-xs text-text-muted mr-1">Theme:</span>
					{#each [['light', '☀️'], ['dark', '🌙'], ['system', '💻']] as [value, icon]}
						<button
							onclick={() => themeStore.set(value as any)}
							class="flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-medium transition-colors
								{themeStore.current === value
									? 'bg-primary/10 text-primary border border-primary/30'
									: 'text-text-muted hover:bg-surface-alt border border-transparent'}"
						>
							{icon}
						</button>
					{/each}
				</div>

				<!-- Sign out -->
				<button
					onclick={() => { mobileMenuOpen = false; authStore.logout(); }}
					class="w-full text-left px-3 py-2.5 rounded-lg text-sm font-medium text-danger hover:bg-red-50 dark:hover:bg-red-950/30 transition-colors"
				>
					Sign out
				</button>
			</div>
		</div>
	{/if}
</nav>
