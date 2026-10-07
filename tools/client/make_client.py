#!/usr/bin/env python3
"""Gera uma CÓPIA NOVA do cliente KR 645 QA apontando para o servidor C# (nunca altera arquivos existentes).

    python3 make_client.py <entrada.exe> <saida_ReleaseQA.exe> [ip] [porta_web] [porta_login]

Entrada esperada: LocalServerFix_ReleaseQA.exe (já com IP 127.0.0.1 e renderizador sem shader) ou
LocalServer_ReleaseQA.exe. O nome da saída TEM de terminar em _ReleaseQA.exe (o cliente carrega a dll
wangreal/wangfixd com o sufixo depois do último '_').

Patches (só dados, sem mexer em código):
  - URL do login web  -> http://<ip>:<porta_web>/Secure/Login/LoginForGame.aspx
  - URL de cadastro   -> http://<ip>:<porta_web>/register   (botão "cadastrar" do login)
  - LOGIN_PORT_LIST[0] (loginunit.cpp:8, VA 0x00AA2538 no .map) 10101 -> <porta_login>
"""
import hashlib
import os
import struct
import sys

NUL = b'\0'
LOGIN_PORT_VA = 0x00AA2538


def fail(msg):
    sys.exit('ERRO: ' + msg)


def replace_slot(data, old_prefix, slot_len, new):
    """Troca a string que começa com old_prefix numa área de slot_len bytes (o original + zeros)."""
    if data.count(old_prefix) != 1:
        fail('texto %r encontrado %d vezes (esperado 1)' % (old_prefix, data.count(old_prefix)))
    off = data.index(old_prefix)
    if len(new) >= slot_len:
        fail('texto novo grande demais: %r' % new)
    end = data.index(NUL, off)
    if any(data[end:off + slot_len]):
        fail('área depois de %r não está zerada' % old_prefix)
    old = bytes(data[off:end])
    data[off:off + slot_len] = new + NUL * (slot_len - len(new))
    print('  @0x%x: %r -> %r' % (off, old, new))


def va_to_offset(data, va):
    pe = struct.unpack_from('<I', data, 0x3C)[0]
    nsec = struct.unpack_from('<H', data, pe + 6)[0]
    opt = struct.unpack_from('<H', data, pe + 20)[0]
    base = struct.unpack_from('<I', data, pe + 24 + 28)[0]
    rva = va - base
    for i in range(nsec):
        s = pe + 24 + opt + i * 40
        vsize, vaddr, rsize, raw = struct.unpack_from('<IIII', data, s + 8)
        if vaddr <= rva < vaddr + max(vsize, rsize):
            if rva - vaddr >= rsize:
                fail('VA 0x%x fora dos dados do arquivo' % va)
            return raw + rva - vaddr
    fail('VA 0x%x sem seção' % va)


def main():
    if len(sys.argv) < 3:
        fail(__doc__)
    src, dst = sys.argv[1], sys.argv[2]
    ip = sys.argv[3] if len(sys.argv) > 3 else '127.0.0.1'
    web = int(sys.argv[4]) if len(sys.argv) > 4 else 30080
    login = int(sys.argv[5]) if len(sys.argv) > 5 else 30101
    if not dst.endswith('_ReleaseQA.exe'):
        fail('a saída tem de terminar em _ReleaseQA.exe')
    if os.path.exists(dst):
        fail('%s já existe; não sobrescrevo' % dst)
    data = bytearray(open(src, 'rb').read())
    print('entrada %s md5=%s' % (src, hashlib.md5(data).hexdigest()))
    host = ip if web == 80 else '%s:%d' % (ip, web)
    # slots: tamanho da string original do cliente + NUL
    replace_slot(data, b'http://127.0.0.1/Secure/Login/LoginForGame.aspx',
                 len(b'http://qa.pangya.gametree.co.kr/Secure/Login/LoginForGame.aspx') + 1,
                 ('http://%s/Secure/Login/LoginForGame.aspx' % host).encode())
    replace_slot(data, b'http://qa.www.gametree.co.kr/SignUp/Join.aspx?rsn=9',
                 len(b'http://qa.www.gametree.co.kr/SignUp/Join.aspx?rsn=9') + 1,
                 ('http://%s/register' % host).encode())
    off = va_to_offset(data, LOGIN_PORT_VA)
    old = struct.unpack_from('<i', data, off)[0]
    if old != 10101:
        fail('LOGIN_PORT_LIST em 0x%x vale %d (esperado 10101)' % (off, old))
    struct.pack_into('<i', data, off, login)
    print('  @0x%x: porta de login 10101 -> %d' % (off, login))
    open(dst, 'wb').write(data)
    print('saída %s md5=%s' % (dst, hashlib.md5(data).hexdigest()))


if __name__ == '__main__':
    main()
