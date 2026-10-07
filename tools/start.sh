#!/bin/bash
# Compila e sobe o servidor C# em segundo plano (WSL). Log: logs/server.out (+ logs/pangya-AAAAMMDD.log).
#   bash tools/start.sh [nomes...]     ex.: bash tools/start.sh web login
# Parar: bash tools/stop.sh
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [ -f logs/server.pid ] && kill -0 "$(cat logs/server.pid)" 2>/dev/null; then
  echo "já está rodando (pid $(cat logs/server.pid))"; exit 0
fi
bash tools/setup-dev.sh >/dev/null
dotnet build src/Pangya.Server -c Release -v q -clp:NoSummary
mkdir -p logs
setsid nohup dotnet src/Pangya.Server/bin/Release/net10.0/Pangya.Server.dll --config config/pangya.json "$@" \
  > logs/server.out 2>&1 < /dev/null &
echo $! > logs/server.pid
sleep 2
cat logs/server.out
