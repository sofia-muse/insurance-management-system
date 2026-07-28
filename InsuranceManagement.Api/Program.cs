using FluentValidation;
using FluentValidation.AspNetCore;
using InsuranceManagement.Api.DTOs;
using InsuranceManagement.Api.Middleware;
using InsuranceManagement.Api.Security;
using InsuranceManagement.Api.Utilities;
using InsuranceManagement.Infrastructure;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

// API composition root:
// - registers application/infrastructure services
// - configures shared HTTP behavior and middleware pipeline
// - applies startup migration/seed policies per environment/config
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        // Flatten and retain field-level errors to keep responses readable
        // for humans and still easy to parse for clients.
        var flatErrors = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .SelectMany(kvp => kvp.Value!.Errors.Select(err =>
                string.IsNullOrWhiteSpace(err.ErrorMessage)
                    ? $"{kvp.Key} is invalid."
                    : err.ErrorMessage))
            .ToList();
        var fieldErrors = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors
                    .Select(err => string.IsNullOrWhiteSpace(err.ErrorMessage)
                        ? $"{kvp.Key} is invalid."
                        : err.ErrorMessage)
                    .ToArray());

        var payload = new
        {
            title = "Validation Failed",
            status = StatusCodes.Status400BadRequest,
            code = "VALIDATION_ERROR",
            detail = string.Join(" | ", flatErrors),
            traceId = context.HttpContext.TraceIdentifier,
            errors = fieldErrors
        };

        return new BadRequestObjectResult(payload);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Insurance Management API",
        Version = "v1",
        Description = "Backend engine for insurance agents to manage customers and policies. Supports idempotency, pagination, correlation IDs, and policy audit events."
    });
});

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ApiCorsPolicy", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (allowedOrigins.Length == 0)
        {
            return;
        }

        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Content-Type", "Authorization", "Idempotency-Key", "X-Correlation-Id", "X-Api-Key");
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var issuer = builder.Configuration["Jwt:Issuer"];
        var audience = builder.Configuration["Jwt:Audience"];
        var key = builder.Configuration["Jwt:SigningKey"];

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? string.Empty)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await ApiErrorResponseWriter.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "AUTH_REQUIRED",
                    "A valid bearer token is required.");
            },
            OnForbidden = async context =>
            {
                await ApiErrorResponseWriter.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "AUTH_FORBIDDEN",
                    "Authenticated user does not have enough permissions.");
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.CanWrite, policy => policy.RequireAssertion(ctx =>
        ctx.User.IsInRole("writer")
        || ctx.User.HasClaim(ClaimTypes.Role, "writer")
        || ctx.User.HasClaim("scope", "api.write")
        || HasLegacyWriteAccess(ctx.Resource)));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        await ApiErrorResponseWriter.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "Too Many Requests",
            "RATE_LIMIT_EXCEEDED",
            "Request rate limit exceeded. Please retry later.");
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy(RateLimitPolicies.PerIpBurst, context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy(RateLimitPolicies.WritePerIdentity, context =>
    {
        var key = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "auth-user"
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\mssqllocaldb;Database=InsuranceManagement;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

builder.Services.AddInfrastructure(connectionString);

var app = builder.Build();
var requireJwtConfig = !app.Environment.IsDevelopment();
if (requireJwtConfig)
{
    var issuer = builder.Configuration["Jwt:Issuer"];
    var audience = builder.Configuration["Jwt:Audience"];
    var signingKey = builder.Configuration["Jwt:SigningKey"];
    if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) || string.IsNullOrWhiteSpace(signingKey))
    {
        throw new InvalidOperationException("JWT settings (Jwt:Issuer, Jwt:Audience, Jwt:SigningKey) must be configured for non-development environments.");
    }
}

// Middleware order matters: correlation/logging first, global error envelope early,
// then endpoint write-access enforcement.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<WriteAccessMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("ApiCorsPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

var runMigrationsOnStartup = builder.Configuration.GetValue<bool?>("Database:RunMigrationsOnStartup")
    ?? app.Environment.IsDevelopment();
var runSeedOnStartup = builder.Configuration.GetValue<bool?>("Database:RunSeedOnStartup")
    ?? app.Environment.IsDevelopment();
if (runMigrationsOnStartup || runSeedOnStartup)
{
    // Keeps local/dev onboarding quick while still allowing production-safe
    // opt-in behavior via explicit configuration flags.
    await DbSeeder.SeedAsync(app.Services, runMigrationsOnStartup, runSeedOnStartup);
}

app.Run();

static bool HasLegacyWriteAccess(object? resource)
{
    if (resource is HttpContext httpContext)
    {
        return httpContext.Items.TryGetValue("LegacyWriteAccessGranted", out var marker)
            && marker is true;
    }

    if (resource is AuthorizationFilterContext filterContext)
    {
        return filterContext.HttpContext.Items.TryGetValue("LegacyWriteAccessGranted", out var marker)
            && marker is true;
    }

    return false;
}

public partial class Program;
