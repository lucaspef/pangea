-- Onde está cada objeto do jogador: 0 inventário, 1 armário do My Room, 2 card ativo/encaixado.
-- (continua tudo genérico em "items"; o armário tem também um saldo de pang.)
alter table items add column location smallint not null default 0;
alter table players add column locker_pang bigint not null default 0 check (locker_pang >= 0);
