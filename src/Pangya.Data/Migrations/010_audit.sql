-- Auditoria (fase 9): toda ação de GM no jogo e de administração (linha de comando e painel web).
create table audit_log (
    id         bigint generated always as identity primary key,
    at         timestamptz not null default now(),
    actor_id   bigint references accounts(id) on delete set null,   -- null = linha de comando / sistema
    actor      text not null,                                       -- nick/login de quem fez, ou 'console'
    action     text not null,                                       -- ex.: notice, kick, block, item-give
    target     text not null default '',
    details    text not null default ''
);
create index audit_log_at_ix on audit_log (id desc);
