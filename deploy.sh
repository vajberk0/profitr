#!/bin/bash
set -euo pipefail
cd ~/profitr

echo "==> Pulling latest code..."
git pull origin main

echo "==> Building frontend..."
cd frontend
npm ci
npm run build
cd ..

echo "==> Publishing backend..."
cd backend/Profitr.Api
dotnet publish -c Release -o bin/publish
cd ../..

echo "==> Restarting service..."
systemctl --user restart profitr

echo "==> Deploy complete!"
