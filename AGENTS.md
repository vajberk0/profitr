# AGENTS.md — Profitr

## Project Overview

Profitr is a portfolio tracker web app for stocks, ETFs, and ETCs with multi-currency P&L monitoring. Users sign in with Google, search for tickers, record buy/sell transactions, track dividends, and see live profit/loss in their preferred currency.

## Architecture

- **Backend**: .NET 9 Minimal API (`backend/Profitr.Api/`)
- **Frontend**: Svelte 5 / SvelteKit (`frontend/`)
- **Database**: SQLite via EF Core (file: `backend/Profitr.Api/profitr.db`)
- **External APIs**: Yahoo Finance (market data), Frankfurter API (FX rates from ECB)

## Key Files

### Backend (`backend/Profitr.Api/`)
| Path | Purpose |
|---|---|
| `Program.cs` | App config, DI, middleware, endpoint mapping |
| `Data/Entities/` | EF Core entities: User, Portfolio, Transaction, Dividend |
| `Data/ProfitrDbContext.cs` | DbContext with model configuration |
| `Endpoints/` | Minimal API endpoint groups (Auth, Portfolio, Transaction, Dividend, Market, Fx) |
| `Services/YahooFinanceService.cs` | Yahoo Finance HTTP client — search, quote, chart, historical price |
| `Services/FxService.cs` | Frankfurter API client — latest/historical FX rates, 30 currencies |
| `Services/PnLService.cs` | Core P&L engine — position aggregation, multi-currency conversion, portfolio history |
| `Models/` | DTOs for API requests/responses |
| `appsettings.json` | Config (connection string, Google OAuth placeholder) |
| `appsettings.Development.json` | Real Google OAuth credentials (gitignored) |

### Frontend (`frontend/src/`)
| Path | Purpose |
|---|---|
| `lib/api/client.ts` | Typed API client with all endpoints + TypeScript interfaces |
| `lib/stores/auth.svelte.ts` | Auth state (Svelte 5 runes) |
| `lib/stores/portfolio.svelte.ts` | Portfolio/transaction/dividend state |
| `lib/components/` | Navbar, PortfolioSwitcher, PortfolioChart, PositionsTable, TickerSearch, TransactionList |
| `lib/utils/format.ts` | Currency/percent/date formatting helpers |
| `routes/+page.svelte` | Landing page with Google sign-in |
| `routes/dashboard/+page.svelte` | Main dashboard — summary cards, chart, positions/transactions/dividends tabs |
| `routes/portfolio/[id]/add/+page.svelte` | Add buy/sell transaction with ticker search |
| `routes/portfolio/[id]/dividend/+page.svelte` | Record dividend payment |
| `routes/settings/+page.svelte` | Display currency selector (30 currencies), portfolio management |

### Tests (`backend/Profitr.Tests/`)
| Path | Purpose |
|---|---|
| `Services/PnLServiceTests.cs` | P&L calculation: single buy, partial sell, multi-currency, fractional shares, etc. |
| `Services/FxServiceTests.cs` | FX service: same-currency bypass, cache hits, live API |
| `Endpoints/TestWebAppFactory.cs` | Test harness with in-memory SQLite + fake auth |
| `Endpoints/PortfolioEndpointTests.cs` | Portfolio CRUD API tests |
| `Endpoints/TransactionEndpointTests.cs` | Transaction API tests (buy, sell validation, CRUD) |
| `Endpoints/MarketEndpointTests.cs` | Market data + FX endpoint tests |

## How to Run

### Rebuild backend after changes:
```
cd backend/Profitr.Api && dotnet build
```

### Rebuild frontend into backend wwwroot:
```
cd frontend && npm run build
```

### RUN BOTH (does `npm run build` on frontend and `dotnet run` on backend), uses `start` to open new window:
```
start runapp.bat
```

- Backend URL: http://localhost:5000 (also serves the built frontend from `wwwroot/`)

### Run tests:
```
cd backend && dotnet test
```

## Key Design Decisions

1. **Positions are computed, not stored** — Net quantity and average cost are derived from the Transaction table. No separate Position entity.
2. **Multi-currency P&L** — Each instrument has a `nativeCurrency` from Yahoo Finance. The user has a `displayCurrency` preference. Cost basis uses historical FX rates at each transaction date; current value uses latest FX rate. This captures both stock price and currency effects.
3. **Caching** — Yahoo quotes cached 60s, search results 5min, chart data 24h, FX latest 1h, FX historical 24h. All via `IMemoryCache`.
4. **Static frontend build** — SvelteKit builds to `backend/Profitr.Api/wwwroot/` via `@sveltejs/adapter-static`. The .NET app serves it with `UseStaticFiles` + SPA fallback.
5. **Auth** — Google OIDC → ASP.NET cookie auth. Callback path is `/signin-google`. API returns 401 for unauthenticated requests to `/api/*`.

## Google OAuth Setup

Credentials are in `appsettings.Development.json` (gitignored). The authorized redirect URI in Google Cloud Console must be: `http://localhost:5000/signin-google`

## What's Not Yet Implemented (from PLAN.md)

- Market hours awareness (open/closed indicators)
- Price alerts / notifications
- Data export (CSV/PDF)
- Mobile-specific responsive polish
- Toast notifications for actions
