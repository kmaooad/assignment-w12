using KmaOoad18.Leanware.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KmaOoad18.Leanware.Web
{
    public partial class Startup
    {
        public void ConfigureStudentServices(IServiceCollection services)
        {
            // For students: register your DI services here

            services.AddDbContext<LeanwareContext>(options => options.UseSqlite("Data Source=leanware.db"));
        }
    }
}