# 📈 Profitr — Portfolio Tracker

Track your stock, ETF & ETC portfolio with real-time multi-currency profit & loss monitoring.

## Features

- **🔍 Ticker Search** — Search any stock, ETF, or ETC globally via Yahoo Finance
- **💰 Buy & Sell Tracking** — Full transaction history with timestamps
- **💱 Multi-Currency P&L** — See profits/losses in any of 30 currencies (ECB rates)
- **📊 Live Charts** — Portfolio value over time with TradingView Lightweight Charts
- **📋 Multiple Portfolios** — Switch between portfolios like tenants
- **💵 Dividend Tracking** — Record dividend payments per holding
- **🔐 Google Sign-In** — Simple OAuth authentication

## Tech Stack

| Component | Technology |
|---|---|
| Backend | .NET 9 Minimal API |
| Database | SQLite via EF Core |
| Frontend | Svelte 5 (SvelteKit) + Tailwind CSS |
| Charts | TradingView Lightweight Charts |
| Market Data | Yahoo Finance (unofficial) |
| FX Rates | Frankfurter API (ECB) |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 20+](https://nodejs.org/)
- Google OAuth credentials (see below)

## Setup

### 1. Google OAuth Credentials

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project (or use existing)
3. Navigate to **APIs & Services → Credentials**
4. Create **OAuth 2.0 Client ID** (Web application)
5. Add authorized redirect URI: `http://localhost:5000/signin-google`
6. Copy the Client ID and Client Secret

### 2. Configure the Backend

Create `backend/Profitr.Api/appsettings.Development.json`:

```json
{
  "Authentication": {
    "Google": {
      "ClientId": "YOUR_CLIENT_ID_HERE",
      "ClientSecret": "YOUR_CLIENT_SECRET_HERE"
    }
  }
}
```

### 3. Install & Build Frontend

```bash
cd frontend
npm install
npm run build     # Builds to backend/Profitr.Api/wwwroot/
```

### 4. Run the Backend

```bash
cd backend/Profitr.Api
dotnet run --urls "http://localhost:5000"
```

Then open http://localhost:5000

### Development Mode (with hot reload)

Terminal 1 — Backend:
```bash
cd backend/Profitr.Api
dotnet watch run --urls "http://localhost:5000"
```

Terminal 2 — Frontend (with HMR):
```bash
cd frontend
npm run dev    # Starts on http://localhost:5173, proxies API to :5000
```

## Running Tests

```bash
cd backend
dotnet test
```

## Project Structure

```
profitr/
├── backend/
│   ├── Profitr.Api/
│   │   ├── Data/           # EF Core entities & DbContext
│   │   ├── Endpoints/      # Minimal API endpoint groups
│   │   ├── Models/          # DTOs
│   │   ├── Services/        # Yahoo Finance, FX, P&L engine
│   │   └── Program.cs       # App configuration
│   └── Profitr.Tests/       # xUnit tests
├── frontend/
│   ├── src/
│   │   ├── lib/             # Components, stores, API client
│   │   └── routes/          # SvelteKit pages
│   └── svelte.config.js
├── PLAN.md                   # Detailed architecture plan
└── README.md
```

## API Overview

| Endpoint | Description |
|---|---|
| `GET /api/auth/login` | Google OAuth login |
| `GET /api/auth/me` | Current user info |
| `GET /api/portfolios` | List portfolios |
| `POST /api/portfolios/{id}/transactions` | Add buy/sell |
| `GET /api/portfolios/{id}/summary` | Portfolio P&L |
| `GET /api/portfolios/{id}/history?range=1y` | Chart data |
| `GET /api/market/search?q=AAPL` | Ticker search |
| `GET /api/market/quote?symbols=AAPL,MSFT` | Live quotes |
| `GET /api/fx/currencies` | 30 supported currencies |

## License

MIT
