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

var builder = WebApplication.CreateBuilder(args);

// Tilføjer logging
var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>().CreateLogger("VaultLogger");

// Vault konfiguration
var httpClientHandler = new HttpClientHandler();
// Get Vault address from configuration or use default
string vaultAddress = builder.Configuration["Vault__Address"] ?? "https://vaulthost:8201/";
logger.LogInformation($"Using Vault address: {vaultAddress}");

httpClientHandler.ServerCertificateCustomValidationCallback =
(message, cert, chain, sslPolicyErrors) => { return true; };

// Konfigurer Vault klienten
IAuthMethodInfo authMethod =
new TokenAuthMethodInfo("00000000-0000-0000-0000-000000000000");
var vaultClientSettings = new VaultClientSettings(vaultAddress, authMethod)
{
    Namespace = "",
    MyHttpClientProviderFunc = handler
    => new HttpClient(httpClientHandler)
    {
        BaseAddress = new Uri(vaultAddress)
    }
};
IVaultClient vaultClient = new VaultClient(vaultClientSettings);

try
{
    // Henter hemmeligheder fra Vault
    logger.LogInformation("Henter hemmeligheder fra Vault...");
    Secret<SecretData> secretData = await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(path:"Secrets", mountPoint: "secret");

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

builder.Services.AddAuthorization();
builder.Services.AddScoped<IUserDBRepository, UserMongoDBService>();
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration["MongoConnectionString"] ?? "mongodb://admin:1234@localhost:27018/";
    return new MongoClient(connectionString);
});

builder.Services.AddHttpClient();
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

// These middleware calls are required for JWT authentication to work
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();