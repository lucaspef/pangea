-- GuildMatch (docs/protocolo/SPEC-guildmatch.md §3.4, §5 passo 5): contribuição de cada membro, vitórias/derrotas/empates
-- da guilda e histórico das partidas. O "pang de guilda" não vai para a carteira do jogador (GB).
alter table guild_members add column point int not null default 0, add column pang int not null default 0;
alter table guilds add column wins int not null default 0, add column losses int not null default 0, add column draws int not null default 0;

create table guild_matches (
    id          bigint generated always as identity primary key,
    at          timestamptz not null default now(),
    guild_red   int references guilds(id) on delete set null,
    guild_blue  int references guilds(id) on delete set null,
    point_red   int not null,
    point_blue  int not null,
    pang_red    int not null,
    pang_blue   int not null,
    winner      smallint not null check (winner in (0, 1, 2))           -- 0 vermelha, 1 azul, 2 empate
);
