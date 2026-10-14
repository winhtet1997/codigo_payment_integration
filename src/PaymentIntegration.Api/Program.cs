using PaymentIntegration.Api.Middleware;
using PaymentIntegration.Api.Swagger;
using PaymentIntegration.Application;
using PaymentIntegration.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(SwaggerSetup.Configure);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var frontendOrigin = builder.Configuration["App:FrontendBaseUrl"] ?? "http://localhost:3000";
var allowedOrigins = new[] { frontendOrigin, "http://localhost:3000" }
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<PaymentIntegration.Infrastructure.Persistence.DatabaseInitializer>();
    await initializer.InitializeAsync(CancellationToken.None);
}

app.UseMiddleware<ProblemExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Payment checkout API");
    options.RoutePrefix = "swagger";
});
app.UseCors();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
