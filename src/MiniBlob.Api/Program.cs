using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using MiniBlob.Api.Services;
using MiniBlob.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Early validation of MiniBlob options (moved outside Configure)
var rootPath = builder.Configuration.GetValue<string>("MiniBlob:RootPath") ?? "Storage";

// Enforce absolute paths in all environments
if (!Path.IsPathRooted(rootPath)) {
    throw new InvalidOperationException(
        $"Relative storage path '{rootPath}' is not allowed. " +
        "Please specify an absolute path in appsettings.json. " +
        "Examples: '/var/miniblob/storage' (Linux), 'C:\\MiniBlob\\Storage' (Windows), " +
        "or use user profile path like: " +
        $"'{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MiniBlobStorage")}'"
    );
}

// Verify the directory exists
if (!Directory.Exists(rootPath)) {
    throw new InvalidOperationException(
        $"Storage path '{rootPath}' does not exist. " +
        "Please create the directory before starting the application."
    );
}

// Bind options to MiniBlobOptions
builder.Services.Configure<MiniBlobOptions>(builder.Configuration.GetSection("MiniBlob"));


// Serilog setup
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Determine if indexing is enabled
bool enableSearchIndexing = builder.Configuration.GetValue<bool>("MiniBlob:EnableSearchIndexing");

// File auth service
builder.Services.AddSingleton<FileMetadataAuthService>();

// File storage service
builder.Services.AddScoped<IStorageService, FileSystemStorageService>();

// Optional EF Core search index
if (enableSearchIndexing) {
    var conn = builder.Configuration.GetConnectionString("DefaultConnection")
               ?? "Data Source=Data/mini_blob_ef.db";
    builder.Services.AddDbContext<BlobDbContext>(options => options.UseSqlite(conn));
    builder.Services.AddScoped<ISearchIndex, EfSearchIndex>();
} else {
    builder.Services.AddScoped<ISearchIndex, NoOpSearchIndex>();
}

// JWT Authentication setup
var jwtKey = builder.Configuration["Jwt:Key"] ?? "abcdefghijklmnopqrstuvwx12345678";
var issuer = builder.Configuration["Jwt:Issuer"] ?? "mini-blob";
var audience = builder.Configuration["Jwt:Audience"] ?? "mini-blob-audience";

builder.Services.AddAuthentication(options => {
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => {
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true,
    };
});

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.MapControllers();

// Apply DB creation only when indexing is enabled
if (enableSearchIndexing) {
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BlobDbContext>();
    db.Database.EnsureCreated(); // Use Migrate() if migrations exist
}
app.MapGet("/", () => "MiniBlob API is running.");

app.Run();

public partial class Program { }
