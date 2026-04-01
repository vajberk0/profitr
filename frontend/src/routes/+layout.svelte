<script lang="ts">
	import '../app.css';
	import Navbar from '$lib/components/Navbar.svelte';
	import { authStore } from '$lib/stores/auth.svelte';
	import { privacyStore } from '$lib/stores/privacy.svelte';
	import { themeStore } from '$lib/stores/theme.svelte';
	import { onMount } from 'svelte';

	let { children } = $props();

	onMount(() => {
		authStore.load();
		privacyStore.init();
		themeStore.init();
	});
</script>

{#if authStore.loading}
	<div class="min-h-screen flex items-center justify-center">
		<div class="w-8 h-8 border-4 border-primary border-t-transparent rounded-full animate-spin"></div>
	</div>
{:else}
	<Navbar />
	<main class="min-h-[calc(100vh-64px)]">
		{@render children()}
	</main>
{/if}
