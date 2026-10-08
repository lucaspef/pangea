#!/usr/bin/env python3
"""Copia para o servidor o data/pangya.iff que o cliente realmente usa: o do ÚLTIMO projectg*.pak (ordem de nome) que
contém data/pangya.iff. Os paks de correção (zzfix, zzzcourse, zzzzitems...) ficam depois do original, então o vencedor
é o que tem o Wiz City no Course.iff, o RandomBox no Item.iff etc. Só LÊ a pasta do cliente.

Formato do pak (westpak): trailer u32 offTabela, u32 n, u8 0x12; entrada = u8 lenNome, u8 tipo, u32 off, u32 tamanho,
u32 tamanho^0x71(ou outro), nome XOR 0x71, NUL. Tipo 0x10 = guardado sem compressão (é como os paks de correção são
gerados); os paks originais comprimidos não são tratados aqui.

  python3 tools/sync-iff.py [pasta-do-cliente] [destino]
"""
import glob
import hashlib
import os
import struct
import sys

GAME = sys.argv[1] if len(sys.argv) > 1 else '/mnt/e/dev/pangya-test/KR642'
DEST = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'data', 'pangya.iff')
NAME = b'data/pangya.iff'


def entries(path):
    with open(path, 'rb') as f:
        f.seek(-9, os.SEEK_END)
        off, n, magic = struct.unpack('<IIB', f.read(9))
        if magic != 0x12:
            return
        f.seek(off)
        table = f.read()
    i = 0
    for _ in range(n):
        ln, kind = table[i], table[i + 1]
        o, size, _x = struct.unpack_from('<III', table, i + 2)
        name = bytes(c ^ 0x71 for c in table[i + 14:i + 14 + ln])
        yield name, kind, o, size
        i += 14 + ln + 1


def main():
    winner = None
    for pak in sorted(glob.glob(os.path.join(GAME, 'projectg*.pak')), key=os.path.basename):
        try:
            for name, kind, off, size in entries(pak):
                if name.lower() == NAME:
                    winner = (pak, kind, off, size)
        except (OSError, struct.error, IndexError):
            print(f'aviso: {pak} ilegível, ignorado')
    if winner is None:
        sys.exit(f'nenhum {NAME.decode()} em {GAME}')
    pak, kind, off, size = winner
    if kind != 0x10:
        sys.exit(f'{os.path.basename(pak)}: {NAME.decode()} comprimido (tipo 0x{kind:X}); use a extração do emulador')
    with open(pak, 'rb') as f:
        f.seek(off)
        data = f.read(size)
    old = open(DEST, 'rb').read() if os.path.exists(DEST) else b''
    if old == data:
        print(f'pangya.iff já é o de {os.path.basename(pak)}')
        return
    os.makedirs(os.path.dirname(DEST), exist_ok=True)
    with open(DEST, 'wb') as f:
        f.write(data)
    print(f'pangya.iff <- {os.path.basename(pak)} ({size} bytes, md5 {hashlib.md5(data).hexdigest()})')


if __name__ == '__main__':
    main()
