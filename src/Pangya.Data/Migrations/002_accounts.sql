-- Contas, chaves de sessão e registro de servidores online.

-- id = MemberNo/UID que o cliente vê (u32). Começa alto para não parecer contagem de jogadores.
create table accounts (
    id              bigint generated always as identity (start with 100001) primary key,
    login           text not null,
    password_hash   text not null,
    nickname        text,
    identity_flags  int not null default 0,          -- bits de GM do cliente (0x4/0x10/0x8000)
    blocked_until   timestamptz,
    block_reason    text,
    created_at      timestamptz not null default now(),
    last_login_at   timestamptz,
    last_login_ip   inet,
    constraint accounts_login_format check (login ~ '^[A-Za-z0-9_]{3,20}$')
);
create unique index accounts_login_uq on accounts (lower(login));
create unique index accounts_nickname_uq on accounts (lower(nickname)) where nickname is not null;

-- Chaves de uso único: kind 1 = AuthKey do login web (vira a "senha" do login server),
-- kind 2 = chave do login server para o game server.
create table sessions (
    key         text primary key,
    account_id  bigint not null references accounts(id) on delete cascade,
    kind        smallint not null,
    expires_at  timestamptz not null,
    created_at  timestamptz not null default now()
);
create index sessions_expires_ix on sessions (expires_at);

-- Servidores online (game, messenger...). Cada um renova expires_at periodicamente;
-- um registro fixo (ex.: servidor externo) pode usar expires_at = 'infinity'.
create table servers (
    id          int primary key,
    kind        text not null,                       -- 'game', 'messenger'
    name        text not null,
    address     text not null,
    port        int not null,
    max_users   int not null,
    cur_users   int not null default 0,
    flags       int not null default 0,
    expires_at  timestamptz not null
);
