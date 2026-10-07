#!/bin/bash
# Para o servidor C# iniciado por tools/start.sh (SIGTERM: encerra limpo).
cd "$(dirname "$0")/.."
if [ -f logs/server.pid ] && kill "$(cat logs/server.pid)" 2>/dev/null; then echo "parado"; fi
rm -f logs/server.pid
