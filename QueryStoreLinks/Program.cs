using QueryStoreLinks.Helpers;

namespace QueryStoreLinks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();

            builder.Services.AddOpenApi();

            var allowedOrigins =
                builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "https://*.krnl64.win" };
            const string CorsPolicyName = "QueryStoreLinksCors";
            var allowLoopback = builder.Environment.IsDevelopment();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy(
                    CorsPolicyName,
                    policy =>
                    {
                        policy
                            .SetIsOriginAllowed(origin =>
                            {
                                if (string.IsNullOrWhiteSpace(origin))
                                    return false;
                                if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
                                {
                                    if (originUri.IsLoopback && allowLoopback)
                                        return true;
                                }

                                return CORS.IsOriginAllowed(origin, allowedOrigins);
                            })
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    }
                );
            });

            var app = builder.Build();

            app.UseCors("QueryStoreLinksCors");

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
