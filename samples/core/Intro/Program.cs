using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Intro
{
    internal class Program
    {
        private static void Main()
        {
            using (var db = new BloggingContext())
            {
                // Remove these lines if you are running migrations from the command line
                db.Database.EnsureDeleted();
                db.Database.Migrate();
            }

            #region Querying
            using (var db = new BloggingContext())
            {
                var blogs = db.Blogs
                    .Where(b => b.Rating > 3)
                    .OrderBy(b => b.Url)
                    .ToList();
            }

            // Anonymous: OK
            using (var db = new BloggingContext())
            {
                var blogs = db.Blogs
                    .Select(b => new { Original = b, Custom = b.BlogId.ToString() + b.Url })
                    .OrderBy(b => b.Custom)
                    .ToList();
            }

            // Class: OK
            using (var db = new BloggingContext())
            {
                var blogs = db.Blogs
                    .Select(b => new Projection { Original = b, Custom = b.BlogId.ToString() + b.Url })
                    .OrderBy(b => b.Custom)
                    .ToList();
            }

            // Record: OK
            using (var db = new BloggingContext())
            {
                var q = db.Blogs
                    .Select(b => new ProjectionRecord { Original = b, Custom = b.BlogId.ToString() + b.Url })
                    .OrderBy(b => b.Custom);
                var blogs = q
                    .ToList();
            }

            // Positional record, no OrderBy: OK
            using (var db = new BloggingContext())
            {
                var blogs = db.Blogs
                    .Select(b => new ProjectionPositionalRecord(b, b.BlogId.ToString() + b.Url))
                    .ToList();
            }

            // Positional record, with OrderBy: crashes
            using (var db = new BloggingContext())
            {
                var blogs = db.Blogs
                    .Select(b => new ProjectionPositionalRecord(b, b.BlogId.ToString() + b.Url))
                    .OrderBy(b => b.Custom)
                    .ToList();
            }

            // Class with private constructor, with OrderBy: crashes
            using (var db = new BloggingContext())
            {
                var blogs = db.Blogs
                    .Select(b => new ProjectionWithPrivateConstructor(b, b.BlogId.ToString() + b.Url))
                    .OrderBy(b => b.Custom)
                    .ToList();
            }
            #endregion

            #region SavingData
            using (var db = new BloggingContext())
            {
                var blog = new Blog { Url = "http://sample.com" };
                db.Blogs.Add(blog);
                db.SaveChanges();
            }
            #endregion
        }
    }

    class Projection
    {
        public Blog Original { get; set; }
        public string Custom { get; set; }
    }

    class ProjectionWithPrivateConstructor
    {
        private ProjectionWithPrivateConstructor()
        {
        }

        public ProjectionWithPrivateConstructor(Blog original, string custom)
        {
            Original = original;
            Custom = custom;
        }

        public Blog Original { get; set; }
        public string Custom { get; set; }
    }

    record ProjectionPositionalRecord(Blog Original, string Custom);

    record ProjectionRecord
    {
        public Blog Original { get; set; }
        public string Custom { get; set; }
    }
}
