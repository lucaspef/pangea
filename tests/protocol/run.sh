#!/bin/bash
# Testes de protocolo do emulador Python (cliente falso que decodifica como o cliente real) contra o SERVIDOR C#,
# em portas privadas (padrão 39000 web, 39001 login, 39002 game) e no banco de teste. Não toca no emulador do usuário.
#   bash tests/protocol/run.sh [porta-base]
set -u
cd "$(dirname "$0")/../.."
ROOT=$(pwd)
EMU=${EMU:-/root/pangya-server-work/emu}
BASE=${1:-39000}
DLL=src/Pangya.Server/bin/Release/net10.0/Pangya.Server.dll
[ -f "$DLL" ] || dotnet build src/Pangya.Server -c Release -v q >/dev/null

CONN=$(grep -o '"ConnectionString": *"[^"]*"' config/test.json | sed 's/.*: *"\(.*\)"/\1/')
CFG=$(mktemp --suffix=.json)
LOG=$(mktemp --suffix=.log)
trap 'kill $PID 2>/dev/null; rm -f "$CFG"' EXIT
cat > "$CFG" <<EOF
{
  "Run": ["web", "login", "game"],
  "Network": { "BindIp": "127.0.0.1", "PublicIp": "127.0.0.1" },
  "Database": { "ConnectionString": "$CONN" },
  "Web": { "Port": $BASE },
  "Login": { "Ports": [$((BASE + 1))] },
  "Game": { "Id": $((BASE + 2)), "Name": "Protocolo", "Port": $((BASE + 2)) },
  "Logging": { "Level": "Debug", "Dir": "" },
  "Data": { "IffPath": "$ROOT/data/pangya.iff" }
}
EOF

FAIL=0
run() { # nome, comando...
  local name=$1; shift
  if "$@"; then echo "--- OK: $name"; else echo "--- FALHOU: $name"; FAIL=1; fi
}

su postgres -c "psql -q -d pangya_test -c 'delete from servers'" 2>/dev/null
USER=tester$BASE
dotnet "$DLL" --config "$CFG" account-create "$USER" x "Tester$BASE" > /dev/null 2>&1   # pode já existir
dotnet "$DLL" --config "$CFG" > "$LOG" 2>&1 &
PID=$!
for _ in $(seq 50); do ss -ltn | grep -q ":$((BASE + 2)) " && break; sleep 0.2; done
sleep 0.5      # primeiro heartbeat no registro

cd "$EMU/test"
run "fakeclient: web -> login -> lista -> game -> 0x42 -> canais -> lobby" \
  env EMU_HTTP=$BASE EMU_LOGIN=$((BASE + 1)) python3 fakeclient.py 127.0.0.1 "$USER"

if [ $FAIL -ne 0 ]; then echo "--- log do servidor:"; grep -v DEBUG "$LOG" | tail -30; fi
rm -f "$LOG"
exit $FAIL
