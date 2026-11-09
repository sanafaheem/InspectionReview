# InspectionReview

Blazor Server app (.NET 9) backed by SQL Server.

## Local setup

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- EF Core CLI, for migrations: `dotnet tool install --global dotnet-ef`

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

### 3. Create the database

```bash
dotnet ef database update --project src/InspectionReview.Web
```

### 4. Run the app

```bash
dotnet run --project src/InspectionReview.Web
```

Then open http://localhost:5276.

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
