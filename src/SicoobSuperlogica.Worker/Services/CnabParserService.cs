using System.Text;
using FluentValidation;
using Microsoft.Extensions.Logging;
using SicoobSuperlogica.Worker.Models;

namespace SicoobSuperlogica.Worker.Services;

public interface ICnabParserService
{
    /// <summary>
    /// Lê o stream de um arquivo CNAB 240 e retorna os registros válidos extraídos
    /// dos pares de segmentos T+U. O stream é consumido e sua posição avança até o fim.
    /// </summary>
    IReadOnlyList<CnabRegistro> Parsear(Stream cnabStream);
}

public sealed class CnabParserService : ICnabParserService
{
    // Posições 1-indexadas conforme layout FEBRABAN/Sicoob CNAB 240 Cobrança – Retorno
    private const int LinhaLen = 240;

    // Offsets 0-indexados usados internamente
    private const int TipoRegistroIdx = 7;   // posição 8
    private const int SegmentoIdx = 13;       // posição 14

    private readonly CnabRegistroValidator _validator = new();
    private readonly ILogger<CnabParserService> _logger;

    public CnabParserService(ILogger<CnabParserService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<CnabRegistro> Parsear(Stream cnabStream)
    {
        var registros = new List<CnabRegistro>();
        SegmentoTRaw? pendingT = null;
        var numLinha = 0;

        // CNAB 240 é tradicionalmente Latin-1 (ISO-8859-1)
        using var reader = new StreamReader(cnabStream, Encoding.Latin1, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        while (reader.ReadLine() is { } linha)
        {
            numLinha++;

            if (string.IsNullOrWhiteSpace(linha)) continue;

            if (linha.Length < LinhaLen)
            {
                _logger.LogWarning("Linha {Num} ignorada: tamanho {Len} < {Esperado}",
                    numLinha, linha.Length, LinhaLen);
                pendingT = null;
                continue;
            }

            var tipoRegistro = linha[TipoRegistroIdx];

            // Somente registros detalhe (tipo 3) contêm segmentos T/U
            if (tipoRegistro != '3')
            {
                pendingT = null;
                continue;
            }

            var segmento = linha[SegmentoIdx];

            if (segmento == 'T')
            {
                if (pendingT is not null)
                    _logger.LogWarning("Linha {Num}: segmento T sem U correspondente descartado (lote {Lote})",
                        numLinha, pendingT.LoteNumero);

                pendingT = ExtrairSegmentoT(linha);
            }
            else if (segmento == 'U')
            {
                if (pendingT is null)
                {
                    _logger.LogWarning("Linha {Num}: segmento U sem T precedente — ignorado", numLinha);
                    continue;
                }

                var u = ExtrairSegmentoU(linha);
                var registro = CombinarTU(pendingT, u);
                pendingT = null;

                var resultado = _validator.Validate(registro);
                if (!resultado.IsValid)
                {
                    _logger.LogWarning(
                        "Registro {Seq} (lote {Lote}) inválido — ignorado: {Erros}",
                        registro.SeqNoLote, registro.LoteNumero,
                        string.Join("; ", resultado.Errors.Select(e => e.ErrorMessage)));
                    continue;
                }

                registros.Add(registro);
            }
            else
            {
                pendingT = null;
            }
        }

        if (pendingT is not null)
            _logger.LogWarning("Segmento T sem U no final do arquivo (lote {Lote}, seq {Seq})",
                pendingT.LoteNumero, pendingT.SeqNoLote);

        return registros;
    }

    // -------------------------------------------------------------------------
    // Extração de campos — layout Sicoob CNAB 240 Cobrança Bancária V2 Retorno
    // Posições 1-indexadas conforme manual; internamente usamos 0-indexed.
    // -------------------------------------------------------------------------

    private static SegmentoTRaw ExtrairSegmentoT(string linha)
    {
        return new SegmentoTRaw(
            LoteNumero:     ParseInt(linha, 3, 4),
            SeqNoLote:      ParseInt(linha, 8, 5),
            NossoNumero:    linha.Substring(34, 23).Trim(),     // pos 35-57
            SeuNumero:      linha.Substring(57, 15).Trim(),     // pos 58-72
            CodigoMovimento: linha.Substring(15, 2),            // pos 16-17
            Vencimento:     ParseData(linha, 97, 8),            // pos 98-105
            DataLiquidacao: ParseData(linha, 126, 8),           // pos 127-134
            ValorTitulo:    ParseDecimal(linha, 105, 15),       // pos 106-120
            ValorLiquidado: ParseDecimal(linha, 134, 15),       // pos 135-149
            ValorJuros:     ParseDecimal(linha, 165, 15),       // pos 166-180
            ValorDesconto:  ParseDecimal(linha, 180, 15),       // pos 181-195
            ValorAbatimento: ParseDecimal(linha, 195, 15),      // pos 196-210
            ValorIOF:       ParseDecimal(linha, 210, 15)        // pos 211-225
        );
    }

    private static SegmentoURaw ExtrairSegmentoU(string linha)
    {
        return new SegmentoURaw(
            DataOcorrencia: ParseData(linha, 125, 8),           // pos 126-133
            DataCredito:    ParseData(linha, 133, 8),           // pos 134-141
            ValorLiquidoCredito: ParseDecimal(linha, 77, 15)    // pos 78-92
        );
    }

    private static CnabRegistro CombinarTU(SegmentoTRaw t, SegmentoURaw u)
    {
        return new CnabRegistro(
            LoteNumero:         t.LoteNumero,
            SeqNoLote:          t.SeqNoLote,
            NossoNumero:        t.NossoNumero,
            SeuNumero:          t.SeuNumero,
            CodigoMovimento:    t.CodigoMovimento,
            Vencimento:         t.Vencimento,
            DataLiquidacao:     t.DataLiquidacao,
            DataOcorrencia:     u.DataOcorrencia,
            DataCredito:        u.DataCredito,
            ValorTitulo:        t.ValorTitulo,
            ValorLiquidado:     t.ValorLiquidado,
            ValorJuros:         t.ValorJuros,
            ValorDesconto:      t.ValorDesconto,
            ValorAbatimento:    t.ValorAbatimento,
            ValorIOF:           t.ValorIOF,
            ValorLiquidoCredito: u.ValorLiquidoCredito
        );
    }

    // -------------------------------------------------------------------------
    // Helpers de parse
    // -------------------------------------------------------------------------

    private static int ParseInt(string linha, int start, int length)
    {
        var s = linha.Substring(start, length).Trim();
        return int.TryParse(s, out var v) ? v : 0;
    }

    /// <summary>Converte 15 dígitos sem ponto decimal (2 casas implícitas) em decimal.</summary>
    private static decimal ParseDecimal(string linha, int start, int length)
    {
        var s = linha.Substring(start, length).Trim();
        if (!long.TryParse(s, out var centavos)) return 0m;
        return centavos / 100m;
    }

    /// <summary>Converte data no formato DDMMAAAA para DateOnly. Retorna null se inválida.</summary>
    private static DateOnly? ParseData(string linha, int start, int length)
    {
        var s = linha.Substring(start, length);
        if (s == "00000000" || string.IsNullOrWhiteSpace(s)) return null;

        if (int.TryParse(s[0..2], out var dia)
            && int.TryParse(s[2..4], out var mes)
            && int.TryParse(s[4..8], out var ano)
            && DateOnly.TryParse($"{ano:D4}-{mes:D2}-{dia:D2}", out var data))
            return data;

        return null;
    }

    // -------------------------------------------------------------------------
    // DTOs internos de parse
    // -------------------------------------------------------------------------

    private sealed record SegmentoTRaw(
        int LoteNumero, int SeqNoLote,
        string NossoNumero, string SeuNumero, string CodigoMovimento,
        DateOnly? Vencimento, DateOnly? DataLiquidacao,
        decimal ValorTitulo, decimal ValorLiquidado,
        decimal ValorJuros, decimal ValorDesconto, decimal ValorAbatimento, decimal ValorIOF);

    private sealed record SegmentoURaw(
        DateOnly? DataOcorrencia, DateOnly? DataCredito, decimal ValorLiquidoCredito);
}

// -------------------------------------------------------------------------
// Validador FluentValidation
// -------------------------------------------------------------------------

internal sealed class CnabRegistroValidator : AbstractValidator<CnabRegistro>
{
    public CnabRegistroValidator()
    {
        RuleFor(x => x.NossoNumero)
            .NotEmpty().WithMessage("Nosso número é obrigatório");

        RuleFor(x => x.CodigoMovimento)
            .NotEmpty().WithMessage("Código de movimento é obrigatório")
            .Length(2).WithMessage("Código de movimento deve ter 2 caracteres");

        RuleFor(x => x.ValorTitulo)
            .GreaterThan(0m).WithMessage("Valor do título deve ser positivo");
    }
}
