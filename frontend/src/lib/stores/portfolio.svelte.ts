import {
	portfolios as portfolioApi,
	transactions as txnApi,
	dividends as divApi,
	cash as cashApi,
	type Portfolio,
	type PortfolioSummary,
	type Transaction,
	type Dividend,
	type CashTransaction,
	type ChartDataPoint
} from '$lib/api/client';

class PortfolioStore {
	portfolios = $state<Portfolio[]>([]);
	activePortfolio = $state<Portfolio | null>(null);
	summary = $state<PortfolioSummary | null>(null);
	transactions = $state<Transaction[]>([]);
	dividendsList = $state<Dividend[]>([]);
	cashTransactions = $state<CashTransaction[]>([]);
	history = $state<ChartDataPoint[]>([]);
	loading = $state(false);
	historyRange = $state('1y');

	async loadPortfolios() {
		this.portfolios = await portfolioApi.list();
		// Set active to default or first
		if (!this.activePortfolio || !this.portfolios.find((p) => p.id === this.activePortfolio?.id)) {
			this.activePortfolio =
				this.portfolios.find((p) => p.isDefault) || this.portfolios[0] || null;
		}
	}

	async switchPortfolio(id: string) {
		this.activePortfolio = this.portfolios.find((p) => p.id === id) || null;
		if (this.activePortfolio) {
			await this.loadSummary();
		}
	}

	async createPortfolio(name: string) {
		const p = await portfolioApi.create(name);
		this.portfolios = [...this.portfolios, p];
		return p;
	}

	async deletePortfolio(id: string) {
		await portfolioApi.delete(id);
		this.portfolios = this.portfolios.filter((p) => p.id !== id);
		if (this.activePortfolio?.id === id) {
			this.activePortfolio =
				this.portfolios.find((p) => p.isDefault) || this.portfolios[0] || null;
		}
	}

	async setDefault(id: string) {
		await portfolioApi.setDefault(id);
		this.portfolios = this.portfolios.map((p) => ({ ...p, isDefault: p.id === id }));
		this.activePortfolio = this.portfolios.find((p) => p.id === id) || null;
	}

	async loadSummary() {
		if (!this.activePortfolio) return;
		this.loading = true;
		try {
			this.summary = await portfolioApi.summary(this.activePortfolio.id);
		} finally {
			this.loading = false;
		}
	}

	async loadHistory(range?: string) {
		if (!this.activePortfolio) return;
		if (range) this.historyRange = range;
		this.history = await portfolioApi.history(this.activePortfolio.id, this.historyRange);
	}

	async loadTransactions() {
		if (!this.activePortfolio) return;
		this.transactions = await txnApi.list(this.activePortfolio.id);
	}

	async loadDividends() {
		if (!this.activePortfolio) return;
		this.dividendsList = await divApi.list(this.activePortfolio.id);
	}

	async loadCashTransactions() {
		if (!this.activePortfolio) return;
		this.cashTransactions = await cashApi.list(this.activePortfolio.id);
	}

	async loadAll() {
		await this.loadPortfolios();
		if (this.activePortfolio) {
			await Promise.all([this.loadSummary(), this.loadHistory(), this.loadTransactions(), this.loadDividends(), this.loadCashTransactions()]);
		}
	}
}

export const portfolioStore = new PortfolioStore();
