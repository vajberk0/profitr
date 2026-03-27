const STORAGE_KEY = 'profitr_privacy_mode';

class PrivacyStore {
	enabled = $state(false);

	/** Call once from the root layout after the DOM is ready. */
	init() {
		try {
			this.enabled = localStorage.getItem(STORAGE_KEY) === 'true';
		} catch {
			// localStorage unavailable (SSR guard, should never happen in this SPA)
		}
	}

	set(value: boolean) {
		this.enabled = value;
		try {
			localStorage.setItem(STORAGE_KEY, String(value));
		} catch {
			// ignore
		}
	}
}

export const privacyStore = new PrivacyStore();
