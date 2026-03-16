export function formatCurrency(value: number, currency: string): string {
	return new Intl.NumberFormat('en-US', {
		style: 'currency',
		currency,
		minimumFractionDigits: 2,
		maximumFractionDigits: 2
	}).format(value);
}

export function formatPercent(value: number): string {
	const sign = value >= 0 ? '+' : '';
	return `${sign}${value.toFixed(2)}%`;
}

export function formatQuantity(value: number): string {
	return value % 1 === 0 ? value.toString() : value.toFixed(4);
}

export function formatDate(date: string): string {
	return new Date(date).toLocaleDateString('en-US', {
		year: 'numeric',
		month: 'short',
		day: 'numeric'
	});
}

export function pnlColor(value: number): string {
	if (value > 0) return 'text-success';
	if (value < 0) return 'text-danger';
	return 'text-text-muted';
}

export function pnlBgColor(value: number): string {
	if (value > 0) return 'bg-green-50 border-green-200';
	if (value < 0) return 'bg-red-50 border-red-200';
	return 'bg-gray-50 border-gray-200';
}
