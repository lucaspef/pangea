-- Emblema da guilda (SPEC-guilda.md §4): 0x112 abre um upload pendente (o id vai como EMBLEM_IDX para o POST HTTP),
-- o Pangya.Web grava o PNG e marca uploaded_at, e o 0x113 aplica a marca na guilda. Cada upload tem um nome de marca
-- novo ('g' + id em hex), porque o cliente guarda cache pelo nome.
create table guild_emblem_uploads (
    id           int generated always as identity primary key,
    guild_id     int not null references guilds(id) on delete cascade,
    account_id   bigint not null references accounts(id) on delete cascade,
    mark         text not null default '',
    created_at   timestamptz not null default now(),
    uploaded_at  timestamptz,
    applied_at   timestamptz
);
create index guild_emblem_uploads_account_ix on guild_emblem_uploads (account_id, id desc);
