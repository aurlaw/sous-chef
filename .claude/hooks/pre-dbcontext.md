---
description: Fires before edits to SousChefDbContext or ServiceCollectionExtensions — reminds about pgvector/Npgsql 10.x workaround pattern
events:
  - pre_tool_use
globs:
  - "SousChef.Infrastructure/Data/SousChefDbContext.cs"
  - "SousChef.Infrastructure/ServiceCollectionExtensions.cs"
alwaysApply: false
---

# Hook: Pre-DbContext Warning

## Trigger
Fires before any edit to `SousChef.Infrastructure/Data/SousChefDbContext.cs` or `SousChef.Infrastructure/ServiceCollectionExtensions.cs`.

## Warning

⚠️ **Do not modify the pgvector / Npgsql 10.x workaround pattern.**

`Pgvector.EntityFrameworkCore 0.3.0` (built against Npgsql 9.x) has a partial incompatibility with Npgsql 10.x. The established workaround must be preserved exactly:

```csharp
// CORRECT — preserve this pattern
var dataSource = new NpgsqlDataSourceBuilder(connectionString)
    .UseVector()           // base Pgvector package — Npgsql 10.x compatible
    .Build();

services.AddDbContext<SousChefDbContext>(options =>
    options.UseNpgsql(dataSource,   // pass pre-built source
        o => o.UseVector()));       // EF model-level type mapping only
                                    // data-source-builder path never invoked here
```

**Why:** When a pre-built `NpgsqlDataSource` is passed to `UseNpgsql()`, the data-source-builder path inside `o.UseVector()` is never invoked — only the EF model-level type mapping runs. This avoids the Npgsql 10.x incompatibility in the EF extension while still enabling vector type support.

**Do not:**
- Call `NpgsqlDataSourceBuilder.UseVector()` from within the `UseNpgsql()` options lambda
- Remove the pre-built source pattern and rely on the connection string alone
- Upgrade `Pgvector.EntityFrameworkCore` without verifying Npgsql compatibility first
