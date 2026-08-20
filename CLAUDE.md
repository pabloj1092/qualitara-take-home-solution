# qualitara-take-home-solution

## Local database

A Postgres 16 database runs locally in Docker, seeded from
[Qualitara/tv-analytics-takehome](https://github.com/Qualitara/tv-analytics-takehome)
(`schema.sql` + `seed.sql`, copied into this repo).

| | |
|---|---|
| Container | `relay_takehome_postgres` |
| Host | `localhost` |
| Port | `5432` |
| Database | `relay_takehome` |
| User | `relay` |
| Password | `relay` |
| Connection string | `postgresql://relay:relay@localhost:5432/relay_takehome` |

Defined in [docker-compose.yml](docker-compose.yml). Start/stop with:

```bash
docker compose up -d
docker compose down
```

Connect via psql:

```bash
docker exec -it relay_takehome_postgres psql -U relay -d relay_takehome
```

Tables: `accounts` (20 rows), `activity_events` (12,626 rows). Schema in
[schema.sql](schema.sql). Treat the seed data as-is — don't regenerate,
extend, or replace it (per the source repo's README).
