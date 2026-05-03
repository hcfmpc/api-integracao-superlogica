namespace SicoobSuperlogica.Worker.Models;

public record SolicitacaoRetornoRequest(
    string NumeroContrato,
    string NumeroConta,
    string DataInicial,
    string DataFinal
);

public record SolicitacaoRetornoResponse(
    string IdSolicitacao,
    string Status
);

public record PollingRetornoResponse(
    string Status,
    string? UrlDownload
);

public static class SicoobRetornoStatus
{
    public const string EmProcessamento = "EM_PROCESSAMENTO";
    public const string Gerado = "GERADO";
    public const string SemMovimento = "SEM_MOVIMENTO";
}
