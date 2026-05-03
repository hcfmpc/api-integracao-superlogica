namespace SicoobSuperlogica.Worker.Models;

/// <summary>
/// Dados extraídos de um par T+U do arquivo de retorno CNAB 240 Sicoob.
/// Campos financeiros e de PII ficam somente em memória — nunca persistidos ou logados.
/// </summary>
public record CnabRegistro(
    int LoteNumero,
    int SeqNoLote,
    string NossoNumero,
    string SeuNumero,
    string CodigoMovimento,
    DateOnly? Vencimento,
    DateOnly? DataLiquidacao,
    DateOnly? DataOcorrencia,
    DateOnly? DataCredito,
    decimal ValorTitulo,
    decimal ValorLiquidado,
    decimal ValorJuros,
    decimal ValorDesconto,
    decimal ValorAbatimento,
    decimal ValorIOF,
    decimal ValorLiquidoCredito
);
