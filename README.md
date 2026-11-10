# InspectionReview

Blazor Server app (.NET 9) backed by SQL Server.

## Local setup

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- EF Core CLI, for migrations: run `dotnet tool restore` in the repo root. This installs the
  version pinned in `.config/dotnet-tools.json`, which matches the project's EF Core version.

### 1. Set the connection strings in user secrets

The connection string (including the database password) is kept in .NET user secrets,
outside the repo. Pick your own strong password. SQL Server requires at least 8 characters
with three of: uppercase, lowercase, digits, symbols.

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=localhost,1433;Database=InspectionReview;User Id=sa;Password=<your-password>;TrustServerCertificate=True" \
  --project src/InspectionReview.Web

dotnet user-secrets set "ConnectionStrings:BlobStorage" "UseDevelopmentStorage=true" \
  --project src/InspectionReview.Web
```

`UseDevelopmentStorage=true` points the Azure Blob SDK at the local Azurite emulator.

Also set the password given to the seeded test users (see [Seed data](#seed-data)). It must have
at least 6 characters, with an uppercase letter, a lowercase letter, a digit and a symbol:

```bash
dotnet user-secrets set "SeedUsers:Password" "<your-password>" --project src/InspectionReview.Web
```

> **Never put connection strings in `appsettings.json`.** That file is committed.
> The keys under `ConnectionStrings` are left empty there on purpose. User secrets
> (locally) or environment variables such as `ConnectionStrings__DefaultConnection` (in deployed
> environments) supply the real values. If `DefaultConnection` is not set, the app stops at
> startup and tells you so.

### 2. Start SQL Server and Azurite

```bash
./scripts/db-up.sh
```

This reads the password from your user secret and starts two Docker containers:

- SQL Server 2022 on `localhost:1433`. The first start takes about 20 seconds.
- Azurite (Azure Blob Storage emulator) on `localhost:10000`.

Data is kept in the `sqlserver-data` and `azurite-data` Docker volumes across restarts.

Use `./scripts/db-up.sh` rather than a plain `docker compose up`. The script is what
passes the password to Docker.

### 3. Run the app

```bash
dotnet run --project src/InspectionReview.Web
```

Then open http://localhost:5276.

In Development, startup applies any pending EF Core migrations (creating the `InspectionReview`
database on first run) and then seeds the test data. There is no separate `dotnet ef database update` step.

## Seed data

Roles, firms and inspections are seeded with `HasData` in `AppDbContext`. They are part of the migrations.
Users and inspection assignments are created by `Data/SeedData.cs` at startup in Development only.
That code checks before inserting, so restarting never adds duplicates. If a different account
already uses a seed user's email, startup stops with an error rather than creating a second account.

All seed users share the `SeedUsers:Password` password:

| User | Role | Firm / assignments |
| --- | --- | --- |
| olivia.grant@harbourview.example | FirmUser | Harbourview Property Group (firm 1) |
| marcus.bell@northgate.example | FirmUser | Northgate Facilities Ltd (firm 2) |
| priya.nair@inspections.example | Inspector | INS-001 and INS-003; recused from Northgate (firm 2), so INS-003 is view-only |
| tom.reyes@inspections.example | Inspector | INS-002 |
| hannah.cole@inspections.example | Reviewer | none |

## Local infrastructure commands

These apply to both containers.

| Task | Command |
| --- | --- |
| Start | `./scripts/db-up.sh` |
| Stop (keeps data) | `docker compose stop` |
| Reset (**deletes all data**) | `docker compose down -v`, then `./scripts/db-up.sh` |

The sa password is set only when the container is first created. If you change the password
in user secrets later, run the reset command above so the container is recreated with the
new password.
