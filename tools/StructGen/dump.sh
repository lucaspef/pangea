#!/bin/bash
# Gera tools/StructGen/layouts.txt: layout exato (MSVC, x86 32 bits) das structs do cliente,
# calculado pelo clang a partir dos headers do rebang (só leitura). Depois rode:
#   dotnet run --project tools/StructGen
# para regenerar o código C# (src/Pangya.Protocol.KR645/Structs.g.cs) e o teste de layout.
set -euo pipefail
cd "$(dirname "$0")"
SHARED=${REBANG:-/root/rebang}/source/shared
clang++ --target=i686-pc-windows-msvc -fsyntax-only -fms-extensions -Wno-everything \
  -I "$SHARED" -I stub -Xclang -fdump-record-layouts-complete layouts.cpp > layouts.txt
echo "layouts.txt: $(grep -c 'Dumping AST Record Layout' layouts.txt) structs"
