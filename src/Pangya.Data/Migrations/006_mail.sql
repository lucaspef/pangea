-- Correio (docs/protocolo/SPEC-correio-presentes.md §6). No KR 645 o "presente" é o correio: presentes da loja, prêmios
-- e cartas entre jogadores viram uma carta com até 4 anexos. Ids da mesma sequência dos objetos (não colidem com guid).
create table mails (
    id           int primary key default nextval('object_id_seq'),
    account_id   bigint not null references accounts(id) on delete cascade,   -- destinatário
    sender_id    bigint references accounts(id) on delete set null,           -- null = sistema
    sender_nick  text not null,                                               -- congelado no envio ('@...' = sistema)
    message      text not null default '',
    created_at   timestamptz not null default now(),
    read_at      timestamptz
);
create index mails_box_ix on mails (account_id, id desc);

create table mail_items (
    id           int primary key default nextval('object_id_seq'),
    mail_id      int not null references mails(id) on delete cascade,
    type_id      int not null,
    quantity     int not null default 1 check (quantity >= 0),               -- pang quando type_id = 0x1A000010
    attrs        jsonb not null default '{}',                                -- atributos do item (upgrades, partes...)
    taken_at     timestamptz
);
create index mail_items_mail_ix on mail_items (mail_id);
