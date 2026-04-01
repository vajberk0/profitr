<script lang="ts">
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import { authStore } from '$lib/stores/auth.svelte';
	import { portfolioStore } from '$lib/stores/portfolio.svelte';
	import {
		importApi,
		market,
		type ImportPreviewRow,
		type ImportCashPreviewRow,
		type ImportPreviewResponse,
		type ImportResultResponse,
		type SymbolMapping,
		type TickerSearchResult
	} from '$lib/api/client';
	import { formatCurrency } from '$lib/utils/format';

	let portfolioId = $derived(page.params.id!);

	type Step = 'upload' | 'preview' | 'importing' | 'done';
	let step = $state<Step>('upload');
	let error = $state('');
	let parsing = $state(false);

	// Upload step
	let fileInput = $state<HTMLInputElement | null>(null);
	let fileName = $state('');
	let dragOver = $state(false);

	// Preview step
	let previewRows = $state<(ImportPreviewRow & { selected: boolean })[]>([]);
	let cashPreviewRows = $state<(ImportCashPreviewRow & { selected: boolean })[]>([]);
	let previewResponse = $state<ImportPreviewResponse | null>(null);

	// Symbol mapping: csvSymbol → selected TickerSearchResult
	let userMappings = $state<Record<string, TickerSearchResult>>({});
	// Custom search results per symbol
	let searchResults = $state<Record<string, TickerSearchResult[]>>({});
	let searchingSymbol = $state<string | null>(null);

	// Result step
	let result = $state<ImportResultResponse | null>(null);

	let selectedCount = $derived(previewRows.filter((r) => r.selected).length);
	let allSelected = $derived(previewRows.length > 0 && previewRows.every((r) => r.selected));
	let selectedCashCount = $derived(cashPreviewRows.filter((r) => r.selected).length);
	let allCashSelected = $derived(
		cashPreviewRows.length > 0 && cashPreviewRows.every((r) => r.selected)
	);
	let totalSelectedCount = $derived(selectedCount + selectedCashCount);

	// All unique symbols, unresolved first
	let allSymbolMappings = $derived(
		previewResponse
			? Object.values(previewResponse.symbolMappings).sort((a, b) => {
					if (a.resolved === b.resolved) return a.csvSymbol.localeCompare(b.csvSymbol);
					return a.resolved ? 1 : -1;
				})
			: []
	);

	let unresolvedCount = $derived(allSymbolMappings.filter((m) => !m.resolved).length);

	onMount(() => {
		if (!authStore.isLoggedIn) goto('/');
	});

	async function handleFile(file: File) {
		if (!file.name.endsWith('.csv')) {
			error = 'Please select a CSV file.';
			return;
		}
		error = '';
		fileName = file.name;
		parsing = true;

		try {
			const response = await importApi.parse(portfolioId, file);
			previewResponse = response;
			previewRows = response.rows.map((r) => ({ ...r, selected: r.isValid }));
			cashPreviewRows = (response.cashRows ?? []).map((r) => ({ ...r, selected: r.isValid }));

			// Initialize user mappings for ALL symbols
			const mappings: Record<string, TickerSearchResult> = {};
			for (const [csvSymbol, mapping] of Object.entries(response.symbolMappings)) {
				if (mapping.resolved && mapping.suggestions.length > 0) {
					// Resolved: preselect the matched Yahoo symbol from suggestions
					const resolved = mapping.suggestions.find(
						(s) => s.symbol === mapping.yahooSymbol
					);
					if (resolved) mappings[csvSymbol] = resolved;
					else mappings[csvSymbol] = mapping.suggestions[0];
				} else if (!mapping.resolved && mapping.suggestions.length > 0) {
					// Unresolved: preselect first suggestion
					mappings[csvSymbol] = mapping.suggestions[0];
				}
			}
			userMappings = mappings;

			step = 'preview';
		} catch (e: any) {
			error = e.message || 'Failed to parse CSV file.';
		} finally {
			parsing = false;
		}
	}

	function onFileSelect(e: Event) {
		const input = e.target as HTMLInputElement;
		if (input.files?.[0]) handleFile(input.files[0]);
	}

	function onDrop(e: DragEvent) {
		e.preventDefault();
		dragOver = false;
		if (e.dataTransfer?.files?.[0]) handleFile(e.dataTransfer.files[0]);
	}

	function toggleAll() {
		const newVal = !allSelected;
		previewRows = previewRows.map((r) => ({ ...r, selected: r.isValid ? newVal : false }));
	}

	function toggleRow(index: number) {
		previewRows[index].selected = !previewRows[index].selected;
	}

	function toggleAllCash() {
		const newVal = !allCashSelected;
		cashPreviewRows = cashPreviewRows.map((r) => ({
			...r,
			selected: r.isValid ? newVal : false
		}));
	}

	function toggleCashRow(index: number) {
		cashPreviewRows[index].selected = !cashPreviewRows[index].selected;
	}

	function selectMapping(csvSymbol: string, suggestion: TickerSearchResult) {
		userMappings = { ...userMappings, [csvSymbol]: suggestion };
	}

	function onMappingDropdownChange(csvSymbol: string, yahooSymbol: string) {
		// Search across both original suggestions and custom search results
		const mapping = previewResponse?.symbolMappings[csvSymbol];
		const origSuggestions = mapping?.suggestions ?? [];
		const customSuggestions = searchResults[csvSymbol] ?? [];
		const all = [...origSuggestions, ...customSuggestions];
		const selected = all.find((s) => s.symbol === yahooSymbol);
		if (selected) {
			selectMapping(csvSymbol, selected);
		}
	}

	async function doSearch(csvSymbol: string, query: string) {
		if (!query || query.length < 1) {
			searchResults = { ...searchResults, [csvSymbol]: [] };
			return;
		}
		searchingSymbol = csvSymbol;
		try {
			const results = await market.search(query);
			searchResults = { ...searchResults, [csvSymbol]: results };
			if (results.length > 0) {
				selectMapping(csvSymbol, results[0]);
			}
		} catch {
			// ignore
		} finally {
			if (searchingSymbol === csvSymbol) searchingSymbol = null;
		}
	}

	// Get effective symbol/name/assetType for a row, applying user mappings
	function getEffective(row: ImportPreviewRow) {
		const userPick = userMappings[row.symbol];
		if (userPick) {
			return { symbol: userPick.symbol, name: userPick.name, assetType: userPick.type };
		}
		return { symbol: row.symbol, name: row.instrumentName, assetType: row.assetType };
	}

	function getSuggestionsForSymbol(csvSymbol: string): TickerSearchResult[] {
		const custom = searchResults[csvSymbol];
		if (custom && custom.length > 0) return custom;
		return previewResponse?.symbolMappings[csvSymbol]?.suggestions ?? [];
	}

	async function confirmImport() {
		const selected = previewRows.filter((r) => r.selected);
		const selectedCash = cashPreviewRows.filter((r) => r.selected);
		if (selected.length === 0 && selectedCash.length === 0) {
			error = 'No transactions selected.';
			return;
		}

		error = '';
		step = 'importing';

		try {
			result = await importApi.confirm(portfolioId, {
				rows: selected.map((r) => {
					const eff = getEffective(r);
					return {
						date: r.date,
						symbol: eff.symbol,
						instrumentName: eff.name,
						assetType: eff.assetType,
						transactionType: r.transactionType,
						quantity: r.quantity,
						pricePerUnit: r.pricePerUnit,
						nativeCurrency: r.nativeCurrency,
						notes:
							r.commission > 0
								? `IBKR commission: ${r.nativeCurrency} ${r.commission.toFixed(2)}`
								: undefined
					};
				}),
				cashRows: selectedCash.map((r) => ({
					date: r.date,
					cashType: r.cashType,
					amount: r.amount,
					currency: r.currency,
					notes: `IBKR import: ${r.description}`
				}))
			});
			step = 'done';
			await portfolioStore.loadAll();
		} catch (e: any) {
			error = e.message || 'Import failed.';
			step = 'preview';
		}
	}

	function reset() {
		step = 'upload';
		fileName = '';
		previewRows = [];
		cashPreviewRows = [];
		previewResponse = null;
		userMappings = {};
		searchResults = {};
		result = null;
		error = '';
	}

	function typeBadgeColor(type: string) {
		switch (type) {
			case 'ETF':
				return 'bg-purple-100 text-purple-700';
			case 'ETC':
				return 'bg-amber-100 text-amber-700';
			default:
				return 'bg-blue-100 text-blue-700';
		}
	}
</script>

<svelte:head>
	<title>Import Transactions — Profitr</title>
</svelte:head>

<div class="max-w-5xl mx-auto px-4 py-8">
	<div class="mb-6">
		<a href="/dashboard" class="text-primary hover:underline text-sm">← Back to Dashboard</a>
		<h1 class="text-2xl font-bold mt-2">Import Transactions</h1>
		<p class="text-text-muted text-sm mt-1">
			Import transactions from an IBKR Transaction History CSV export.
		</p>
	</div>

	{#if error}
		<div class="mb-4 px-4 py-3 bg-red-50 border border-red-200 rounded-lg text-sm text-danger">
			{error}
		</div>
	{/if}

	<!-- Step 1: Upload -->
	{#if step === 'upload'}
		<div class="bg-surface rounded-xl border border-border p-6">
			<h2 class="text-lg font-semibold mb-4">Upload CSV File</h2>
			<p class="text-sm text-text-muted mb-4">
				Export your Transaction History from IBKR's Flex Queries or Reports as CSV, then upload
				it here.
			</p>

			<!-- svelte-ignore a11y_no_static_element_interactions -->
			<div
				class="border-2 border-dashed rounded-xl p-12 text-center transition-colors {dragOver
					? 'border-primary bg-blue-50'
					: 'border-border hover:border-primary/50'}"
				ondragover={(e) => {
					e.preventDefault();
					dragOver = true;
				}}
				ondragleave={() => (dragOver = false)}
				ondrop={onDrop}
			>
				{#if parsing}
					<div class="flex flex-col items-center gap-3">
						<div
							class="w-8 h-8 border-4 border-primary border-t-transparent rounded-full animate-spin"
						></div>
						<p class="text-sm text-text-muted">Parsing {fileName}...</p>
					</div>
				{:else}
					<div class="flex flex-col items-center gap-3">
						<svg
							class="w-12 h-12 text-text-muted"
							fill="none"
							viewBox="0 0 24 24"
							stroke="currentColor"
							stroke-width="1.5"
						>
							<path
								stroke-linecap="round"
								stroke-linejoin="round"
								d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m6.75 12l-3-3m0 0l-3 3m3-3v6m-1.5-15H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z"
							/>
						</svg>
						<div>
							<p class="text-sm font-medium">Drop your CSV file here or</p>
							<button
								onclick={() => fileInput?.click()}
								class="text-sm text-primary hover:underline font-medium"
							>
								browse to select
							</button>
						</div>
						<p class="text-xs text-text-muted">Supports IBKR Transaction History CSV</p>
					</div>
				{/if}
			</div>

			<input
				bind:this={fileInput}
				type="file"
				accept=".csv"
				onchange={onFileSelect}
				class="hidden"
			/>
		</div>

		<!-- Step 2: Preview -->
	{:else if step === 'preview'}
		<!-- Symbol Mapping Section — ALL symbols -->
		<div class="bg-surface rounded-xl border border-border mb-4">
			<div
				class="p-4 border-b border-border {unresolvedCount > 0
					? 'bg-amber-50'
					: 'bg-surface-alt'} rounded-t-xl"
			>
				<div class="flex items-center gap-2">
					{#if unresolvedCount > 0}
						<svg
							class="w-5 h-5 text-amber-600 flex-shrink-0"
							fill="none"
							viewBox="0 0 24 24"
							stroke="currentColor"
							stroke-width="2"
						>
							<path
								stroke-linecap="round"
								stroke-linejoin="round"
								d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z"
							/>
						</svg>
					{/if}
					<div>
						<h3 class="text-sm font-semibold">Symbol Mapping</h3>
						<p class="text-xs text-text-muted mt-0.5">
							Verify each CSV symbol maps to the correct Yahoo Finance ticker.
							{#if unresolvedCount > 0}
								<span class="text-amber-700 font-medium"
									>{unresolvedCount} symbol{unresolvedCount !== 1 ? 's' : ''} could not be
									matched automatically.</span
								>
							{/if}
						</p>
					</div>
				</div>
			</div>
			<div class="divide-y divide-border">
				{#each allSymbolMappings as mapping}
					{@const csvSymbol = mapping.csvSymbol}
					{@const currentPick = userMappings[csvSymbol]}
					{@const suggestions = getSuggestionsForSymbol(csvSymbol)}
					{@const isSearched =
						(searchResults[csvSymbol]?.length ?? 0) > 0}

					<div class="flex items-start gap-3 p-4">
						<!-- CSV symbol -->
						<div class="flex items-center gap-2 min-w-[120px] pt-1.5">
							{#if !mapping.resolved}
								<span class="text-amber-500 text-xs" title="Not found on Yahoo Finance"
									>⚠</span
								>
							{:else}
								<span class="text-green-500 text-xs" title="Matched">✓</span>
							{/if}
							<span
								class="font-mono font-semibold text-sm px-2 py-0.5 rounded {mapping.resolved
									? 'bg-gray-100 text-gray-800 dark:bg-gray-800 dark:text-gray-200'
									: 'bg-amber-100 text-amber-800'}"
							>
								{csvSymbol}
							</span>
							<svg
								class="w-4 h-4 text-text-muted flex-shrink-0"
								fill="none"
								viewBox="0 0 24 24"
								stroke="currentColor"
								stroke-width="2"
							>
								<path
									stroke-linecap="round"
									stroke-linejoin="round"
									d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3"
								/>
							</svg>
						</div>

						<!-- Mapping picker -->
						<div class="flex-1 space-y-1.5">
							<!-- Dropdown of suggestions -->
							<select
								class="w-full px-3 py-1.5 border border-border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-primary bg-surface"
								value={currentPick?.symbol ?? ''}
								onchange={(e) =>
									onMappingDropdownChange(
										csvSymbol,
										(e.target as HTMLSelectElement).value
									)}
							>
								{#if suggestions.length === 0 && !currentPick}
									<option value="">No suggestions found — use search below</option>
								{/if}
								{#each suggestions as s}
									<option value={s.symbol}>
										{s.symbol} — {s.name} ({s.exchangeDisplay || s.exchange}{s.type
											? ' · ' + s.type
											: ''})
									</option>
								{/each}
							</select>

							<!-- Custom search input -->
							<div class="flex items-center gap-2">
								<input
									type="text"
									placeholder="Type ticker and press Enter..."
									class="flex-1 px-2.5 py-1 border border-border rounded-md text-xs focus:outline-none focus:ring-1 focus:ring-primary"
									onkeydown={(e) => {
										if (e.key === 'Enter') {
											e.preventDefault();
											doSearch(csvSymbol, (e.target as HTMLInputElement).value);
										}
									}}
								/>
								<button
									type="button"
									class="px-2 py-1 text-xs border border-border rounded-md hover:bg-surface-alt transition-colors"
									onclick={(e) => {
										const input = (e.currentTarget as HTMLElement)
											.previousElementSibling as HTMLInputElement;
										if (input?.value) doSearch(csvSymbol, input.value);
									}}
								>
									Search
								</button>
								{#if searchingSymbol === csvSymbol}
									<div
										class="w-4 h-4 border-2 border-primary border-t-transparent rounded-full animate-spin flex-shrink-0"
									></div>
								{/if}
							</div>

							<!-- Selection summary -->
							{#if currentPick}
								<p class="text-xs text-green-700">
									✓ <span class="font-semibold">{currentPick.symbol}</span> — {currentPick.name}
									<span class="text-text-muted"
										>({currentPick.exchangeDisplay || currentPick.exchange}{currentPick.type
											? ' · ' + currentPick.type
											: ''})</span
									>
								</p>
							{:else}
								<p class="text-xs text-amber-600">
									⚠ No mapping — rows will use "{csvSymbol}" as-is
								</p>
							{/if}
						</div>
					</div>
				{/each}
			</div>
		</div>

		<!-- Preview Table -->
		<!-- Cash Transactions Table -->
		{#if cashPreviewRows.length > 0}
			<div class="bg-surface rounded-xl border border-border mb-4">
				<div class="p-5 border-b border-border">
					<h2 class="text-lg font-semibold">Cash Deposits & Withdrawals</h2>
					<p class="text-sm text-text-muted mt-0.5">
						{selectedCashCount} of {cashPreviewRows.length} cash transaction{cashPreviewRows.length !== 1 ? 's' : ''} selected
					</p>
				</div>
				<div class="overflow-x-auto">
					<table class="w-full text-sm">
						<thead>
							<tr class="border-b border-border text-text-muted text-left bg-surface-alt">
								<th class="py-2.5 px-3">
									<input
										type="checkbox"
										checked={allCashSelected}
										onchange={toggleAllCash}
										class="rounded"
									/>
								</th>
								<th class="py-2.5 px-3 font-medium">Date</th>
								<th class="py-2.5 px-3 font-medium">Type</th>
								<th class="py-2.5 px-3 font-medium">Description</th>
								<th class="py-2.5 px-3 font-medium text-right">Amount</th>
								<th class="py-2.5 px-3 font-medium">Currency</th>
							</tr>
						</thead>
						<tbody>
							{#each cashPreviewRows as row, i}
								<tr
									class="border-b border-border transition-colors {row.selected
										? 'hover:bg-surface-alt'
										: 'opacity-40'}"
								>
									<td class="py-2 px-3">
										<input
											type="checkbox"
											checked={row.selected}
											disabled={!row.isValid}
											onchange={() => toggleCashRow(i)}
											class="rounded"
										/>
									</td>
									<td class="py-2 px-3 whitespace-nowrap">{row.date}</td>
									<td class="py-2 px-3">
										<span
											class="px-1.5 py-0.5 rounded text-xs font-medium {row.cashType === 'Deposit'
												? 'bg-green-100 text-green-700'
												: 'bg-red-100 text-red-700'}"
										>
											{row.cashType}
										</span>
									</td>
									<td class="py-2 px-3 text-text-muted">{row.description}</td>
									<td class="py-2 px-3 text-right font-medium"
										>{formatCurrency(row.amount, row.currency)}</td
									>
									<td class="py-2 px-3 font-mono text-xs">{row.currency}</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			</div>
		{/if}

		<!-- Transactions Preview Table -->
		<div class="bg-surface rounded-xl border border-border">
			<div class="p-5 border-b border-border flex items-center justify-between">
				<div>
					<h2 class="text-lg font-semibold">Preview Import</h2>
					<p class="text-sm text-text-muted mt-0.5">
						{totalSelectedCount} item{totalSelectedCount !== 1 ? 's' : ''} selected
						({selectedCount} transaction{selectedCount !== 1 ? 's' : ''}{#if selectedCashCount > 0}, {selectedCashCount} cash{/if})
						from <span class="font-medium">{fileName}</span>
					</p>
				</div>
				<div class="flex gap-2">
					<button
						onclick={reset}
						class="px-4 py-2 text-sm border border-border rounded-lg hover:bg-surface-alt transition-colors"
					>
						Cancel
					</button>
					<button
						onclick={confirmImport}
						disabled={totalSelectedCount === 0}
						class="px-4 py-2 text-sm bg-primary text-white rounded-lg hover:bg-primary-dark disabled:opacity-50 transition-colors"
					>
						Import {totalSelectedCount} Item{totalSelectedCount !== 1 ? 's' : ''}
					</button>
				</div>
			</div>

			<div class="overflow-x-auto">
				<table class="w-full text-sm">
					<thead>
						<tr class="border-b border-border text-text-muted text-left bg-surface-alt">
							<th class="py-2.5 px-3">
								<input
									type="checkbox"
									checked={allSelected}
									onchange={toggleAll}
									class="rounded"
								/>
							</th>
							<th class="py-2.5 px-3 font-medium">Date</th>
							<th class="py-2.5 px-3 font-medium">Type</th>
							<th class="py-2.5 px-3 font-medium">Symbol</th>
							<th class="py-2.5 px-3 font-medium">Name</th>
							<th class="py-2.5 px-3 font-medium">Asset</th>
							<th class="py-2.5 px-3 font-medium text-right">Qty</th>
							<th class="py-2.5 px-3 font-medium text-right">Price</th>
							<th class="py-2.5 px-3 font-medium text-right">Total</th>
							<th class="py-2.5 px-3 font-medium text-right">Commission</th>
						</tr>
					</thead>
					<tbody>
						{#each previewRows as row, i}
							{@const eff = getEffective(row)}
							{@const wasRemapped = eff.symbol !== row.symbol}
							<tr
								class="border-b border-border transition-colors {row.selected
									? 'hover:bg-surface-alt'
									: 'opacity-40'}"
							>
								<td class="py-2 px-3">
									<input
										type="checkbox"
										checked={row.selected}
										disabled={!row.isValid}
										onchange={() => toggleRow(i)}
										class="rounded"
									/>
								</td>
								<td class="py-2 px-3 whitespace-nowrap">{row.date}</td>
								<td class="py-2 px-3">
									<span
										class="px-1.5 py-0.5 rounded text-xs font-medium {row.transactionType ===
										'Buy'
											? 'bg-green-100 text-green-700'
											: 'bg-red-100 text-red-700'}"
									>
										{row.transactionType}
									</span>
								</td>
								<td class="py-2 px-3">
									<span class="font-semibold">{eff.symbol}</span>
									{#if wasRemapped}
										<span
											class="text-xs text-text-muted ml-1 line-through"
											title="Original CSV symbol">{row.symbol}</span
										>
									{/if}
								</td>
								<td class="py-2 px-3 text-text-muted truncate max-w-[200px]">{eff.name}</td>
								<td class="py-2 px-3">
									<span
										class="px-1.5 py-0.5 rounded text-xs {typeBadgeColor(eff.assetType)}"
										>{eff.assetType}</span
									>
								</td>
								<td class="py-2 px-3 text-right">{row.quantity}</td>
								<td class="py-2 px-3 text-right"
									>{formatCurrency(row.pricePerUnit, row.nativeCurrency)}</td
								>
								<td class="py-2 px-3 text-right font-medium"
									>{formatCurrency(
										row.quantity * row.pricePerUnit,
										row.nativeCurrency
									)}</td
								>
								<td class="py-2 px-3 text-right text-text-muted"
									>{row.commission > 0
										? formatCurrency(row.commission, row.nativeCurrency)
										: '—'}</td
								>
							</tr>
						{/each}
					</tbody>
				</table>
			</div>
		</div>

		<!-- Step 3: Importing -->
	{:else if step === 'importing'}
		<div class="bg-surface rounded-xl border border-border p-12">
			<div class="flex flex-col items-center gap-4">
				<div
					class="w-10 h-10 border-4 border-primary border-t-transparent rounded-full animate-spin"
				></div>
				<p class="text-lg font-medium">Importing transactions...</p>
				<p class="text-sm text-text-muted">This may take a moment.</p>
			</div>
		</div>

		<!-- Step 4: Done -->
	{:else if step === 'done' && result}
		<div class="bg-surface rounded-xl border border-border p-6">
			<div class="text-center mb-6">
				<div
					class="w-14 h-14 mx-auto mb-4 rounded-full bg-green-100 flex items-center justify-center"
				>
					<svg
						class="w-7 h-7 text-green-600"
						fill="none"
						viewBox="0 0 24 24"
						stroke="currentColor"
						stroke-width="2"
					>
						<path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7" />
					</svg>
				</div>
				<h2 class="text-xl font-bold">Import Complete</h2>
				<p class="text-text-muted mt-1">
					Successfully imported {result.importedCount} transaction{result.importedCount !== 1
						? 's'
						: ''}{#if result.cashImportedCount > 0}
						and {result.cashImportedCount} cash transaction{result.cashImportedCount !== 1
							? 's'
							: ''}{/if}.
				</p>
			</div>

			{#if result.errors.length > 0}
				<div class="mb-4 px-4 py-3 bg-amber-50 border border-amber-200 rounded-lg">
					<p class="text-sm font-medium text-amber-800 mb-1">
						{result.skippedCount} transaction{result.skippedCount !== 1 ? 's' : ''} skipped:
					</p>
					<ul class="text-sm text-amber-700 list-disc pl-5">
						{#each result.errors as err}
							<li>{err}</li>
						{/each}
					</ul>
				</div>
			{/if}

			<div class="flex justify-center gap-3 mt-6">
				<button
					onclick={reset}
					class="px-4 py-2 text-sm border border-border rounded-lg hover:bg-surface-alt transition-colors"
				>
					Import More
				</button>
				<a
					href="/dashboard"
					class="px-4 py-2 text-sm bg-primary text-white rounded-lg hover:bg-primary-dark transition-colors"
				>
					Go to Dashboard
				</a>
			</div>
		</div>
	{/if}
</div>
