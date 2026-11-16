using LibraryApp.Data;
using LibraryApp.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.Configure<LibraryApp.Services.LibraryOptions>(builder.Configuration.GetSection(LibraryApp.Services.LibraryOptions.SectionName));
builder.Services.AddDbContext<LibraryContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Docker/CI için isteğe bağlı: Database:MigrateOnStartup=true ise bekleyen migration'lar açılışta uygulanır.
// Varsayılan kapalıdır; üretimde migration'lar bilinçli olarak (dotnet ef database update) çalıştırılır.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<LibraryContext>().Database.MigrateAsync();
}

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
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
