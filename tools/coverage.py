#!/usr/bin/env python3
# Pacotes C->S do game server que o cliente manda (SPEC-coverage.md do emulador, só leitura) e que o C# ainda não trata.
#   python3 tools/coverage.py
import re, glob
cov = open('/root/pangya-server-work/emu/SPEC-coverage.md', encoding='utf-8').read()
rows = {}
for line in cov.splitlines():
    m = re.match(r'\|\s*GAME (0x[0-9A-Fa-f]+)\*?\s*\|\s*GAME\s*\|\s*([^|]*)\|', line)
    if m:
        rows[int(m.group(1),16)] = m.group(2).strip()[:110]
handled = set()
for f in glob.glob('src/Pangya.Protocol.KR645/Game/*.cs'):
    s = open(f, encoding='utf-8').read()
    for m in re.finditer(r'\bC[A-Za-z0-9]+\s*=\s*(0x[0-9A-Fa-f]+)', s):
        handled.add(int(m.group(1),16))
missing = [i for i in sorted(rows) if i not in handled]
print(f"cliente->servidor (GAME): {len(rows)} no coverage, {len(handled)} constantes C# , faltando {len(missing)}")
for i in missing: print(f"0x{i:02X}  {rows[i]}")
