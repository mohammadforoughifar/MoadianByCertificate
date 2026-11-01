# Database pagination regression checks

Run from the repository root with .NET 8 SDK and NuGet access:

```sh
dotnet run --project inventory/tests/DatabasePagination
```

The executable links the **production** paging helpers and client response parser,
uses real in-memory SQLite (not EF InMemory), intercepts the executed SQL, and
checks SQL Server translation without needing a SQL Server connection.

Covers filtered totals, exact offsets, unique ordering, mapping only page rows,
legacy caps, empty/negative/large offsets, response shapes, double-pagination
protection, a combined cartable union, SQLite quantity predicate translation,
dictionary response shapes, root-page/subtree hydration and authorization scope,
grouped midpoint-to-even performance ranking, and
`{ total, items }` / `{ totalCount, items }` client parsing.

This is not an endpoint integration suite: authorization scopes, application
mappings and all migrated service queries still require a full API/Client build
and database-backed acceptance tests. No passing runtime result is claimed until
this command has actually been executed.
