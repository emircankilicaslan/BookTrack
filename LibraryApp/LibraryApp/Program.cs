using System.Security.Claims;
using System.Threading.RateLimiting;
using LibraryApp.Data;
using LibraryApp.Models;
using LibraryApp.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.Configure<LibraryApp.Services.LibraryOptions>(builder.Configuration.GetSection(LibraryApp.Services.LibraryOptions.SectionName));
builder.Services.AddDbContext<LibraryContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "POST /api/auth/login ile alınan token.",
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
    });
});

// ---- Kimlik doğrulama: JWT (HS256) + rol bazlı yetki. Anahtar yoksa uygulama başlamaz.
var signingKey = TokenService.SigningKey(builder.Configuration["Jwt:Secret"]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<TokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = TokenService.Issuer,
            ValidateAudience = true, ValidAudience = TokenService.Audience,
            ValidateLifetime = true, RequireExpirationTime = true,
            ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = "name", RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        o.Events = new JwtBearerEvents
        {
            // Her istekte kullanıcı veritabanından doğrulanır: pasife alınan ya da silinen hesabın token'ı süresi dolmadan reddedilir.
            OnTokenValidated = async ctx =>
            {
                if (!int.TryParse(ctx.Principal?.FindFirstValue("sub"), out var id)) { ctx.Fail("Token konusu geçersiz."); return; }
                var db = ctx.HttpContext.RequestServices.GetRequiredService<LibraryContext>();
                var user = await db.Users.AsNoTracking().Where(u => u.Id == id)
                    .Select(u => new { u.IsActive, u.Role }).FirstOrDefaultAsync();
                if (user is null || !user.IsActive || user.Role != ctx.Principal!.FindFirstValue("role"))
                    ctx.Fail("Hesap pasif, bulunamadı veya yetkisi değişti.");
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Kaba kuvvet denemelerine karşı: giriş ucunda IP başına dakikada sınırlı istek (bellekte; tek örnek için yeterli).
var girisLimiti = builder.Configuration.GetValue("RateLimit:LoginPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "bilinmiyor",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = girisLimiti, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// Docker/CI için isteğe bağlı: Database:MigrateOnStartup=true ise bekleyen migration'lar açılışta uygulanır.
// Varsayılan kapalıdır; üretimde migration'lar bilinçli olarak (dotnet ef database update) çalıştırılır.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LibraryContext>().Database.MigrateAsync();
}

// Veritabanında aktif Admin yoksa ve Auth:AdminUsername/AdminPassword verilmişse ilk yönetici oluşturulur.
await AdminBootstrap.EnsureAdminAsync(app.Services, app.Configuration, app.Logger);

// Güvenlik başlıkları. Web arayüzü yalnızca kendi kaynaklarını yükler; Swagger UI satır içi betik kullandığı için kapsam dışı.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (!ctx.Request.Path.StartsWithSegments("/swagger"))
        ctx.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    await next();
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Konteynerde TLS genellikle önündeki ters vekilde sonlanır; HttpsRedirection:Enabled=false ile kapatılabilir.
if (app.Configuration.GetValue("HttpsRedirection:Enabled", true)) app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
