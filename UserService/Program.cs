using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using NLog;
using NLog.Web;
using Services;
using System.Text;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Token;
using VaultSharp.V1.Commons;

// --- NLog: Setup NLog for early logging if needed ---
var earlyLogger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
earlyLogger.Debug("UserService: Initializing Program.cs");

var builder = WebApplication.CreateBuilder(args);

// --- Configure Logging (NLog integration) ---
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// --- Vault Configuration & Secret Fetching ---
var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
logger.LogInformation("UserService: Attempting to configure Vault...");

// Get Vault connection details from environment variables (set by docker-compose)
string vaultAddress = builder.Configuration["Vault:Address"] ?? "https://vaulthost:8201";
string vaultToken = builder.Configuration["Vault:Token"];

if (string.IsNullOrEmpty(vaultToken))
{
    logger.LogError("UserService: Vault:Token is NOT configured in environment variables. Cannot authenticate with Vault. Ensure Vault__Token is set in docker-compose.yml.");
    throw new InvalidOperationException("Vault token is not configured. Application cannot start.");
}
logger.LogInformation($"UserService: Using Vault Address: {vaultAddress}");
logger.LogInformation("UserService: Using Vault Token (length): {VaultTokenLength}", vaultToken.Length);


var httpClientHandler = new HttpClientHandler();
httpClientHandler.ServerCertificateCustomValidationCallback =
    (message, cert, chain, sslPolicyErrors) =>
    {
        logger.LogWarning("UserService: Bypassing Vault SSL certificate validation. [Development ONLY]");
        return true;
    };

IAuthMethodInfo authMethod = new TokenAuthMethodInfo(vaultToken);
var vaultClientSettings = new VaultClientSettings(vaultAddress, authMethod)
{
    Namespace = "",
    MyHttpClientProviderFunc = handler => new HttpClient(httpClientHandler) { BaseAddress = new Uri(vaultAddress) }
};
IVaultClient vaultClient = new VaultClient(vaultClientSettings);

try
{
    logger.LogInformation("UserService: Fetching JWT parameters from Vault path 'Secrets'...");
    Secret<SecretData> jwtParamsSecret = await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(
        path: "Secrets",
        mountPoint: "secret"
    );
    string? jwtSecretKey = jwtParamsSecret.Data.Data["Secret"]?.ToString();
    string? jwtIssuer = jwtParamsSecret.Data.Data["Issuer"]?.ToString();
    string? jwtAudience = jwtParamsSecret.Data.Data["Audience"]?.ToString();

    if (string.IsNullOrEmpty(jwtSecretKey)) throw new InvalidOperationException("JWT Secret not found in Vault at secret/Secrets.");
    if (string.IsNullOrEmpty(jwtIssuer)) throw new InvalidOperationException("JWT Issuer not found in Vault at secret/Secrets.");
    if (string.IsNullOrEmpty(jwtAudience)) throw new InvalidOperationException("JWT Audience not found in Vault at secret/Secrets.");

    builder.Configuration["JwtSettings:Secret"] = jwtSecretKey;
    builder.Configuration["JwtSettings:Issuer"] = jwtIssuer;
    builder.Configuration["JwtSettings:Audience"] = jwtAudience;
    logger.LogInformation("UserService: JWT parameters loaded from Vault.");

    logger.LogInformation("UserService: Fetching Connection parameters from Vault path 'Connections'...");
    Secret<SecretData> connectionParamsSecret = await vaultClient.V1.Secrets.KeyValue.V2.ReadSecretAsync(
        path: "Connections",
        mountPoint: "secret"
    );
    string? mongoConnectionString = connectionParamsSecret.Data.Data["mongoConnectionString"]?.ToString();
    string? mongoDbName = connectionParamsSecret.Data.Data["MongoDbDatabaseName"]?.ToString();
    string? authServiceUrl = connectionParamsSecret.Data.Data["AuthServiceUrl"]?.ToString();

    if (string.IsNullOrEmpty(mongoConnectionString)) throw new InvalidOperationException("mongoConnectionString not found in Vault at secret/Connections.");
    if (string.IsNullOrEmpty(mongoDbName)) throw new InvalidOperationException("MongoDbDatabaseName not found in Vault at secret/Connections.");
    if (string.IsNullOrEmpty(authServiceUrl)) throw new InvalidOperationException("AuthServiceUrl not found in Vault at secret/Connections.");

    builder.Configuration["MongoDb:ConnectionString"] = mongoConnectionString;
    builder.Configuration["MongoDb:DatabaseName"] = mongoDbName;
    builder.Configuration["AuthServiceUrl"] = authServiceUrl;
    logger.LogInformation("UserService: Connection parameters (MongoDB, AuthServiceUrl) loaded from Vault.");

}
catch (Exception ex)
{
    logger.LogCritical(ex, "UserService: CRITICAL ERROR fetching secrets from Vault. Application cannot start properly.");
    throw;
}


// --- Add services to the container ---
builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Configure CORS policy to allow your frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowCors", policy =>
    {
        policy.WithOrigins("http://localhost:8081", "http://localhost:8080", "https://localhost:8081", "http://localhost:4000", "http://localhost:5162", "http://localhost:8201") // Added 8081 first
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Configure JWT Authentication (UserService validates tokens issued by AuthService)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:Secret"])) // Read from config
        };
    });
logger.LogInformation("UserService: JWT Authentication services configured.");

builder.Services.AddAuthorization();

// MongoDB Configuration
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration["MongoDb:ConnectionString"]; 
    var dbName = configuration["MongoDb:DatabaseName"]; 

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        logger.LogCritical("UserService: MongoDb:ConnectionString is missing from configuration after Vault fetch. Application cannot connect to DB.");
        throw new InvalidOperationException("MongoDb:ConnectionString is missing. Check Vault configuration at secret/Connections.");
    }
    logger.LogInformation($"UserService: Configuring IMongoClient for database: {dbName} (ConnectionString retrieved).");
    return new MongoClient(connectionString);
});
builder.Services.AddScoped<IUserDBRepository, UserMongoDBService>();

// HttpClient for the Login PageModel within UserService to call AuthService
builder.Services.AddHttpClient("AuthApiClient", (serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var authUrl = configuration["AuthServiceUrl"]; 

    if (!string.IsNullOrEmpty(authUrl))
    {
        client.BaseAddress = new Uri(authUrl);
        logger.LogInformation($"UserService: AuthApiClient BaseAddress set to: {authUrl}");
    }
    else
    {
        logger.LogCritical("UserService: AuthServiceUrl for AuthApiClient NOT FOUND in IConfiguration. Login page will fail. Check Vault at secret/Connections for key 'AuthServiceUrl'.");
        throw new InvalidOperationException("AuthServiceUrl for AuthApiClient is not configured. Check Vault.");
    }
});

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "UserService API & UI", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter JWT",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseCors("AllowCors");

// --- Configure the HTTP request pipeline ---
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "UserService API & UI v1"));
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

logger.LogInformation("UserService: Application starting...");
app.Run();