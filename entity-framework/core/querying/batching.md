---
title: Query Batching - EF Core
description: Executing several LINQ queries and bulk operations in a single database round-trip with Entity Framework Core
author: virzak
ms.date: TODO(release)
uid: core/querying/batching
---
# Query Batching

> [!NOTE]
> This article describes a feature proposed in [dotnet/efcore#39390](https://github.com/dotnet/efcore/pull/39390) that has not shipped in any EF Core release yet. It will be updated when the feature ships. Query batching is only available with relational database providers.

Every LINQ query that EF Core executes is sent to the database as its own command, which means its own network round-trip. When a page or a request needs several independent result sets, those round-trips add up, and on a high-latency connection they can dominate the response time. Query batching lets you send several queries to the database in a single command and then consume each result set in turn.

> [!TIP]
> You can [view this article's sample on GitHub](https://github.com/dotnet/EntityFramework.Docs/tree/main/samples/core/Querying/Batching).

## Executing a batch

Pass each query as a lambda to `Batch` or `BatchAsync` on your context. The lambdas receive the context so that the queries are created from it, and they are not executed individually; the batch compiles them, combines their SQL into one command and executes that command once. The result is a `BatchResultsReader` from which the result sets are consumed in the order the queries were given:

[!code-csharp[Main](../../../samples/core/Querying/Batching/Program.cs?name=BasicBatch)]

The SQL sent to the database contains both queries, separated by the provider's statement terminator:

```sql
SELECT [b].[BlogId], [b].[Rating], [b].[Url]
FROM [Blogs] AS [b]
WHERE [b].[Rating] > @b0_minimumRating
ORDER BY [b].[BlogId];

SELECT [p].[PostId], [p].[Archived], [p].[BlogId], [p].[Content], [p].[Date], [p].[Title]
FROM [Posts] AS [p]
WHERE [p].[Date] >= @b1_since
ORDER BY [p].[Date] DESC
```

Parameters keep their usual names with a per-query prefix, so the same variable name can appear in several queries of a batch.

Each query is translated exactly as it would be on its own, so everything that applies to a standalone query also applies inside a batch: the same operators are supported, `Include` works, change tracking behaves the same way, and `AsNoTracking` or query filters are honoured per query.

## Consuming results

The result sets must be consumed in the same order as the queries, and each one can be consumed once. `BatchResultsReader` offers several ways to do so:

- `NextAsList<T>` and `NextAsListAsync<T>` materialize the next result set into a `List<T>`.
- `NextAsEnumerable<T>` and `NextAsAsyncEnumerable<T>` return a lazily evaluated enumerable that streams the rows of the next result set as they are read from the database.
- `NextAsRowsAffected` and `NextAsRowsAffectedAsync` consume the result of a bulk operation (see below).

The element type you ask for must match the element type of the query at that position; EF Core throws if it does not.

[!code-csharp[Main](../../../samples/core/Querying/Batching/Program.cs?name=StreamingResults)]

Requesting a result set moves the underlying data reader forward. An enumerable returned for an earlier result set is therefore invalidated as soon as a later result set is requested, and enumerating it afterwards throws. Consume each streamed result set completely before asking for the next one. Result sets cannot be consumed in parallel or interleaved, because a batch is read through a single data reader. If several result sets need to be processed concurrently, materialize them with `NextAsListAsync` first and then work on the lists in parallel.

The reader holds the database connection until it is disposed. Dispose it as soon as the required results have been consumed, as in the samples above, which use `using` declarations.

## Anonymous type projections

The `NextAsList<T>` family needs the element type as a type argument, which cannot be written for anonymous types. Wrap such a query with `BatchQuery.Create`, pass the wrapper to the batch and then pass the same wrapper again when consuming the result set; the element type is inferred from it:

[!code-csharp[Main](../../../samples/core/Querying/Batching/Program.cs?name=AnonymousProjection)]

The wrapper is only used to infer the type. The result sets are still consumed in positional order, so pass the wrapper at the position where its query was supplied.

## Including ExecuteUpdate and ExecuteDelete

A batch can also contain <xref:core/saving/execute-insert-update-delete> operations. They are not executed when the lambda is evaluated; their SQL is added to the batch and runs together with the queries. The number of affected rows comes back as a result set of its own, consumed with `NextAsRowsAffected` or `NextAsRowsAffectedAsync`:

[!code-csharp[Main](../../../samples/core/Querying/Batching/Program.cs?name=BulkOperations)]

The bulk operation is followed by a provider-specific statement that reads the number of affected rows, so that the count comes back as a result set like any other:

```sql
SET NOCOUNT OFF;
UPDATE [p]
SET [p].[Archived] = @b0_p
FROM [Posts] AS [p]
WHERE [p].[Date] < @b0_cutoff;
SELECT @@ROWCOUNT;

SELECT [p].[PostId], [p].[Archived], [p].[BlogId], [p].[Content], [p].[Date], [p].[Title]
FROM [Posts] AS [p]
WHERE [p].[Archived] = CAST(0 AS bit)
ORDER BY [p].[Date]
```

> [!IMPORTANT]
> A batch does not create a transaction. Whether the statements in a batch run atomically depends on the database: SQL Server, for example, commits each statement separately unless a transaction is open. If the queries and bulk operations in a batch must succeed or fail together, start a transaction explicitly:

[!code-csharp[Main](../../../samples/core/Querying/Batching/Program.cs?name=BatchInTransaction)]

Only `ExecuteUpdate` and `ExecuteDelete` can be included. Other terminal operators such as `Count`, `Any` or `First` cannot be batched; express them as a query that returns rows instead, for example a projection with `Take(1)`.

## Streaming and buffering

By default the rows of each result set are read from the database only as they are consumed, in the same way as a standalone query. When a retrying execution strategy is configured (see <xref:core/miscellaneous/connection-resiliency>), EF Core must be able to re-run the whole batch if a transient failure occurs part-way through, so all result sets are read into memory when the batch executes. Keep this in mind when batching queries that return many rows in a resilient configuration.

## Limitations

The following cannot be included in a batch, and EF Core throws an `InvalidOperationException` describing the reason:

- Queries configured as split queries (see <xref:core/querying/single-split-queries>). Each split query issues several commands, so it cannot be a single statement in a batch.
- `GroupBy` queries that EF Core executes in streaming mode.
- Raw SQL queries created with `FromSql` that have not been composed on with further LINQ operators (see <xref:core/querying/sql-queries>). Composed `FromSql` queries are supported.
- Queries that use a `DbParameter` instance supplied by your code rather than a captured variable.
- Queries created from a different context instance than the one the batch is executed on.
- Queries whose SQL was pregenerated by query precompilation.
- Providers that do not expose a way to read the number of rows affected by the preceding statement cannot include `ExecuteUpdate` or `ExecuteDelete` in a batch. SQL Server and SQLite support it.

A batch also requires that all its queries agree on buffering, which is always the case when the execution strategy is configured once for the context.

## Relationship to split queries

<xref:core/querying/single-split-queries> describes how a single query with several collection `Include` operators can be split into several SQL queries to avoid cartesian explosion, at the cost of extra round-trips. Query batching addresses a different situation: several unrelated queries that your code already runs one after another. The two can be combined conceptually, but a split query itself cannot be an element of a batch.
