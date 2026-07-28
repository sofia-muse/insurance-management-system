using InsuranceManagement.Core.Entities;
using InsuranceManagement.Core.Enums;
using InsuranceManagement.Core.Interfaces;
using InsuranceManagement.Infrastructure.Data;
using InsuranceManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InsuranceManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<InsuranceDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IPolicyService, PolicyService>();

        return services;
    }
}
