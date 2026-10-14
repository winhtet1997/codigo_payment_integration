using Microsoft.AspNetCore.Hosting;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Sockets;
using MySqlConnector;
using PaymentIntegration.Application.Abstractions;
using PaymentIntegration.Infrastructure.Payments;
using PaymentIntegration.Infrastructure.Persistence;

namespace PaymentIntegration.Tests;

public sealed class CheckoutApiFixture : IAsyncLifetime
{
    public const string Secret = "test-secret-key";

    public CountingGateway Gateway { get; } = new();

    public string MySqlConnectionString { get; } =
        "Server=127.0.0.1;Port=3306;Database=payment_test;User=root;Password=root;AllowPublicKeyRetrieval=True;SslMode=None";

    public HttpClient Client { get; private set; } = null!;

    public Task InitializeAsync()
    {
        EnsureMySqlIsRunning();
        EnsureRedisIsRunning();

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = MySqlConnectionString,
                ["ConnectionStrings:Redis"] = "127.0.0.1:6379,defaultDatabase=1",
                ["TwoC2P:MerchantId"] = "TESTMERCHANT",
                ["TwoC2P:SecretKey"] = Secret,
                ["App:FrontendBaseUrl"] = "http://localhost:3000",
                ["App:PublicApiBaseUrl"] = "http://localhost:8080"
            };
            foreach (var setting in settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton<IPaymentGateway>(Gateway);
            });
        });

        Client = factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    private void EnsureMySqlIsRunning()
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(MySqlConnectionString);
            var database = builder.Database;
            builder.Database = "";
            using var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE DATABASE IF NOT EXISTS `" + database.Replace("`", "", StringComparison.Ordinal) + "`;";
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "MySQL is not running on 127.0.0.1 port 3306 with user root and password root. Start it with scripts/start-local.ps1.",
                exception);
        }
    }

    private static void EnsureRedisIsRunning()
    {
        try
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", 6379);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Redis is not running on 127.0.0.1 port 6379. Start it with scripts/start-local.ps1.",
                exception);
        }
    }

    public AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(MySqlConnectionString, new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;
        return new AppDbContext(options);
    }
}

public sealed class CountingGateway : IPaymentGateway
{
    private int _tokenCalls;
    private int _doPaymentCalls;
    private int _inquiryCalls;
    private readonly List<TokenCommand> _tokenCommands = new();
    private readonly object _gate = new();

    public int TokenCalls => Volatile.Read(ref _tokenCalls);

    public int DoPaymentCalls => Volatile.Read(ref _doPaymentCalls);

    public int InquiryCalls => Volatile.Read(ref _inquiryCalls);

    public IReadOnlyList<TokenCommand> TokenCommands
    {
        get
        {
            lock (_gate)
            {
                return _tokenCommands.ToList();
            }
        }
    }

    private readonly Dictionary<string, decimal> _amounts = new();

    public void Reset()
    {
        Interlocked.Exchange(ref _tokenCalls, 0);
        Interlocked.Exchange(ref _doPaymentCalls, 0);
        Interlocked.Exchange(ref _inquiryCalls, 0);
        lock (_gate)
        {
            _tokenCommands.Clear();
            _amounts.Clear();
        }
    }

    public async Task<TokenResult> CreateTokenAsync(TokenCommand command, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _tokenCalls);
        lock (_gate)
        {
            _tokenCommands.Add(command);
            _amounts[command.InvoiceNo] = command.Amount;
        }

        // Long enough that the other nine requests are waiting on the Redis lock.
        await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        return new TokenResult(
            "pay-" + command.InvoiceNo,
            "https://sandbox-pgw-ui.2c2p.com/payment/4.3/#/token/test",
            "0000",
            "Success");
    }

    public async Task<DoPaymentResult> DoPaymentAsync(DoPaymentCommand command, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _doPaymentCalls);
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        return new DoPaymentResult(
            "2000",
            "Transaction is completed, please do payment inquiry request for full payment information.",
            null,
            "CC");
    }

    public Task<PaymentResultNotice> InquireAsync(string invoiceNo, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _inquiryCalls);
        decimal amount;
        lock (_gate)
        {
            _amounts.TryGetValue(invoiceNo, out amount);
        }

        return Task.FromResult(new PaymentResultNotice(
            invoiceNo,
            amount,
            "SGD",
            "0000",
            "Success",
            "CC",
            "411111XXXXXX1111",
            "REF001",
            null));
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<CheckoutApiFixture>
{
    public const string Name = "integration";
}
