using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LibraryApp.Tests;

/// <summary>Testlerde kimlik doğrulama: yapılandırma ve oturum açmış istemci üretimi.</summary>
public static class TestAuth
{
    public const string AdminUser = "yonetici";
    public const string AdminPassword = "Test-Yonetici-Parola-1";
    public const string JwtSecret = "test-only-secret-test-only-secret-1234567890";

    /// <summary>JWT anahtarı ve ilk yönetici ayarları; giriş hız sınırı testlerde gevşetilir.</summary>
    public static void Configure(IWebHostBuilder builder, int loginPerMinute = 1000)
    {
        builder.UseSetting("Jwt:Secret", JwtSecret);
        builder.UseSetting("Auth:AdminUsername", AdminUser);
        builder.UseSetting("Auth:AdminPassword", AdminPassword);
        builder.UseSetting("RateLimit:LoginPerMinute", loginPerMinute.ToString());
    }

    public static async Task<string> LoginTokenAsync(HttpClient client, string user, string password)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    public static async Task<HttpClient> LoginAsync(HttpClient client, string user, string password)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginTokenAsync(client, user, password));
        return client;
    }

    public static Task<HttpClient> AdminAsync(WebApplicationFactory<Program> factory) =>
        LoginAsync(factory.CreateClient(), AdminUser, AdminPassword);

    /// <summary>Xunit sınıf kurucularında kullanılmak üzere eşzamanlı sürüm.</summary>
    public static HttpClient Admin(WebApplicationFactory<Program> factory) => AdminAsync(factory).GetAwaiter().GetResult();
}
