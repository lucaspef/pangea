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
  "Game": { "Id": $((BASE + 2)), "Name": "Protocolo", "Port": $((BASE + 2)), "BotDelaySeconds": 0.3,
            "Courses": [19, 16, 15, 14, 13, 11, 8, 10, 0, 1, 2, 3, 4, 5, 6, 7, 9] },
  "Logging": { "Level": "Debug", "Dir": "" },
  "Data": { "IffPath": "$ROOT/data/pangya.iff" },
  "NewPlayer": { "Cookie": 1000 }
}
EOF

FAIL=0
run() { # nome, comando...
  local name=$1; shift
  if "$@"; then echo "--- OK: $name"; else echo "--- FALHOU: $name"; FAIL=1; fi
}

# começa limpo: servidores registrados e as contas dos testes (podem ter sobrado de uma execução anterior),
# no banco de config/test.json (não um nome fixo)
DBNAME=$(echo "$CONN" | sed -n 's/.*Database=\([^;]*\).*/\1/p')
su postgres -c "psql -q -d ${DBNAME:-pangya_test} -c \"delete from servers; delete from guild_history; delete from guild_members; delete from guilds; delete from accounts where login like '%$BASE'\"" 2>/dev/null
USER=tester$BASE
for u in tester roomA roomB golf shop tour wiz; do   # contas dos testes (senha "x", como os scripts mandam); podem já existir
  dotnet "$DLL" --config "$CFG" account-create "$u$BASE" x "N$u$BASE" > /dev/null 2>&1
done
dotnet "$DLL" --config "$CFG" > "$LOG" 2>&1 &
PID=$!
for _ in $(seq 50); do ss -ltn | grep -q ":$((BASE + 2)) " && break; sleep 0.2; done
sleep 0.5      # primeiro heartbeat no registro

cd "$EMU/test"
run "fakeclient: web -> login -> lista -> game -> 0x42 -> canais -> lobby" \
  env EMU_HTTP=$BASE EMU_LOGIN=$((BASE + 1)) python3 fakeclient.py 127.0.0.1 "$USER"

run "test_room.py: lista de salas, criar/entrar/sair, chat, config, pronto, bot, início (0x74/0x50)" \
  env EMU_HTTP=$BASE EMU_LOGIN=$((BASE + 1)) python3 test_room.py "$BASE"
# test_ingame.py: a parte 1 testa o módulo Python em memória; a parte 2 é a partida de ponta a ponta contra o C#
run "test_ingame.py: partida completa com bot (3 buracos, tacada especial, resultado cifrado, fim de jogo)" \
  env EMU_HTTP=$BASE EMU_LOGIN=$((BASE + 1)) python3 test_ingame.py "$BASE"

# test_modes.py e test_wizcity.py: a parte 1 testa o Python em memória; a parte 2 é de ponta a ponta contra o C#
run "test_modes.py: torneio (sala em massa, 3 buracos, bot simulado, resultado)" \
  env EMU_HTTP=$BASE EMU_LOGIN=$((BASE + 1)) python3 test_modes.py "$BASE"
run "test_wizcity.py: Wiz City (tabela de moedas/caixas no 0x50, crédito pelo 0x1C)" \
  env EMU_HTTP=$BASE EMU_LOGIN=$((BASE + 1)) python3 test_wizcity.py "$BASE"

# test_player_shop.py gera o usuário pelo horário; aqui ele é fixado na conta pré-criada "shop<base>"
run "test_player_shop.py: inventário, loja (pang/cookie, erros), equipamento e persistência ao relogar" \
  env EMU_HTTP=$BASE EMU_LOGIN=$((BASE + 1)) python3 -c "
import sys; sys.argv = ['test_player_shop.py', '$BASE']; sys.path.insert(0, '.')
import test_player_shop as t; t.USER = 'shop$BASE'; t.main()"

if [ $FAIL -ne 0 ]; then echo "--- log do servidor:"; grep -v DEBUG "$LOG" | tail -30; fi
rm -f "$LOG"
exit $FAIL
