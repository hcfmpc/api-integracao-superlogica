using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SicoobSuperlogica.Worker.Configuration;
using SicoobSuperlogica.Worker.Models;

namespace SicoobSuperlogica.Worker.Services;

public interface IAuthService
{
    Task<string> ObterTokenAsync(CondominioCredenciais credenciais, CancellationToken ct);
    void InvalidarToken(string clientId);
}

public sealed class AuthService : IAuthService, IDisposable
{
    private readonly SicoobSettings _settings;
    private readonly ILogger<AuthService> _logger;
    private readonly Dictionary<string, TokenCache> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AuthService(SicoobSettings settings, ILogger<AuthService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> ObterTokenAsync(CondominioCredenciais credenciais, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(credenciais.ClientId, out var cached) && !cached.IsExpired)
                return cached.AccessToken;

            var token = await FetchTokenAsync(credenciais, ct);
            _cache[credenciais.ClientId] = token;
            return token.AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void InvalidarToken(string clientId) => _cache.Remove(clientId);

    private async Task<TokenCache> FetchTokenAsync(CondominioCredenciais credenciais, CancellationToken ct)
    {
        using var handler = BuildHandler(credenciais);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = credenciais.ClientId
        });

        _logger.LogInformation("Solicitando token Sicoob para client_id {ClientId}", credenciais.ClientId);

        var response = await client.PostAsync(_settings.TokenUrl, body, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        var accessToken = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("access_token ausente na resposta.");
        var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

        return new TokenCache(accessToken, DateTime.UtcNow.AddSeconds(expiresIn - 30));
    }

    private static HttpClientHandler BuildHandler(CondominioCredenciais credenciais)
    {
        var handler = new HttpClientHandler();

        if (!string.IsNullOrWhiteSpace(credenciais.CertificatePath) &&
            File.Exists(credenciais.CertificatePath))
        {
            var cert = new X509Certificate2(
                credenciais.CertificatePath,
                credenciais.CertificatePassword,
                X509KeyStorageFlags.MachineKeySet);
            handler.ClientCertificates.Add(cert);
        }

        return handler;
    }

    public void Dispose() => _lock.Dispose();

    private sealed record TokenCache(string AccessToken, DateTime ExpiresAt)
    {
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }
}
