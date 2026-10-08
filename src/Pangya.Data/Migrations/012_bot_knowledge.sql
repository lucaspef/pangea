-- Aprendizado do bot por nível (0 fácil .. 4 impossível): carregado quando o game server sobe e gravado durante o jogo.
create table bot_holes (
    level      smallint not null,
    course     smallint not null,                -- mapa (Course.iff)
    hole       smallint not null,                -- número do buraco no mapa
    data       jsonb not null,                   -- {safe, hazards, blocked, cobraBlocked}: listas de [x, z]
    updated_at timestamptz not null default now(),
    primary key (level, course, hole)
);

create table bot_calibration (
    level      smallint primary key,
    data       jsonb not null,                   -- {distance, aim, samples, special}
    updated_at timestamptz not null default now()
);
