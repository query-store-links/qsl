using QueryStoreLinks.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Configure AllowUnsafeTls option from configuration (default: false)
HttpClientProvider.AllowUnsafeTls = builder.Configuration.GetValue<bool>("AllowUnsafeTls", false);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

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
                        // Allow loopback origins during development for local UI testing
                        if (originUri.IsLoopback && allowLoopback)
                            return true;
                    }

                    return IsOriginAllowed(origin, allowedOrigins);
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    );
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseDeveloperExceptionPage();
}

// Serve static files for the minimal frontend
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseHttpsRedirection();

// Use CORS policy
app.UseCors(CorsPolicyName);

// Map API controllers
app.MapControllers();

// Fallback to index.html for SPA style navigation
app.MapFallbackToFile("index.html");

app.Run();

// Helper: match origin against patterns supporting wildcard host (*.example.com) and optional scheme
static bool IsOriginAllowed(string? origin, string[] patterns)
{
    if (string.IsNullOrWhiteSpace(origin))
        return false;
    if (patterns == null || patterns.Length == 0)
        return false;

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        return false;
    var originScheme = originUri.Scheme;
    var originHost = originUri.Host;

    foreach (var p in patterns)
    {
        if (string.IsNullOrWhiteSpace(p))
            continue;

        string pat = p.Trim();
        string? patScheme = null;
        string patHost = pat;

        if (pat.Contains("://"))
        {
            // Split into scheme and host portion without requiring the host to be a valid DNS name
            var parts = pat.Split(new[] { "://" }, 2, StringSplitOptions.None);
            patScheme = parts[0];
            patHost = parts.Length > 1 ? parts[1] : string.Empty;
        }

        // Trim any trailing slashes that might be present
        if (patHost.EndsWith("/"))
            patHost = patHost.TrimEnd('/');

        // If scheme is specified in pattern and differs -> not match
        if (
            !string.IsNullOrEmpty(patScheme)
            && !string.Equals(patScheme, originScheme, StringComparison.OrdinalIgnoreCase)
        )
            continue;

        // Host pattern may contain leading wildcard *.example.com
        if (patHost.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = patHost.Substring(2);
            if (originHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        else
        {
            if (string.Equals(originHost, patHost, StringComparison.OrdinalIgnoreCase))
                return true;
        }
    }

    return false;
}

// Simple model kept for template compatibility
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
