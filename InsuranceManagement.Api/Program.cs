using FluentValidation;
using FluentValidation.AspNetCore;
using InsuranceManagement.Api.Middleware;
using InsuranceManagement.Infrastructure;
using InsuranceManagement.Infrastructure.Data;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Insurance Management API",
        Version = "v1",
        Description = "Backend engine for insurance agents to manage customers and policies."
    });
});

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=insurance.db";

builder.Services.AddInfrastructure(connectionString);

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

await DbSeeder.SeedAsync(app.Services);

app.Run();

public partial class Program;
