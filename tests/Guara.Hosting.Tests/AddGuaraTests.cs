using Guara.Abstractions;
using Guara.Core;
using Guara.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Hosting.Tests;

public class AddGuaraTests
{
    [Fact]
    public void AddGuara_RegistersCoreServices_AndReturnsFluentBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddGuara(options => options.ApplicationName = "app-teste");

        Assert.Same(services, builder.Services);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<InProcessEventPublisher>(provider.GetRequiredService<IEventPublisher>());
        Assert.NotNull(provider.GetRequiredService<JobStateMachine>());
        Assert.NotNull(provider.GetRequiredService<TimeProvider>());
        Assert.Equal("app-teste", provider.GetRequiredService<GuaraOptions>().ApplicationName);
    }

    [Fact]
    public void AddGuara_CalledTwice_KeepsSingleRegistrations()
    {
        var services = new ServiceCollection();

        services.AddGuara();
        services.AddGuara();

        Assert.Single(services, d => d.ServiceType == typeof(IEventPublisher));
        Assert.Single(services, d => d.ServiceType == typeof(JobStateMachine));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddGuara_EmptyApplicationName_Throws(string applicationName)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddGuara(options => options.ApplicationName = applicationName));
        Assert.Contains("ApplicationName", ex.Message);
    }

    [Fact]
    public void AddGuara_EmptyQueues_Throws()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddGuara(options => options.DefaultQueues = []));
        Assert.Contains("DefaultQueues", ex.Message);
    }
}
