using Microsoft.EntityFrameworkCore;

namespace KmaOoad18.Leanware.Web.Data
{
    public class LeanwareContext : DbContext
    {
        // Add DbSets for your entities

        public LeanwareContext(DbContextOptions<LeanwareContext> options) : base(options) { }
    }
}
