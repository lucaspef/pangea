# Protocolo KR 645 — especificações

Cópia (2026-10-07) das especificações do emulador Python (`/root/pangya-server-work/emu`), escritas a partir do
cliente decompilado e validadas contra o cliente real. São a referência para a camada `Pangya.Protocol.KR645`.
Se o emulador ganhar descobertas novas, copie de novo os `SPEC-*.md` de lá.

- Layout das structs: gerado de `rebang/source/shared/*.h` por `tools/StructGen` (ver `src/Pangya.Protocol.KR645/Structs.g.cs`).
- Cifra: `src/Pangya.Core/Crypto/PacketCipher.cs`, validada pelos vetores de `tools/CryptoCheck` (código do próprio cliente).

## Versões de cliente (nota da sessão do emulador, 2026-10-07)
- O cliente KR "645 QA" (outubro de 2011) é da era **Season 5** (o código tem a pasta `s5/`); a Season 6 coreana
  ("Challenges") saiu em 20/11/2012. A camada `Protocol.KR645` continua como está; uma camada futura deve mirar um
  cliente S6+ real.
- Referência para essa camada futura: cliente oficial **KR 839** (julho de 2016) em
  `C:\Users\Lucas\Downloads\PangYa_Client_KR_839` — ProjectG.exe sem empacotamento (analisável) e paks com a mesma chave KR.
- Correções que o cliente de teste 645 precisa com os dados 642: `SPEC-client-data.md` do emulador.
