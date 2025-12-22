var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

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

// Map API controllers
app.MapControllers();

// Fallback to index.html for SPA style navigation
app.MapFallbackToFile("index.html");

app.Run();

// Simple model kept for template compatibility
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
