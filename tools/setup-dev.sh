#!/bin/bash
# Prepara o ambiente de desenvolvimento no WSL (root): PostgreSQL rodando, usuário "pangya",
# bancos "pangya" e "pangya_test", e os arquivos config/pangya.json e config/test.json com a senha gerada.
# Pode ser rodado várias vezes: não recria o que já existe.
set -euo pipefail
cd "$(dirname "$0")/.."

pg_lsclusters -h | grep -q online || service postgresql start >/dev/null

if [ -f config/pangya.json ]; then
  PW=$(python3 -c 'import re,sys; print(re.search(r"Password=([^;\"]+)", open("config/pangya.json").read()).group(1))')
else
  PW=$(head -c 18 /dev/urandom | base64 | tr -dc 'A-Za-z0-9')
fi

for f in pangya test; do
  [ -f "config/$f.json" ] || sed "s/CHANGE_ME/$PW/" "config/$f.example.json" > "config/$f.json"
done
chmod 600 config/pangya.json config/test.json

psql_q() { su postgres -c "psql -qtAX -v ON_ERROR_STOP=1 -c \"$1\""; }
if [ -z "$(psql_q "select 1 from pg_roles where rolname='pangya'")" ]; then
  psql_q "create role pangya login password '$PW'"
else
  psql_q "alter role pangya password '$PW'"
fi
for db in pangya pangya_test; do
  [ -n "$(psql_q "select 1 from pg_database where datname='$db'")" ] || psql_q "create database $db owner pangya"
done
# dados do jogo: o pangya.iff que o cliente usa (o do último pak de correção; tools/sync-iff.py só lê a pasta do
# cliente). Sem a pasta do cliente, a cópia do emulador Python (extraída do projectg642.pak, sem as correções).
GAME_DIR=${GAME_DIR:-/mnt/e/dev/pangya-test/KR642}
IFF_SOURCE=${IFF_SOURCE:-/root/pangya-server-work/emu/data/pangya.iff}
mkdir -p data
if [ -d "$GAME_DIR" ]; then python3 tools/sync-iff.py "$GAME_DIR" data/pangya.iff
else [ -f data/pangya.iff ] || cp "$IFF_SOURCE" data/pangya.iff; fi

echo "setup-dev: ok (bancos pangya e pangya_test; config em config/pangya.json e config/test.json)"
