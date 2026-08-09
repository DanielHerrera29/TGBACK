# Supabase data contract

The initial contract is `supabase/migrations/20260803000000_initial_transportes_schema.sql`.

- `settings`: business and RNDC defaults.
- `remesas`: shipment data and RNDC state.
- `manifiestos`: manifests linked to `remesas.id`.
- `counters`: internal atomic sequence state.
- `generar_consecutivo_remesa()` and `generar_consecutivo_manifiesto()`: PostgREST RPC endpoints.

The API uses trusted server-to-server calls with a secret/service-role key. Public roles have no table privileges. If browser access is required later, create narrow RLS policies and use a publishable key plus the user's JWT; never reuse the backend secret.

