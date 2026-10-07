-- Dados de jogo de cada conta. Banco genérico (regra 12): todo objeto do jogo (personagem, item, caddie,
-- mascote, card, móvel...) é uma linha de "items" com o typeid do IFF + quantidade + atributos jsonb.
-- O id vem da sequência global object_id_seq: único entre todos os jogadores (e bots).

create table players (
    account_id  bigint primary key references accounts(id) on delete cascade,
    level       smallint not null default 0 check (level between 0 and 70),
    exp         int not null default 0 check (exp >= 0),
    pang        bigint not null default 0 check (pang >= 0),
    cookie      bigint not null default 0 check (cookie >= 0),
    equip       jsonb not null default '{}',       -- o que está equipado (ids/typeids)
    stats       jsonb not null default '{}',       -- estatísticas (tacadas, partidas, recordes...)
    flags       int not null default 0,            -- bit 0: tutorial feito
    created_at  timestamptz not null default now()
);

create table items (
    id          int primary key default nextval('object_id_seq'),
    account_id  bigint not null references accounts(id) on delete cascade,
    type_id     int not null,                      -- typeid do IFF; grupo = type_id >> 26
    quantity    int not null default 1 check (quantity >= 0),
    attrs       jsonb not null default '{}',       -- partes, cores, upgrades, mensagem, peça do caddie...
    expires_at  timestamptz,                       -- null = permanente
    created_at  timestamptz not null default now()
);
create index items_account_ix on items (account_id);
