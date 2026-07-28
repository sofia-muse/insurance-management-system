using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using InsuranceManagement.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InsuranceManagement.Tests;

public class ApiEndpointTests
{
    private const string JwtIssuer = "InsuranceManagement.Tests";
    private const string JwtAudience = "InsuranceManagement.Tests.Clients";
    private const string JwtSigningKey = "tests-signing-key-with-minimum-32-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var dbName = $"api-tests-{Guid.NewGuid()}";

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<InsuranceDbContext>));
                services.RemoveAll(typeof(InsuranceDbContext));

                services.AddDbContext<InsuranceDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));
            });
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsOnStartup"] = "false",
                    ["Database:RunSeedOnStartup"] = "false",
                    ["ApiSecurity:EnableLegacyWriteApiKey"] = "false",
                    ["Jwt:Issuer"] = JwtIssuer,
                    ["Jwt:Audience"] = JwtAudience,
                    ["Jwt:SigningKey"] = JwtSigningKey
                });
            });
        });
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateWriterToken());
        return client;
    }

    private static string CreateWriterToken()
        => CreateToken("test-user", includeWriteScope: true, includeWriterRole: true);

    private static string CreateReadOnlyToken()
        => CreateToken("readonly-user", includeWriteScope: false, includeWriterRole: false);

    private static string CreateToken(string subject, bool includeWriteScope, bool includeWriterRole)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject) };
        if (includeWriteScope)
        {
            claims.Add(new Claim("scope", "api.write"));
        }
        if (includeWriterRole)
        {
            claims.Add(new Claim(ClaimTypes.Role, "writer"));
        }

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task PostCustomer_ThenIssuePolicy_ReturnsCreatedResources()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "API User",
            email = $"api-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-0500",
            dateOfBirth = "1992-08-08",
            address = "5 Api Street"
        });

        customerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customerId}/policies")
        {
            Content = JsonContent.Create(new
            {
                type = "Health",
                premium = 750.25m,
                startDate = "2026-01-01",
                endDate = "2026-12-31",
                coverageDetails = "API issued health policy"
            })
        };
        request.Headers.Add("Idempotency-Key", "api-issue-key");

        var policyResponse = await client.SendAsync(request);
        policyResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var policy = await policyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        policy.GetProperty("customerId").GetGuid().Should().Be(customerId);
        policy.GetProperty("type").GetString().Should().Be("Health");
        policy.GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task GetPolicies_ReturnsPagedItemsEnvelope()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Filter User",
            email = $"filter-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-0600",
            dateOfBirth = "1988-04-04",
            address = "6 Filter Blvd"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        await client.PostAsJsonAsync($"/api/customers/{customerId}/policies", new
        {
            type = "Car",
            premium = 1100m,
            startDate = "2026-01-01",
            endDate = "2026-12-31",
            coverageDetails = "Car policy"
        });

        var listResponse = await client.GetAsync("/api/policies?type=Car&page=1&pageSize=10");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        payload.GetProperty("page").GetInt32().Should().Be(1);
        payload.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(p => p.GetProperty("type").GetString() == "Car");
    }

    [Fact]
    public async Task CancelPolicy_ThenUpdate_ReturnsBadRequestWithCode()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Cancel User",
            email = $"cancel-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-0700",
            dateOfBirth = "1979-09-09",
            address = "7 Cancel Court"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var policyResponse = await client.PostAsJsonAsync($"/api/customers/{customerId}/policies", new
        {
            type = "Life",
            premium = 900m,
            startDate = "2026-01-01",
            endDate = "2026-12-31",
            coverageDetails = "Life policy"
        });
        var policy = await policyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var policyId = policy.GetProperty("id").GetGuid();

        var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/policies/{policyId}/cancel");
        cancelRequest.Headers.Add("Idempotency-Key", "cancel-key-api");
        var cancelResponse = await client.SendAsync(cancelRequest);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateResponse = await client.PutAsJsonAsync($"/api/policies/{policyId}", new
        {
            premium = 950m,
            startDate = "2026-01-01",
            endDate = "2026-12-31",
            coverageDetails = "Should fail"
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await updateResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("POLICY_CANCELLED_UPDATE_FORBIDDEN");
    }

    [Fact]
    public async Task GetPolicyEvents_ReturnsHistoryAfterIssueAndUpdate()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Events User",
            email = $"events-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-0800",
            dateOfBirth = "1989-02-02",
            address = "8 Event Lane"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var issueResponse = await client.PostAsJsonAsync($"/api/customers/{customerId}/policies", new
        {
            type = "Health",
            premium = 400m,
            startDate = "2026-01-01",
            endDate = "2026-12-31",
            coverageDetails = "Events policy"
        });
        var policy = await issueResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var policyId = policy.GetProperty("id").GetGuid();

        await client.PutAsJsonAsync($"/api/policies/{policyId}", new
        {
            premium = 450m,
            startDate = "2026-01-01",
            endDate = "2026-12-31",
            coverageDetails = "Events policy updated"
        });

        var eventsResponse = await client.GetAsync($"/api/policies/{policyId}/events");
        eventsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await eventsResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        events.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task IssuePolicy_WithSameIdempotencyKeyDifferentPayload_ReturnsConflictWithCode()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Idempotency User",
            email = $"idem-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-0900",
            dateOfBirth = "1987-01-01",
            address = "9 Retry Street"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var request1 = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customerId}/policies")
        {
            Content = JsonContent.Create(new
            {
                type = "Health",
                premium = 100m,
                startDate = "2026-01-01",
                endDate = "2026-12-31",
                coverageDetails = "Payload A"
            })
        };
        request1.Headers.Add("Idempotency-Key", "issue-conflict-key");
        var first = await client.SendAsync(request1);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var request2 = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customerId}/policies")
        {
            Content = JsonContent.Create(new
            {
                type = "Health",
                premium = 200m,
                startDate = "2026-01-01",
                endDate = "2026-12-31",
                coverageDetails = "Payload B"
            })
        };
        request2.Headers.Add("Idempotency-Key", "issue-conflict-key");
        var second = await client.SendAsync(request2);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await second.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD");
    }

    [Fact]
    public async Task IssuePolicy_WithTrimVariantCoverageAndSameIdempotencyKey_ReplaysOriginalPolicy()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Trim Coverage User",
            email = $"trim-cov-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-4545",
            dateOfBirth = "1987-03-03",
            address = "45 Replay Lane"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var firstRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customerId}/policies")
        {
            Content = JsonContent.Create(new
            {
                type = "Health",
                premium = 315m,
                startDate = "2026-01-01",
                endDate = "2026-12-31",
                coverageDetails = "Semantically same coverage"
            })
        };
        firstRequest.Headers.Add("Idempotency-Key", "trim-coverage-key");
        var firstResponse = await client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstPolicy = await firstResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var secondRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customerId}/policies")
        {
            Content = JsonContent.Create(new
            {
                type = "Health",
                premium = 315m,
                startDate = "2026-01-01",
                endDate = "2026-12-31",
                coverageDetails = "   Semantically same coverage   "
            })
        };
        secondRequest.Headers.Add("Idempotency-Key", "trim-coverage-key");
        var secondResponse = await client.SendAsync(secondRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondPolicy = await secondResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        secondPolicy.GetProperty("id").GetGuid().Should().Be(firstPolicy.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task IssuePolicy_WithWhitespaceVariantIdempotencyKey_ReplaysOriginalPolicy()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Trim Key User",
            email = $"trim-key-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-5656",
            dateOfBirth = "1988-04-04",
            address = "56 Replay Road"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var firstRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customerId}/policies")
        {
            Content = JsonContent.Create(new
            {
                type = "Life",
                premium = 455m,
                startDate = "2026-01-01",
                endDate = "2026-12-31",
                coverageDetails = "Whitespace key replay"
            })
        };
        firstRequest.Headers.Add("Idempotency-Key", "trim-key-1");
        var firstResponse = await client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstPolicy = await firstResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var secondRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customerId}/policies")
        {
            Content = JsonContent.Create(new
            {
                type = "Life",
                premium = 455m,
                startDate = "2026-01-01",
                endDate = "2026-12-31",
                coverageDetails = "Whitespace key replay"
            })
        };
        secondRequest.Headers.Add("Idempotency-Key", "  trim-key-1  ");
        var secondResponse = await client.SendAsync(secondRequest);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondPolicy = await secondResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        secondPolicy.GetProperty("id").GetGuid().Should().Be(firstPolicy.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task RequestWithCorrelationId_EchoesHeaderInResponse()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);
        var correlationId = "corr-test-123";

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers?page=1&pageSize=1");
        request.Headers.Add("X-Correlation-Id", correlationId);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        values!.Single().Should().Be(correlationId);
    }

    [Fact]
    public async Task ValidationFailure_ReturnsStandardizedErrorPayload()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Underage User",
            email = $"underage-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-1000",
            dateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10).ToString("yyyy-MM-dd"),
            address = "10 Young Ave"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        error.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        error.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object);
        var errors = error.GetProperty("errors");
        errors.EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task WriteEndpoint_WithoutJwt_ReturnsUnauthorized()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Unauthorized User",
            email = $"unauth-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-1212",
            dateOfBirth = "1990-01-01",
            address = "No Key Road"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("AUTH_REQUIRED");
    }

    [Fact]
    public async Task WriteEndpoint_WithReadOnlyJwt_ReturnsForbidden()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateReadOnlyToken());

        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Forbidden User",
            email = $"forbidden-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-4343",
            dateOfBirth = "1990-01-01",
            address = "Forbidden Road"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("AUTH_FORBIDDEN");
    }

    [Fact]
    public async Task WriteBurst_ExceedingLimit_ReturnsTooManyRequests()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);
        HttpResponseMessage? hit = null;

        for (var i = 0; i < 30; i++)
        {
            var response = await client.PostAsJsonAsync("/api/customers", new
            {
                fullName = $"Burst User {i}",
                email = $"burst-{i}-{Guid.NewGuid():N}@example.com",
                phone = "+1-555-9898",
                dateOfBirth = "1990-01-01",
                address = "Burst Avenue"
            });

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                hit = response;
                break;
            }
        }

        hit.Should().NotBeNull();
        var error = await hit!.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task IssuePolicy_WithInvalidIdempotencyKey_ReturnsBadRequest()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Invalid Key User",
            email = $"invalid-idem-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-1414",
            dateOfBirth = "1989-01-01",
            address = "Validation Road"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customerId}/policies")
        {
            Content = JsonContent.Create(new
            {
                type = "Health",
                premium = 600m,
                startDate = "2026-01-01",
                endDate = "2026-12-31",
                coverageDetails = "Invalid idempotency key test"
            })
        };
        request.Headers.Add("Idempotency-Key", "invalid key with spaces");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("INVALID_IDEMPOTENCY_KEY");
    }

    [Fact]
    public async Task GetCustomer_WhenMissing_ReturnsNotFoundWithCode()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var response = await client.GetAsync($"/api/customers/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task GetCustomers_WithSqlLikeKeywordsInSearch_RemainsAllowed()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var response = await client.GetAsync("/api/customers?search=Union%20Street%20select&page=1&pageSize=20&sortBy=fullName&sortDir=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCustomers_WithApostropheInSearch_RemainsAllowed()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var response = await client.GetAsync("/api/customers?search=O%27Connor&page=1&pageSize=20&sortBy=fullName&sortDir=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPolicy_WhenMissing_ReturnsNotFoundWithCode()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var response = await client.GetAsync($"/api/policies/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("POLICY_NOT_FOUND");
    }

    [Fact]
    public async Task GetCustomerById_WithStaleActivePolicy_ReturnsExpiredPolicySummaryStatus()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Customer Status User",
            email = $"cust-status-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-3030",
            dateOfBirth = "1986-06-06",
            address = "30 Status Way"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var policyResponse = await client.PostAsJsonAsync($"/api/customers/{customerId}/policies", new
        {
            type = "Health",
            premium = 345m,
            startDate = today.AddDays(-20).ToString("yyyy-MM-dd"),
            endDate = today.AddDays(-2).ToString("yyyy-MM-dd"),
            coverageDetails = "Stale status policy"
        });
        policyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var policy = await policyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var policyId = policy.GetProperty("id").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
            var persisted = await db.Policies.FirstAsync(p => p.Id == policyId);
            persisted.Status = InsuranceManagement.Core.Enums.PolicyStatus.Active;
            await db.SaveChangesAsync();
        }

        var customerWithPoliciesResponse = await client.GetAsync($"/api/customers/{customerId}");
        customerWithPoliciesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await customerWithPoliciesResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var status = payload
            .GetProperty("policies")
            .EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == policyId)
            .GetProperty("status")
            .GetString();

        status.Should().Be("Expired");
    }

    [Fact]
    public async Task UpdateCustomer_WhenEmailExists_ReturnsConflict()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var first = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "A User",
            email = $"a-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-1111",
            dateOfBirth = "1980-01-01",
            address = "Street A"
        });
        var second = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "B User",
            email = $"b-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-2222",
            dateOfBirth = "1981-01-01",
            address = "Street B"
        });

        var secondCustomer = await second.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var secondId = secondCustomer.GetProperty("id").GetGuid();
        var firstCustomer = await first.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var firstEmail = firstCustomer.GetProperty("email").GetString();

        var update = await client.PutAsJsonAsync($"/api/customers/{secondId}", new
        {
            fullName = "B User Updated",
            email = firstEmail,
            phone = "+1-555-2222",
            dateOfBirth = "1981-01-01",
            address = "Street B2"
        });

        update.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await update.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("CUSTOMER_EMAIL_EXISTS");
    }

    [Fact]
    public async Task CancelPolicy_WhenAlreadyCancelledWithoutIdempotency_ReturnsBusinessRuleError()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Cancel Idem User",
            email = $"cancel-idem-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-1010",
            dateOfBirth = "1982-04-04",
            address = "Idem Street"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var policyResponse = await client.PostAsJsonAsync($"/api/customers/{customerId}/policies", new
        {
            type = "Life",
            premium = 500m,
            startDate = "2026-01-01",
            endDate = "2026-12-31",
            coverageDetails = "Cancelable"
        });
        var policy = await policyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var policyId = policy.GetProperty("id").GetGuid();

        var firstResult = await client.PostAsync($"/api/policies/{policyId}/cancel", null);
        firstResult.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondResult = await client.PostAsync($"/api/policies/{policyId}/cancel", null);

        secondResult.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await secondResult.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("code").GetString().Should().Be("POLICY_ALREADY_CANCELLED");
    }

    [Fact]
    public async Task CancelPolicy_WithWhitespaceVariantIdempotencyKey_ReplaysOriginalResponse()
    {
        await using var factory = CreateFactory();
        var client = CreateAuthorizedClient(factory);

        var customerResponse = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Cancel Trim Key User",
            email = $"cancel-trim-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-7878",
            dateOfBirth = "1981-04-04",
            address = "78 Cancel Path"
        });
        var customer = await customerResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var customerId = customer.GetProperty("id").GetGuid();

        var policyResponse = await client.PostAsJsonAsync($"/api/customers/{customerId}/policies", new
        {
            type = "Life",
            premium = 630m,
            startDate = "2026-01-01",
            endDate = "2026-12-31",
            coverageDetails = "Cancel replay"
        });
        var policy = await policyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var policyId = policy.GetProperty("id").GetGuid();

        var firstCancel = new HttpRequestMessage(HttpMethod.Post, $"/api/policies/{policyId}/cancel");
        firstCancel.Headers.Add("Idempotency-Key", "cancel-trim-key");
        var firstResult = await client.SendAsync(firstCancel);
        firstResult.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPayload = await firstResult.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var secondCancel = new HttpRequestMessage(HttpMethod.Post, $"/api/policies/{policyId}/cancel");
        secondCancel.Headers.Add("Idempotency-Key", "  cancel-trim-key ");
        var secondResult = await client.SendAsync(secondCancel);
        secondResult.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondPayload = await secondResult.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        secondPayload.GetProperty("id").GetGuid().Should().Be(firstPayload.GetProperty("id").GetGuid());
        secondPayload.GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task RequestWithInvalidCorrelationId_GeneratesSafeIdentifier()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers?page=1&pageSize=1");
        request.Headers.Add("X-Correlation-Id", "invalid id with spaces !!!");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        var headerValue = values!.Single();
        headerValue.Should().NotContain(" ");
        headerValue.Length.Should().BeLessThanOrEqualTo(64);
    }

    [Fact]
    public async Task WriteEndpoint_WithLegacyApiKeyEnabledButMissingKeyConfig_ReturnsInternalServerErrorWithAccurateTitle()
    {
        var dbName = $"api-tests-legacy-config-{Guid.NewGuid()}";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<InsuranceDbContext>));
                services.RemoveAll(typeof(InsuranceDbContext));
                services.AddDbContext<InsuranceDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));
            });
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsOnStartup"] = "false",
                    ["Database:RunSeedOnStartup"] = "false",
                    ["ApiSecurity:EnableLegacyWriteApiKey"] = "true",
                    ["ApiSecurity:WriteApiKey"] = ""
                });
            });
        });

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Config User",
            email = $"config-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-9090",
            dateOfBirth = "1990-01-01",
            address = "Config Road"
        });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        error.GetProperty("title").GetString().Should().Be("Internal Server Error");
        error.GetProperty("code").GetString().Should().Be("WRITE_ACCESS_CONFIG_INVALID");
    }

    [Fact]
    public async Task WriteEndpoint_WithLegacyApiKeyEnabled_AllowsWriteWithoutJwt()
    {
        var dbName = $"api-tests-legacy-enabled-{Guid.NewGuid()}";
        var legacyApiKey = "legacy-write-key";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<InsuranceDbContext>));
                services.RemoveAll(typeof(InsuranceDbContext));
                services.AddDbContext<InsuranceDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));
            });
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsOnStartup"] = "false",
                    ["Database:RunSeedOnStartup"] = "false",
                    ["ApiSecurity:EnableLegacyWriteApiKey"] = "true",
                    ["ApiSecurity:WriteApiKey"] = legacyApiKey,
                    ["Jwt:Issuer"] = JwtIssuer,
                    ["Jwt:Audience"] = JwtAudience,
                    ["Jwt:SigningKey"] = JwtSigningKey
                });
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", legacyApiKey);
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "Legacy Auth User",
            email = $"legacy-auth-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-9292",
            dateOfBirth = "1988-08-08",
            address = "Legacy Lane"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task WriteEndpoint_WithLegacyApiKeyEnabled_StillAllowsValidJwtWithoutApiKey()
    {
        var dbName = $"api-tests-legacy-jwt-{Guid.NewGuid()}";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<InsuranceDbContext>));
                services.RemoveAll(typeof(InsuranceDbContext));
                services.AddDbContext<InsuranceDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));
            });
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsOnStartup"] = "false",
                    ["Database:RunSeedOnStartup"] = "false",
                    ["ApiSecurity:EnableLegacyWriteApiKey"] = "true",
                    ["ApiSecurity:WriteApiKey"] = "legacy-write-key",
                    ["Jwt:Issuer"] = JwtIssuer,
                    ["Jwt:Audience"] = JwtAudience,
                    ["Jwt:SigningKey"] = JwtSigningKey
                });
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateWriterToken());
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            fullName = "JWT Legacy Coexist User",
            email = $"jwt-legacy-{Guid.NewGuid():N}@example.com",
            phone = "+1-555-9393",
            dateOfBirth = "1988-08-08",
            address = "Jwt Coexist Lane"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
