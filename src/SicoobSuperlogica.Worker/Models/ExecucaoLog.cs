namespace SicoobSuperlogica.Worker.Models;

public class ExecucaoLog
{
    public int Id { get; set; }
    public int CondominioId { get; set; }
    public string DataInicial { get; set; } = string.Empty;
    public string DataFinal { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? TotalRegistros { get; set; }
    public string? MensagemErro { get; set; }
    public DateTime ExecutadoEm { get; set; }
}
