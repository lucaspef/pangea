-- Amigos do mensageiro (docs/protocolo/SPEC-messenger.md §4.3). Uma linha por lado: o pedido de A para B grava
-- (A, B, pedido) e (B, A, pendente); aceitar põe as duas em aceito. Apelido e bloqueio são de cada lado.
create table friends (
    owner_id    bigint not null references accounts(id) on delete cascade,
    friend_id   bigint not null references accounts(id) on delete cascade,
    state       smallint not null check (state in (1, 2, 3)),   -- 1 eu pedi, 2 ele pediu (falta eu aceitar), 3 amigos
    alias       text not null default '',
    blocked     boolean not null default false,                  -- eu bloqueei
    created_at  timestamptz not null default now(),
    primary key (owner_id, friend_id),
    check (owner_id <> friend_id)
);
create index friends_friend_ix on friends (friend_id);
