#!/bin/bash
# Compila tudo e roda todos os testes (xUnit + testes de protocolo em tests/protocol).
# Uso no WSL: bash verify.sh        Sai com código != 0 se algo falhar.
set -uo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

FAIL=0
step() { echo; echo "=== $1"; }
check() { if [ "$1" -eq 0 ]; then echo "--- OK: $2"; else echo "--- FALHOU: $2"; FAIL=1; fi; }

step "ambiente"
bash tools/setup-dev.sh; check $? "setup-dev"

step "build"
dotnet build Pangya.slnx -c Release -warnaserror -v q -clp:NoSummary 2>&1 | grep -Ev '^\s*$|Determining projects|Restored|All projects are up-to-date'
check "${PIPESTATUS[0]}" "build"

step "testes xUnit"
dotnet test Pangya.slnx -c Release --no-build -v q 2>&1 | grep -Ev '^\s*$|^Test run for|^VSTest|^Starting test execution|^A total of'
check "${PIPESTATUS[0]}" "xUnit"

if [ -x tests/protocol/run.sh ] || [ -f tests/protocol/run.sh ]; then
  step "testes de protocolo (Python, contra o servidor C#)"
  bash tests/protocol/run.sh; check $? "protocolo"
fi

echo
if [ $FAIL -eq 0 ]; then echo "VERIFY: VERDE"; else echo "VERIFY: VERMELHO"; fi
exit $FAIL
