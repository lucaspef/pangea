-- Bilhetes do mensageiro (쪽지; docs/protocolo/SPEC-messenger.md §5.3): texto curto, custa 10 pang a quem manda.
-- delivered_at = já mostrado ao destinatário (0x2E/0x103 pelo mensageiro ou 0xB0 pelo game).
create table notes (
    id            bigint generated always as identity primary key,
    account_id    bigint not null references accounts(id) on delete cascade,   -- destinatário
    sender_id     bigint references accounts(id) on delete set null,
    sender_nick   text not null,
    text          text not null,
    created_at    timestamptz not null default now(),
    delivered_at  timestamptz
);
create index notes_box_ix on notes (account_id, id desc);
