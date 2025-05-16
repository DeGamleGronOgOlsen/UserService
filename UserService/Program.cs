using Microsoft.OpenApi.Models;
using NLog;
using NLog.Web;
using MongoDB.Driver;
using Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.V1.Commons;
using VaultSharp.V1.AuthMethods;

var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings()
    .GetCurrentClassLogger();
logger.Debug("start min service");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Add services to the container.
    builder.Services.AddControllers();
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Version = "v1",
            Title = "UserAPI API",
            Description = "An ASP.NET Core Web API for managing ToDo items",
            TermsOfService = new Uri("https://example.com/terms"),
            Contact = new OpenApiContact
            {
                Name = "Example Contact",
                Url = new Uri("https://example.com/contact")
            },
            License = new OpenApiLicense
            {
                Name = "Example License",
                Url = new Uri("https://example.com/license")
            }
        });
    });

    // Tilføjer logging
    var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>().CreateLogger("VaultLogger");

    var httpClientHandler = new HttpClientHandler();
    var EndPoint = "https://vaulthost:8201/";
    httpClientHandler.ServerCertificateCustomValidationCallback =
    (message, cert, chain, sslPolicyErrors) => { return true; };

    try
    {
        // Henter hemmeligheder fra Vault
        logger.LogInformation("Henter hemmeligheder fra Vault...");
        Secret<SecretData> secretData = await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(path: "Secrets", mountPoint: "secret");

        string mySecretKey = secretData.Data.Data["Secret"]?.ToString();
        if (string.IsNullOrEmpty(mySecretKey))
        {
            logger.LogError("Secret er ikke defineret i Vault.");
            throw new ArgumentNullException(nameof(mySecretKey), "Secret er ikke defineret i Vault.");
        }

        string myIssuer = secretData.Data.Data["Issuer"]?.ToString();
        if (string.IsNullOrEmpty(myIssuer))
        {
            logger.LogError("Issuer er ikke defineret i Vault.");
            throw new ArgumentNullException(nameof(myIssuer), "Issuer er ikke defineret i Vault.");
        }

        builder.Configuration["Secret"] = mySecretKey;
        builder.Configuration["Issuer"] = myIssuer;

        logger.LogInformation("Hemmeligheder hentet fra Vault:");
        logger.LogInformation($"Secret: {mySecretKey}");
        logger.LogInformation($"Issuer: {myIssuer}");


        // Konfigurer JWT autentificering
        builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters()
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = myIssuer,
                ValidAudience = "http://localhost",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(mySecretKey))
            };
        });
    }
    catch (Exception ex)
    {
        logger.LogError($"Fejl under hentning af hemmeligheder fra Vault: {ex.Message}");
        throw;
    }

    builder.Services.AddAuthorization();
    builder.Services.AddScoped<IUserDBRepository, UserMongoDBService>();
    builder.Services.AddSingleton<IMongoClient>(sp =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var connectionString = configuration["MongoConnectionString"] ?? "mongodb://admin:1234@localhost:27018/";
        return new MongoClient(connectionString);
    });

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.UseAuthentication();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    logger.Error(ex, "Stopped program because of exception");
    throw;
}
finally
{
    NLog.LogManager.Shutdown();
}
