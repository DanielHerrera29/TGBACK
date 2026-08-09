# Backend architecture

- `Controllers/RemesaController.cs`: RNDC processes 3 and 5.
- `Controllers/ManifiestoController.cs`: RNDC processes 4 and 6.
- `Services/XmlGeneratorService.cs`: RNDC XML construction.
- `Services/RndcClient.cs`: legacy SOAP 1.1 transport and parsing.
- `Services/SupabaseService.cs`: server-side PostgREST persistence and RPC.
- `Configurations/SupabaseOptions.cs`: startup-validated configuration.
- `supabase/migrations/`: versioned database contract.

The backend is the trusted boundary. Never ship server keys or RNDC credentials to browser code. Keep state transitions recoverable: create `draft`, submit externally, then update to `generated`/fulfilled or `error_rndc`.

