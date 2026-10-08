-- Guildas (docs/protocolo/SPEC-guilda.md §9). Um jogador está em no máximo uma guilda; o pedido de entrada pendente é
-- uma linha com cargo 9 (é assim que o cliente vê: o jogador "aguardando" já tem o guildUID da guilda pedida).
create table guilds (
    id          int generated always as identity primary key,       -- guildUID (u32 no protocolo, nunca 0)
    name        text not null,                                        -- até 20 bytes cp949
    name_key    text not null,                                        -- nome normalizado (unicidade entre as abertas)
    master_id   bigint not null references accounts(id),
    notice      text not null default '',
    introduce   text not null default '',
    mark        text not null default '',                             -- arquivo do emblema (sem .png)
    pang        int not null default 0,
    point       int not null default 0,
    created_at  timestamptz not null default now(),
    closed_at   timestamptz                                           -- encerrada: some das listas, o nome volta a valer
);
create unique index guilds_open_name_ux on guilds (name_key) where closed_at is null;

create table guild_members (
    account_id  bigint primary key references accounts(id) on delete cascade,
    guild_id    int not null references guilds(id) on delete cascade,
    class       smallint not null check (class in (1, 2, 3, 9)),      -- 1 mestre, 2 submestre, 3 membro, 9 pedido
    message     text not null default '',                             -- mensagem pessoal / texto do pedido
    joined_at   timestamptz not null default now()
);
create index guild_members_guild_ix on guild_members (guild_id, class);

-- histórico por jogador (0x107)
create table guild_history (
    id          bigint generated always as identity primary key,
    account_id  bigint not null references accounts(id) on delete cascade,
    guild_id    int not null,
    guild_name  text not null,
    state       smallint not null,                                    -- GUILD_STATE_IDX
    at          timestamptz not null default now()
);
create index guild_history_account_ix on guild_history (account_id, id desc);

-- 24 h depois de criar, encerrar ou sair (código 54006)
alter table players add column guild_cooldown_until timestamptz;
