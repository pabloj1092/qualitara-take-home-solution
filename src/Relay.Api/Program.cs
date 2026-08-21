using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Relay.Api.Testing;
using Relay.Application;
using Relay.Application.Abstractions;
using Relay.Application.Dashboard;
using Relay.Domain;
using Relay.Infrastructure;
using Relay.Infrastructure.Reading;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy => policy
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Stage 1 checkpoint only: exercises the whole assembly path — validation, orchestrator,
// baseline, status, DTO, JSON — with a real curl before Stage 2's migration/database exist.
// Never set in a real environment; Program.cs is otherwise the only place a port meets its
// concrete implementation.
var useStubReader = builder.Configuration.GetValue<bool>("UseStubDashboardReader");

if (useStubReader)
{
    var (dashboardReader, metadataReader) = StubDataSeed.Build();
    builder.Services.AddSingleton<IDashboardReader>(dashboardReader);
    builder.Services.AddSingleton<IAccountMetadataReader>(metadataReader);
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("Relay")
        ?? throw new InvalidOperationException("Missing configuration value 'ConnectionStrings:Relay'.");

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.MapEnum<OutcomePolarity>("outcome_polarity");
    var dataSource = dataSourceBuilder.Build();

    builder.Services.AddDbContext<RelayDbContext>(options =>
        options.UseNpgsql(dataSource).UseSnakeCaseNamingConvention());

    builder.Services.AddScoped<IDashboardReader, EfDashboardReader>();
    builder.Services.AddScoped<IAccountMetadataReader, EfAccountMetadataReader>();
}

builder.Services.AddScoped<DashboardQueryService>();
builder.Services.AddScoped<MetaQueryService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    if (!useStubReader)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RelayDbContext>();
        db.Database.Migrate();
    }
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        var (status, title) = exception switch
        {
            RequestValidationException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        context.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception is RequestValidationException requestValidation
                ? requestValidation.Message
                : app.Environment.IsDevelopment()
                    ? exception?.Message
                    : "An unexpected error occurred. Check the server logs.",
        };

        if (exception is RequestValidationException withParameter)
        {
            problem.Extensions["parameter"] = withParameter.ParameterName;
        }

        await context.Response.WriteAsJsonAsync(problem);
    });
});

app.UseCors("AngularDev");

app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration test suite (Stage 3).
public partial class Program;
