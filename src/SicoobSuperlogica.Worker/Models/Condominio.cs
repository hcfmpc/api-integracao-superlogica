namespace SicoobSuperlogica.Worker.Models;

public class Condominio
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string NumeroContrato { get; set; } = string.Empty;
    public string NumeroConta { get; set; } = string.Empty;
    public string Cooperativa { get; set; } = string.Empty;
    public byte[] CredenciaisEnc { get; set; } = [];
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }

    // Decrypted at runtime — never persisted
    public CondominioCredenciais? Credenciais { get; set; }
}

public record CondominioCredenciais(
    string ClientId,
    string ClientSecret,
    string CertificatePath,
    string CertificatePassword,
    string AppTokenSuperlogica,
    string AccessTokenSuperlogica
);
