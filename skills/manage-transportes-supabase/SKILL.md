---
name: manage-transportes-supabase
description: Evolve, secure, diagnose, and verify the Supabase/Postgres integration used by TransportesGutierrez.Api. Use for schema migrations, PostgREST tables and filters, RPC counter functions, RLS/grants, Supabase environment configuration, connectivity checks, and C# model-to-column mapping in this repository.
---

# Manage Transportes Supabase

Read [references/data-contract.md](references/data-contract.md) before changing database or persistence code.

## Workflow

1. Treat `supabase/migrations/` as the source of truth. Add a new timestamped migration; never rewrite an applied production migration.
2. Keep C# PascalCase mapped to Postgres `snake_case` through shared serializer settings.
3. Generate remesa and manifiesto counters atomically inside Postgres RPC functions.
4. Enable RLS on every exposed table. Grant backend access only to `service_role`; add user policies only when Supabase Auth is intentionally introduced.
5. Store the server key only in `Supabase__Key` or a secret manager. Prefer current `sb_secret_...` keys; never expose one to a browser or logs.
6. Make migrations transactional where PostgreSQL permits and index actual filters and foreign keys.
7. Verify with `GET /api/rndc/health` and one non-destructive `select=id&limit=1` request.

## Deployment

```powershell
supabase db push --dry-run
supabase db push
```

Do not deploy to a remote project without explicit authorization and confirmed credentials.

