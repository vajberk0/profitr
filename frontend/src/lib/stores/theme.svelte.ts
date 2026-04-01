const STORAGE_KEY = 'profitr_theme';

export type Theme = 'light' | 'dark' | 'system';

class ThemeStore {
	current = $state<Theme>('system');
	isDark = $state(false);

	init() {
		try {
			const saved = localStorage.getItem(STORAGE_KEY) as Theme | null;
			if (saved && ['light', 'dark', 'system'].includes(saved)) {
				this.current = saved;
			}
		} catch {}
		this.apply();

		// Listen for system preference changes
		if (typeof window !== 'undefined') {
			window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
				if (this.current === 'system') this.apply();
			});
		}
	}

	set(theme: Theme) {
		this.current = theme;
		try {
			localStorage.setItem(STORAGE_KEY, theme);
		} catch {}
		this.apply();
	}

	/** Cycle through: system → dark → light → system */
	cycle() {
		const next: Record<Theme, Theme> = { system: 'dark', dark: 'light', light: 'system' };
		this.set(next[this.current]);
	}

	apply() {
		if (typeof document === 'undefined') return;
		const dark =
			this.current === 'dark' ||
			(this.current === 'system' &&
				window.matchMedia('(prefers-color-scheme: dark)').matches);
		this.isDark = dark;
		document.documentElement.classList.toggle('dark', dark);
	}
}

export const themeStore = new ThemeStore();
