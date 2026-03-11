using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using SlotHero.Api.Services;
using SlotHero.Core;

// Configured before host build to capture startup failures in both 
//console and daily rolling log files
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Bind Google settings to a strongly-typed object so secrets aren't scattered via raw IConfiguration lookups
builder.Services.Configure<GoogleSettings>(builder.Configuration.GetSection("Google"));
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<IBusinessService, BusinessService>();

// SQLite path points to the Core project to keep the database co-located with the data model
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=../slotHero.Core/slotHero.db"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Scalar provides a visual UI for the OpenAPI document since .AddOpenApi() alone has no built-in UI
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
