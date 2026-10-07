# Servidor PangYa (C# / .NET 10)

Servidores para o cliente PangYa KR "645 QA". Plano, regras e fases: [docs/PLANO.md](docs/PLANO.md).

## Desenvolvimento (WSL Ubuntu, root)
```
bash tools/setup-dev.sh   # PostgreSQL rodando, usuário/bancos e config/pangya.json + config/test.json (senha gerada)
bash verify.sh            # compila e roda todos os testes; termina com "VERIFY: VERDE"
```
Do Git Bash no Windows: `MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -u root -- bash /mnt/e/dev/pangya-server/verify.sh`.

## Estrutura
- `src/Pangya.Core` — cifra, pacotes, IFF, configuração, logs, limites
- `src/Pangya.Domain` — lógica do jogo (independente de versão do cliente)
- `src/Pangya.Protocol.KR645` — pacotes do cliente 645
- `src/Pangya.Data` — PostgreSQL (migrações em `Migrations/NNN_nome.sql`)
- `tests/` — xUnit e testes de protocolo
- `config/*.example.json` — modelos de configuração (os `.json` reais não vão para o git)
