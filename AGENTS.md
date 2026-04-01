# AGENTS.md — Profitr

## Project Overview

Profitr is a portfolio tracker web app for stocks, ETFs, and ETCs with multi-currency P&L monitoring. Users sign in with Google, search for tickers, record buy/sell transactions, track dividends, and see live profit/loss in their preferred currency.

**Live at [profitr.je.mk](https://profitr.je.mk/)**

## Architecture

- **Backend**: .NET 10 Minimal API (`backend/Profitr.Api/`)
- **Frontend**: Svelte 5 / SvelteKit (`frontend/`)
- **Database**: SQLite via EF Core (file: `backend/Profitr.Api/profitr.db`)
- **External APIs**: Yahoo Finance (market data), Frankfurter API (FX rates from ECB)

## Key Files

### Backend (`backend/Profitr.Api/`)
| Path | Purpose |
|---|---|
| `Program.cs` | App config, DI, middleware, endpoint mapping |
| `Data/Entities/` | EF Core entities: User, Portfolio, Transaction, Dividend, CashTransaction |
| `Data/ProfitrDbContext.cs` | DbContext with model configuration |
| `Data/DatabaseMigrator.cs` | Lightweight SQLite migration runner (see Database Migrations section) |
| `Endpoints/` | Minimal API endpoint groups (Auth, Portfolio, Transaction, Dividend, Cash, Market, Fx) |
| `Services/YahooFinanceService.cs` | Yahoo Finance HTTP client — search, quote, chart, historical price |
| `Services/FxService.cs` | Frankfurter API client — latest/historical FX rates, 30 currencies |
| `Services/PnLService.cs` | Core P&L engine — position aggregation, multi-currency conversion, portfolio history |
| `Models/` | DTOs for API requests/responses |
| `appsettings.json` | Config (connection string, Google OAuth placeholder) |
| `appsettings.Development.json` | Real Google OAuth credentials (gitignored) |
| `appsettings.Production.json` | Production config on deploy VM (gitignored, placed in `bin/publish/`) |

### Frontend (`frontend/src/`)
| Path | Purpose |
|---|---|
| `lib/api/client.ts` | Typed API client with all endpoints + TypeScript interfaces |
| `lib/stores/auth.svelte.ts` | Auth state (Svelte 5 runes) |
| `lib/stores/portfolio.svelte.ts` | Portfolio/transaction/dividend/cash state |
| `lib/stores/privacy.svelte.ts` | Privacy mode toggle — persisted in `localStorage`, pure frontend preference |
| `lib/components/` | Navbar, PortfolioSwitcher, PortfolioChart, PositionsTable, TickerSearch, TransactionList |
| `lib/utils/format.ts` | Currency/percent/date formatting helpers |
| `routes/+page.svelte` | Landing page with Google sign-in |
| `routes/dashboard/+page.svelte` | Main dashboard — summary cards, chart, positions/transactions/dividends/cash tabs |
| `routes/portfolio/[id]/add/+page.svelte` | Add buy/sell transaction with ticker search |
| `routes/portfolio/[id]/dividend/+page.svelte` | Record dividend payment |
| `routes/portfolio/[id]/cash/+page.svelte` | Record cash deposit or withdrawal |
| `routes/settings/+page.svelte` | Display currency selector (30 currencies), privacy mode toggle, portfolio management |

### Deployment
| Path | Purpose |
|---|---|
| `deploy.sh` | VM deploy script — git pull, build frontend, publish backend, restart service |
| `.github/workflows/deploy.yml` | GitHub Actions workflow — SSHs into VM and runs `deploy.sh` on push to `main` |

### Tests (`backend/Profitr.Tests/`)
| Path | Purpose |
|---|---|
| `Services/PnLServiceTests.cs` | P&L calculation: single buy, partial sell, multi-currency, fractional shares, etc. |
| `Services/FxServiceTests.cs` | FX service: same-currency bypass, cache hits, live API |
| `Endpoints/TestWebAppFactory.cs` | Test harness with in-memory SQLite + fake auth |
| `Endpoints/PortfolioEndpointTests.cs` | Portfolio CRUD API tests |
| `Endpoints/TransactionEndpointTests.cs` | Transaction API tests (buy, sell validation, CRUD) |
| `Endpoints/MarketEndpointTests.cs` | Market data + FX endpoint tests |
| `Data/DatabaseMigratorTests.cs` | Migration system: new DB, existing DB upgrade, idempotent re-runs |

## How to Run

### Rebuild backend after changes:
```
cd backend/Profitr.Api && dotnet build
```
- Backend URL: http://localhost:5000 (also serves the built frontend from `wwwroot/`)

### Rebuild frontend into backend wwwroot:
```
cd frontend && npm run build
```

### Run tests:
```
cd backend && dotnet test
```

### After making changes:
```
git add -A && git commit -m "description of changes" && git push
```
Pushing to `main` triggers auto-deployment to production via GitHub Actions.

## Deployment

The app is hosted on a VM and auto-deploys on every push to `main`.

**How it works:**
1. Push to `main` triggers `.github/workflows/deploy.yml`
2. GitHub Actions (cloud runner) SSHs into the production VM
3. Runs `deploy.sh`: `git pull` → `npm ci && npm run build` → `dotnet publish` → `systemctl --user restart profitr`
4. The .NET app runs as a **systemd user service** (`~/.config/systemd/user/profitr.service`), serving both the API and the static frontend

**GitHub Secrets required:**
- `VM_HOST` — Production VM IP
- `VM_USERNAME` — SSH username
- `VM_SSH_KEY` — Dedicated deploy SSH private key

**Production config:** `appsettings.Production.json` lives on the VM in the publish directory (not in the repo). It contains Google OAuth credentials and any production overrides.

## Database Migrations

The app uses a lightweight migration system (`Data/DatabaseMigrator.cs`) instead of EF Core Migrations.

**How it works:**
- On startup, `EnsureCreatedAsync()` creates the full schema for **new** databases
- Then the migrator checks a `__Migrations` tracking table and applies any pending SQL migrations for **existing** databases
- Each migration runs at most once and is recorded in `__Migrations`

**To add a new migration:**
1. Open `backend/Profitr.Api/Data/DatabaseMigrator.cs`
2. Add a new entry to the `Migrations` list with a sequential name and SQL:
   ```csharp
   ("002_AddSomeFeature", """
       CREATE TABLE IF NOT EXISTS ...;
       ALTER TABLE ... ADD COLUMN ...;
   """),
   ```
3. Use `CREATE TABLE IF NOT EXISTS` / `CREATE INDEX IF NOT EXISTS` (safe for both new and existing DBs)
4. **Never modify or reorder existing migrations** — they are immutable once shipped
5. The new table/column must also be added to the EF model (`DbContext`, entities) so new databases get the full schema via `EnsureCreated`

**Never delete `profitr.db`** — it contains user data. The migration system handles schema evolution.

## Key Design Decisions

1. **Positions and cash are computed, not stored** — Net quantity and average cost are derived from the Transaction table. Cash balance is computed as: `+deposits −withdrawals −buys +sells +dividends` (Option A / implicit cash). Negative cash is valid (margin). No separate Position or balance entity.
2. **Multi-currency P&L** — Each instrument has a `nativeCurrency` from Yahoo Finance. The user has a `displayCurrency` preference. Cost basis uses historical FX rates at each transaction date; current value uses latest FX rate. This captures both stock price and currency effects.
3. **Caching** — Yahoo quotes cached 60s, search results 5min, chart data 24h, FX latest 1h, FX historical 24h. All via `IMemoryCache`.
4. **Static frontend build** — SvelteKit builds to `backend/Profitr.Api/wwwroot/` via `@sveltejs/adapter-static`. The .NET app serves it with `UseStaticFiles` + SPA fallback.
5. **Auth** — Google OIDC → ASP.NET cookie auth. Callback path is `/signin-google`. API returns 401 for unauthenticated requests to `/api/*`.
6. **Privacy mode** — A purely client-side preference (`localStorage`) that hides all absolute monetary amounts (portfolio value, cost basis, position size/value, P&L in currency, dividends) and switches the portfolio chart to percentage growth from the first data point. Performance metrics (P&L %, chart trend) remain visible. Toggled via a quick-access button in the Navbar or the full toggle in Settings. No backend involvement — nothing is stored server-side.

## Google OAuth Setup

Credentials are in `appsettings.Development.json` (gitignored) for local dev, and `appsettings.Production.json` on the deploy VM.

Authorized redirect URIs in Google Cloud Console:
- Local: `http://localhost:5000/signin-google`
- Production: `https://profitr.je.mk/signin-google`

## What's Not Yet Implemented (from PLAN.md)

- Market hours awareness (open/closed indicators)
- Price alerts / notifications
- Data export (CSV/PDF)
- Mobile-specific responsive polish
- Toast notifications for actions
