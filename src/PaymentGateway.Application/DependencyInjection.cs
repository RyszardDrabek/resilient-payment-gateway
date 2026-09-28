using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentGateway.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            var licenseKey = Environment.GetEnvironmentVariable("MEDIATR_LICENSE_KEY");
            if (!string.IsNullOrWhiteSpace(licenseKey))
            {
                cfg.LicenseKey = licenseKey;
            }
        });

        services.AddValidatorsFromAssembly(assembly);
        return services;
    }
}
