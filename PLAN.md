# Profitr — Portfolio Tracker

## Overview

A web application to track stock/ETF/ETC portfolios with multi-currency profit/loss monitoring.

---

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│              FRONTEND — Svelte 5 (SvelteKit)             │
│                                                           │
│  Landing ─ Dashboard ─ Portfolio ─ Add/Sell ─ Settings   │
│                                                           │
│  Lightweight Charts (TradingView) for financial charts   │
│  Periodic fetch every 60s for live quotes                │
├──────────────────────────────────────────────────────────┤
│              BACKEND — .NET 9 Web API                    │
│                                                           │
│  /api/auth/*        → Google OAuth (OIDC)                │
│  /api/portfolios/*  → Portfolio & Position CRUD          │
│  /api/market/*      → Search, Quote, Chart proxies       │
│  /api/fx/*          → Currency exchange rates            │
├──────────────────────────────────────────────────────────┤
│              DATA                                         │
│                                                           │
│  SQLite via EF Core    In-memory cache (IMemoryCache)    │
├──────────────────────────────────────────────────────────┤
│              EXTERNAL APIS                                │
│                                                           │
│  Yahoo Finance (unofficial, via HTTP)                    │
│  Frankfurter API (ECB exchange rates, free, no key)      │
└──────────────────────────────────────────────────────────┘
```

---

## Tech Stack

| Layer            | Technology                        | Why                                                    |
| ---------------- | --------------------------------- | ------------------------------------------------------ |
| **Backend**      | .NET 9 Minimal API                | User requirement. Fast, great tooling.                 |
| **ORM**          | EF Core 9 + SQLite                | Code-first, migrations, easy local dev.                |
| **Auth**         | ASP.NET Cookie Auth + Google OIDC | Built-in .NET support, no extra libs.                  |
| **Frontend**     | Svelte 5 (SvelteKit)             | Simpler than React, less boilerplate, fast.            |
| **Charts**       | Lightweight Charts (TradingView) | Purpose-built for financial data, small bundle.        |
| **UI**           | Tailwind CSS 4                    | Utility-first, fast styling.                           |
| **Market Data**  | Yahoo Finance v8 (HTTP)           | Free, global coverage, returns currency per instrument.|
| **FX Rates**     | Frankfurter API                   | Free, no key, ECB-backed, 30 currencies.              |
| **Caching**      | IMemoryCache (.NET)               | Quotes: 60s TTL. FX latest: 1h. FX historical: 24h.  |

---

## Data Model

### User
- Id (string, CUID)
- Email (unique)
- Name
- AvatarUrl
- DisplayCurrency (default "EUR", one of 30 Frankfurter currencies)
- GoogleSubjectId (for OAuth mapping)
- CreatedAt

### Portfolio
- Id (GUID)
- UserId → User
- Name (default "My Portfolio")
- IsDefault (bool) — first portfolio created is the default
- CreatedAt

### Transaction
Represents a BUY or SELL event. Positions are computed from transactions.

- Id (GUID)
- PortfolioId → Portfolio
- Type (BUY / SELL)
- Symbol (e.g. "AAPL", "SIE.DE")
- InstrumentName (e.g. "Apple Inc.")
- AssetType (EQUITY / ETF / ETC)
- Quantity (decimal, supports fractional)
- PricePerUnit (decimal, in native currency)
- NativeCurrency (e.g. "USD" — from Yahoo)
- TransactionDate (DateTime)
- Notes (optional string)
- CreatedAt

### Dividend
- Id (GUID)
- PortfolioId → Portfolio
- Symbol
- AmountPerShare (decimal, in native currency)
- NativeCurrency
- ExDate (DateTime)
- PayDate (DateTime)
- Notes (optional)
- CreatedAt

### Computed: Position (not stored, derived from Transactions)
A "position" is the aggregate of all BUY/SELL transactions for a symbol within a portfolio:
- Symbol
- InstrumentName
- AssetType
- NativeCurrency
- NetQuantity = Σ(BUY quantities) − Σ(SELL quantities)
- AverageCostBasis = weighted average of BUY prices
- TotalInvested = Σ(BUY price × qty)
- TotalReturned = Σ(SELL price × qty)
- TotalDividends = Σ(dividend × shares held at ex-date)

---

## Multi-Currency P&L Logic

Every instrument has a **native currency** (from Yahoo Finance).
The user has a **display currency** preference.

### Conversion formula:
```
Value in display currency = Value in native currency × FX rate(native → display)
```

### On adding a BUY:
```
Buy 10× AAPL at $150 on 2024-06-15 (native = USD)
FX rate on 2024-06-15: USD→EUR = 0.923 (from Frankfurter)
Cost in EUR: 10 × 150 × 0.923 = €1,384.50
```

### On viewing portfolio (display = EUR):
```
Current AAPL: $195, current USD→EUR = 0.871
Value: 10 × 195 × 0.871 = €1,698.45
P&L: €1,698.45 − €1,384.50 = +€313.95 (+22.7%)
```

This captures both stock price movement AND currency effects.

---

## API Endpoints

### Auth
| Method | Route              | Description                     |
| ------ | ------------------ | ------------------------------- |
| GET    | /api/auth/login    | Redirects to Google OAuth       |
| GET    | /api/auth/callback | Handles Google callback         |
| GET    | /api/auth/me       | Returns current user or 401     |
| POST   | /api/auth/logout   | Signs out, clears cookie        |

### Portfolios
| Method | Route                                   | Description                      |
| ------ | --------------------------------------- | -------------------------------- |
| GET    | /api/portfolios                         | List user's portfolios           |
| POST   | /api/portfolios                         | Create new portfolio             |
| PUT    | /api/portfolios/{id}                    | Update name                      |
| DELETE | /api/portfolios/{id}                    | Delete portfolio (not default)   |
| GET    | /api/portfolios/{id}/positions          | Computed positions with P&L      |
| GET    | /api/portfolios/{id}/summary            | Total value, P&L, chart data     |

### Transactions
| Method | Route                                   | Description                      |
| ------ | --------------------------------------- | -------------------------------- |
| GET    | /api/portfolios/{id}/transactions       | List transactions                |
| POST   | /api/portfolios/{id}/transactions       | Add BUY or SELL                  |
| PUT    | /api/transactions/{id}                  | Edit transaction                 |
| DELETE | /api/transactions/{id}                  | Delete transaction               |

### Dividends
| Method | Route                                   | Description                      |
| ------ | --------------------------------------- | -------------------------------- |
| GET    | /api/portfolios/{id}/dividends          | List dividends                   |
| POST   | /api/portfolios/{id}/dividends          | Record dividend                  |
| DELETE | /api/dividends/{id}                     | Delete dividend                  |

### Market Data (proxy with caching)
| Method | Route                          | Description                              |
| ------ | ------------------------------ | ---------------------------------------- |
| GET    | /api/market/search?q=          | Ticker search (Yahoo)                    |
| GET    | /api/market/quote?symbols=     | Current quotes (Yahoo, cached 60s)       |
| GET    | /api/market/chart/{symbol}     | Historical OHLCV (Yahoo, cached 24h)     |
| GET    | /api/market/history-price      | Close price for symbol on a date         |

### FX Rates
| Method | Route                          | Description                              |
| ------ | ------------------------------ | ---------------------------------------- |
| GET    | /api/fx/latest?from=&to=       | Latest rate (Frankfurter, cached 1h)     |
| GET    | /api/fx/historical?date=&from= | Rate on specific date (cached forever)   |
| GET    | /api/fx/currencies             | List of 30 supported currencies          |

---

## Pages

| Page             | Route                       | Description                                    |
| ---------------- | --------------------------- | ---------------------------------------------- |
| Landing          | /                           | Hero, features, Google sign-in                 |
| Dashboard        | /dashboard                  | Active portfolio summary, quick P&L, chart     |
| Portfolio        | /portfolio/{id}             | Positions table, P&L chart, transactions list  |
| Add Transaction  | /portfolio/{id}/add         | Search ticker → enter details → save           |
| Record Dividend  | /portfolio/{id}/dividend    | Select holding → enter dividend details        |
| Settings         | /settings                   | Display currency (30 options), account info    |

### Portfolio Switcher
- Account widget in the top-right (avatar + name)
- Dropdown shows list of portfolios with radio-style selection
- "New Portfolio" option at the bottom
- Switching sets the active portfolio and redirects to dashboard

---

## Caching Strategy

| Data                  | Cache TTL       | Storage          |
| --------------------- | --------------- | ---------------- |
| Stock quotes          | 60 seconds      | IMemoryCache     |
| Ticker search results | 5 minutes       | IMemoryCache     |
| Historical chart data | 24 hours        | IMemoryCache     |
| FX latest rate        | 1 hour          | IMemoryCache     |
| FX historical rate    | 24 hours        | IMemoryCache     |

---

## Implementation Phases

### Phase 1 — Project Scaffolding & Auth
- .NET 9 Web API project with EF Core + SQLite
- Svelte 5 (SvelteKit) frontend project
- Google OAuth flow (backend handles OIDC, sets cookie)
- Basic layout shell with auth state
- **Git commit**

### Phase 2 — Market Data & FX Services
- Yahoo Finance HTTP client (search, quote, chart, history-price)
- Frankfurter HTTP client (latest, historical, currencies)
- Caching layer with IMemoryCache
- API routes for market data and FX
- **Git commit**

### Phase 3 — Portfolio & Transaction CRUD
- EF Core models and migrations
- Portfolio CRUD (create, list, rename, delete, switch default)
- Transaction CRUD (buy, sell, edit, delete)
- Dividend recording
- Position computation from transactions
- **Git commit**

### Phase 4 — P&L Engine
- Multi-currency P&L calculations
- Portfolio summary (total value, total P&L, per-position)
- Historical portfolio value computation for charts
- **Git commit**

### Phase 5 — Frontend Dashboard & Charts
- Dashboard with portfolio summary cards
- Positions table with live P&L
- Portfolio value chart (Lightweight Charts)
- Add transaction flow with ticker search
- Live refresh (60s polling)
- **Git commit**

### Phase 6 — Polish & Tests
- Portfolio switcher in account widget
- Settings page (currency selector)
- Loading/error/empty states
- Responsive design
- Unit tests for P&L engine
- Integration tests for API endpoints
- **Git commit**

---

## Testing Strategy

### Backend (.NET xUnit)
- **P&L calculation engine** — core math with various currency combos
- **Position aggregation** — BUY/SELL netting, average cost basis
- **API integration tests** — WebApplicationFactory for endpoint testing
- **Market data parsing** — Yahoo Finance response handling

### Frontend (Vitest + Testing Library)
- **P&L display formatting** — currency formatting, percentage calc
- **Portfolio switcher logic**
- **Transaction form validation**

---

## Project Structure

```
profitr/
├── PLAN.md
├── README.md
├── backend/
│   ├── Profitr.Api/              — Main Web API project
│   │   ├── Program.cs
│   │   ├── Endpoints/            — Minimal API endpoint groups
│   │   ├── Services/             — Yahoo, Frankfurter, P&L engine
│   │   ├── Data/                 — EF Core DbContext, entities
│   │   └── Models/               — DTOs, request/response models
│   └── Profitr.Tests/            — xUnit test project
├── frontend/
│   ├── src/
│   │   ├── routes/               — SvelteKit pages
│   │   ├── lib/                  — Components, stores, utils
│   │   └── app.html
│   ├── package.json
│   └── svelte.config.js
└── .gitignore
```
