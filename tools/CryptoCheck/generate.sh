#!/bin/bash
# Extrai as tabelas de chave do cliente (packet.cpp) para src/Pangya.Core/Crypto/keytables.bin e gera
# tests/Pangya.Tests/Data/crypto_vectors.txt com o jrencrypt.cpp do cliente (rebang, só leitura).
# Os arquivos gerados vão para o git; o teste CipherTests confere a cifra C# contra esses vetores.
set -euo pipefail
cd "$(dirname "$0")"
ROOT=$(cd ../.. && pwd)
SRC=${REBANG:-/root/rebang}/source/client/ProjectG
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

python3 -I - "$SRC/packet.cpp" "$TMP/keytables.h" "$ROOT/src/Pangya.Core/Crypto/keytables.bin" <<'EOF'
import re, sys
src = open(sys.argv[1], encoding='latin-1').read()
i = src.index('static unsigned char PublicKeyTable')
j = src.index('WMemBlock::WMemBlock()')
body = src[i:src.rfind('};', i, j) + 2]
open(sys.argv[2], 'w').write(body + '\n')
out = bytearray()
for name in ('PublicKeyTable', 'PrivateKeyTable'):
    a = src.index(name); b = src.index('};', a)
    vals = re.findall(r'0x[0-9a-fA-F]{2}', src[a:b])
    assert len(vals) == 4096, (name, len(vals))
    out += bytes(int(v, 16) for v in vals)
open(sys.argv[3], 'wb').write(out)
EOF

sed 's/#include "minatl.h"/#include <cstring>/' "$SRC/jrencrypt.cpp" > "$TMP/jrencrypt.cpp"
cp "$SRC/jrencrypt.h" "$TMP/"
g++ -O1 -I"$TMP" -o "$TMP/harness" harness.cpp "$TMP/jrencrypt.cpp"

mkdir -p "$ROOT/tests/Pangya.Tests/Data"
python3 -I - <<'EOF' > "$TMP/in.txt"
import random
random.seed(645)
for t in range(400):
    k, seed, n = random.randrange(16), random.randrange(256), random.choice([0, 1, 2, 3, 4, 5, 7, 8, random.randrange(2, 600)])
    body = bytes(random.randrange(256) for _ in range(n)).hex() or '-'
    print('c2s' if t % 2 else 's2c', k, seed, body)
EOF
"$TMP/harness" < "$TMP/in.txt" > "$TMP/out.txt"
! grep -q FAIL "$TMP/out.txt"
paste -d' ' "$TMP/in.txt" "$TMP/out.txt" > "$ROOT/tests/Pangya.Tests/Data/crypto_vectors.txt"
echo "keytables.bin: $(stat -c %s "$ROOT/src/Pangya.Core/Crypto/keytables.bin") bytes; vetores: $(wc -l < "$ROOT/tests/Pangya.Tests/Data/crypto_vectors.txt")"
