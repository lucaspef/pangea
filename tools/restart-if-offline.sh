#!/bin/bash
# Reinicia o servidor de desenvolvimento só se ninguém estiver conectado (última entrada/saída do log = saída).
#   bash tools/restart-if-offline.sh
cd "$(dirname "$0")/.."
last=$(grep -E 'entrou:|fechada: cliente|servidor parando' logs/server.out 2>/dev/null | tail -1)
case "$last" in
  *entrou:*) echo "ONLINE: não reiniciei ($last)"; exit 1 ;;
esac
bash tools/stop.sh
sleep 1
bash tools/start.sh | tail -1
