# Protocolo KR 645 — especificações

Cópia (2026-10-07) das especificações do emulador Python (`/root/pangya-server-work/emu`), escritas a partir do
cliente decompilado e validadas contra o cliente real. São a referência para a camada `Pangya.Protocol.KR645`.
Se o emulador ganhar descobertas novas, copie de novo os `SPEC-*.md` de lá.

- Layout das structs: gerado de `rebang/source/shared/*.h` por `tools/StructGen` (ver `src/Pangya.Protocol.KR645/Structs.g.cs`).
- Cifra: `src/Pangya.Core/Crypto/PacketCipher.cs`, validada pelos vetores de `tools/CryptoCheck` (código do próprio cliente).
