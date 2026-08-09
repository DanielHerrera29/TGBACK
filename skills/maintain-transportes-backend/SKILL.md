---
name: maintain-transportes-backend
description: Maintain, diagnose, refactor, secure, and test the TransportesGutierrez.Api ASP.NET Core 8 backend and its RNDC SOAP workflows. Use for changes to controllers, DTOs, XML generation, RNDC requests/responses, dependency injection, configuration, API errors, observability, or backend tests in this repository.
---

# Maintain Transportes Backend

Read [references/architecture.md](references/architecture.md) before changing behavior.

## Workflow

1. Inspect the affected controller, DTO, service, and model together.
2. Preserve RNDC process mappings: remesa generation `3`, manifiesto generation `4`, remesa fulfillment `5`, manifiesto fulfillment `6`.
3. Never log or return RNDC passwords, Supabase keys, authorization headers, or XML containing credentials.
4. Keep external I/O behind injected services. Pass cancellation tokens in new async paths.
5. Validate inputs at the API boundary. Use invariant culture for numbers and explicit RNDC date formats.
6. Escape every dynamic XML value. Prefer `XDocument` or `XmlWriter` when changing XML structure.
7. Persist a draft before RNDC submission, then record success or failure explicitly.
8. Build the solution and run all tests after each coherent change.

## Verification

```powershell
dotnet build .\TransportesGutierrez.sln
dotnet test .\TransportesGutierrez.sln --no-build
```

For configuration changes, verify startup failure without required secrets, then start with environment variables and call `GET /api/rndc/health`.

