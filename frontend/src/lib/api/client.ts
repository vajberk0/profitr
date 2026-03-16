const BASE = '';

async function request<T>(url: string, options?: RequestInit): Promise<T> {
	const res = await fetch(`${BASE}${url}`, {
		credentials: 'include',
		headers: { 'Content-Type': 'application/json', ...options?.headers },
		...options
	});
	if (res.status === 401) {
		window.location.href = '/';
		throw new Error('Unauthorized');
	}
	if (!res.ok) {
		const text = await res.text();
		throw new Error(text || `HTTP ${res.status}`);
	}
	if (res.status === 204) return undefined as T;
	return res.json();
}

// Auth
export const auth = {
	me: () => request<UserInfo>('/api/auth/me'),
	logout: () => request<void>('/api/auth/logout', { method: 'POST' }),
	updateSettings: (displayCurrency: string) =>
		request<UserInfo>('/api/auth/settings', {
			method: 'PUT',
			body: JSON.stringify({ displayCurrency })
		})
};

// Portfolios
export const portfolios = {
	list: () => request<Portfolio[]>('/api/portfolios'),
	create: (name: string) =>
		request<Portfolio>('/api/portfolios', { method: 'POST', body: JSON.stringify({ name }) }),
	update: (id: string, name: string) =>
		request<Portfolio>(`/api/portfolios/${id}`, { method: 'PUT', body: JSON.stringify({ name }) }),
	delete: (id: string) => request<void>(`/api/portfolios/${id}`, { method: 'DELETE' }),
	setDefault: (id: string) =>
		request<void>(`/api/portfolios/${id}/set-default`, { method: 'POST' }),
	summary: (id: string) => request<PortfolioSummary>(`/api/portfolios/${id}/summary`),
	history: (id: string, range = '1y') =>
		request<ChartDataPoint[]>(`/api/portfolios/${id}/history?range=${range}`)
};

// Transactions
export const transactions = {
	list: (portfolioId: string) =>
		request<Transaction[]>(`/api/portfolios/${portfolioId}/transactions`),
	create: (portfolioId: string, data: CreateTransactionRequest) =>
		request<Transaction>(`/api/portfolios/${portfolioId}/transactions`, {
			method: 'POST',
			body: JSON.stringify(data)
		}),
	update: (id: string, data: UpdateTransactionRequest) =>
		request<Transaction>(`/api/transactions/${id}`, {
			method: 'PUT',
			body: JSON.stringify(data)
		}),
	delete: (id: string) => request<void>(`/api/transactions/${id}`, { method: 'DELETE' })
};

// Dividends
export const dividends = {
	list: (portfolioId: string) =>
		request<Dividend[]>(`/api/portfolios/${portfolioId}/dividends`),
	create: (portfolioId: string, data: CreateDividendRequest) =>
		request<Dividend>(`/api/portfolios/${portfolioId}/dividends`, {
			method: 'POST',
			body: JSON.stringify(data)
		}),
	delete: (id: string) => request<void>(`/api/dividends/${id}`, { method: 'DELETE' })
};

// Market data
export const market = {
	search: (q: string) => request<TickerSearchResult[]>(`/api/market/search?q=${encodeURIComponent(q)}`),
	quote: (symbols: string[]) =>
		request<Record<string, QuoteResult>>(`/api/market/quote?symbols=${symbols.join(',')}`),
	chart: (symbol: string, range = '1y') =>
		request<ChartResult>(`/api/market/chart/${symbol}?range=${range}`),
	historyPrice: (symbol: string, date: string) =>
		request<HistoryPriceResult>(`/api/market/history-price?symbol=${symbol}&date=${date}`)
};

// FX
export const fx = {
	currencies: () => request<CurrencyInfo[]>('/api/fx/currencies'),
	latest: (from: string, to: string) =>
		request<{ from: string; to: string; rate: number }>(`/api/fx/latest?from=${from}&to=${to}`)
};

// Types
export interface UserInfo {
	id: string;
	email: string;
	name: string | null;
	avatarUrl: string | null;
	displayCurrency: string;
}

export interface Portfolio {
	id: string;
	name: string;
	isDefault: boolean;
	createdAt: string;
	positionCount: number;
}

export interface Position {
	symbol: string;
	instrumentName: string;
	assetType: string;
	nativeCurrency: string;
	quantity: number;
	averageCostBasis: number;
	totalInvested: number;
	currentPrice: number;
	currentValue: number;
	pnL: number;
	pnLPercent: number;
	totalDividends: number;
	totalInvestedDisplay: number;
	currentValueDisplay: number;
	pnLDisplay: number;
	pnLPercentDisplay: number;
	totalDividendsDisplay: number;
}

export interface PortfolioSummary {
	id: string;
	name: string;
	isDefault: boolean;
	displayCurrency: string;
	totalValue: number;
	totalCostBasis: number;
	totalPnL: number;
	totalPnLPercent: number;
	totalDividends: number;
	positions: Position[];
}

export interface Transaction {
	id: string;
	type: string;
	symbol: string;
	instrumentName: string;
	assetType: string;
	quantity: number;
	pricePerUnit: number;
	nativeCurrency: string;
	transactionDate: string;
	notes: string | null;
	createdAt: string;
}

export interface CreateTransactionRequest {
	type: string;
	symbol: string;
	instrumentName: string;
	assetType: string;
	quantity: number;
	pricePerUnit: number;
	nativeCurrency: string;
	transactionDate: string;
	notes?: string;
}

export interface UpdateTransactionRequest {
	quantity: number;
	pricePerUnit: number;
	transactionDate: string;
	notes?: string;
}

export interface Dividend {
	id: string;
	symbol: string;
	amountPerShare: number;
	nativeCurrency: string;
	exDate: string;
	payDate: string;
	notes: string | null;
	createdAt: string;
}

export interface CreateDividendRequest {
	symbol: string;
	amountPerShare: number;
	nativeCurrency: string;
	exDate: string;
	payDate: string;
	notes?: string;
}

export interface TickerSearchResult {
	symbol: string;
	name: string;
	type: string;
	exchange: string;
}

export interface QuoteResult {
	symbol: string;
	name: string;
	price: number;
	change: number;
	changePercent: number;
	currency: string;
	exchange: string;
	assetType: string;
}

export interface ChartResult {
	symbol: string;
	currency: string;
	points: { date: string; open: number; high: number; low: number; close: number; volume: number }[];
}

export interface ChartDataPoint {
	date: string;
	value: number;
}

export interface HistoryPriceResult {
	symbol: string;
	date: string;
	closePrice: number;
	currency: string;
}

export interface CurrencyInfo {
	code: string;
	name: string;
}
