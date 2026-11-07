using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace EFQuerying.Batching;

internal class Program
{
    private static async Task Main(string[] args)
    {
        using (var context = new BloggingContext())
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();

            context.Blogs.AddRange(
                new Blog
                {
                    Url = "http://sample.com/blogs/fish",
                    Rating = 5,
                    Posts =
                    [
                        new Post { Title = "Fish are great", Content = "...", Date = new DateTime(2026, 1, 10) },
                        new Post { Title = "Fish are fun", Content = "...", Date = new DateTime(2025, 6, 2), Archived = true }
                    ]
                },
                new Blog
                {
                    Url = "http://sample.com/blogs/cats",
                    Rating = 3,
                    Posts =
                    [
                        new Post { Title = "Cats are great", Content = "...", Date = new DateTime(2026, 2, 14) }
                    ]
                });

            await context.SaveChangesAsync();
        }

        using (var context = new BloggingContext())
        {
            #region BasicBatch
            var minimumRating = 3;
            var since = new DateTime(2026, 1, 1);

            using var batch = await context.BatchAsync(
                ctx => ctx.Blogs.Where(b => b.Rating > minimumRating).OrderBy(b => b.BlogId),
                ctx => ctx.Posts.Where(p => p.Date >= since).OrderByDescending(p => p.Date));

            var blogs = await batch.NextAsListAsync<Blog>();
            var recentPosts = await batch.NextAsListAsync<Post>();
            #endregion

            Console.WriteLine($"{blogs.Count} blogs, {recentPosts.Count} recent posts");
        }

        using (var context = new BloggingContext())
        {
            #region StreamingResults
            using var batch = await context.BatchAsync(
                ctx => ctx.Posts.OrderBy(p => p.PostId),
                ctx => ctx.Blogs.OrderBy(b => b.BlogId));

            // Rows are read from the database as the loop advances
            await foreach (var post in batch.NextAsAsyncEnumerable<Post>())
            {
                Console.WriteLine(post.Title);
            }

            // Requesting the next result set invalidates the enumerable above
            var blogs = await batch.NextAsListAsync<Blog>();
            #endregion
        }

        using (var context = new BloggingContext())
        {
            #region AnonymousProjection
            var blogSummaries = BatchQuery.Create((BloggingContext ctx) => ctx.Blogs
                .Select(b => new { b.Url, PostCount = b.Posts.Count }));

            using var batch = await context.BatchAsync(
                blogSummaries,
                ctx => ctx.Posts.Where(p => !p.Archived));

            var summaries = await batch.NextAsListAsync(blogSummaries);
            var activePosts = await batch.NextAsListAsync<Post>();

            foreach (var summary in summaries)
            {
                Console.WriteLine($"{summary.Url}: {summary.PostCount} posts");
            }
            #endregion
        }

        using (var context = new BloggingContext())
        {
            #region BulkOperations
            var cutoff = new DateTime(2026, 1, 1);

            using var batch = await context.BatchAsync(
                ctx => ctx.Posts.Where(p => p.Date < cutoff).ExecuteUpdate(s => s.SetProperty(p => p.Archived, true)),
                ctx => ctx.Posts.Where(p => !p.Archived).OrderBy(p => p.Date));

            var archivedCount = await batch.NextAsRowsAffectedAsync();
            var remainingPosts = await batch.NextAsListAsync<Post>();
            #endregion

            Console.WriteLine($"Archived {archivedCount} posts, {remainingPosts.Count} remain");
        }

        using (var context = new BloggingContext())
        {
            #region BatchInTransaction
            await using var transaction = await context.Database.BeginTransactionAsync();

            using (var batch = await context.BatchAsync(
                       ctx => ctx.Posts.Where(p => p.Archived).ExecuteDelete(),
                       ctx => ctx.Blogs.Where(b => !b.Posts.Any()).ExecuteDelete()))
            {
                var deletedPosts = await batch.NextAsRowsAffectedAsync();
                var deletedBlogs = await batch.NextAsRowsAffectedAsync();

                Console.WriteLine($"Deleted {deletedPosts} posts and {deletedBlogs} empty blogs");
            }

            await transaction.CommitAsync();
            #endregion
        }
    }
}
