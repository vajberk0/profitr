import { auth as authApi, type UserInfo } from '$lib/api/client';

class AuthStore {
	user = $state<UserInfo | null>(null);
	loading = $state(true);

	async load() {
		this.loading = true;
		try {
			this.user = await authApi.me();
		} catch {
			this.user = null;
		} finally {
			this.loading = false;
		}
	}

	async logout() {
		await authApi.logout();
		this.user = null;
		window.location.href = '/';
	}

	async updateCurrency(currency: string) {
		if (!this.user) return;
		this.user = await authApi.updateSettings(currency);
	}

	get isLoggedIn() {
		return this.user !== null;
	}
}

export const authStore = new AuthStore();
