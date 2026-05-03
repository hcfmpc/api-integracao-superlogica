using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SicoobSuperlogica.Worker.Services;

namespace SicoobSuperlogica.Tests.Unit;

/// <summary>
/// Testa o parser CNAB 240 com fixtures de texto posicional.
/// Layout: Sicoob Cobrança Bancária V2 – Arquivo de Retorno.
/// </summary>
public class CnabParserServiceTests
{
    private readonly CnabParserService _sut = new(NullLogger<CnabParserService>.Instance);

    // -----------------------------------------------------------------------
    // Helpers de fixture CNAB 240 – segmentos T e U, 240 chars exatos
    // -----------------------------------------------------------------------

    /// <summary>
    /// Segmento T padrão (código de movimento "06" = liquidação normal).
    /// Posições 1-indexadas conforme layout Sicoob CNAB 240.
    /// </summary>
    private static string SegmentoT(
        string nossoNumero = "00000000000000000000001",   // pos 35-57  (23 chars)
        string seuNumero   = "123456789012345",            // pos 58-72  (15 chars)
        string codigoMov   = "06",                         // pos 16-17  (2 chars)
        string vencimento  = "01052026",                   // pos 98-105 (DDMMAAAA)
        string valorTitulo = "000000000010000",            // pos 106-120 (centavos)
        string dataSaldo   = "02052026",                   // pos 127-134
        string valorSaldo  = "000000000010000",            // pos 135-149
        string lote        = "0001",
        string seq         = "00001")
    {
        // 240 chars built field by field
        // pos 1-3
        var sb = new StringBuilder(240);
        sb.Append("756");                      // 3  → pos 1-3
        sb.Append(lote.PadLeft(4, '0'));       // 4  → pos 4-7
        sb.Append('3');                        // 1  → pos 8
        sb.Append(seq.PadLeft(5, '0'));        // 5  → pos 9-13
        sb.Append('T');                        // 1  → pos 14
        sb.Append('0');                        // 1  → pos 15
        sb.Append(codigoMov);                  // 2  → pos 16-17
        sb.Append("1234");                     // 4  → pos 18-21
        sb.Append('5');                        // 1  → pos 22
        sb.Append("0000001234");               // 10 → pos 23-32
        sb.Append('6');                        // 1  → pos 33
        sb.Append(' ');                        // 1  → pos 34
        sb.Append(nossoNumero.PadLeft(23));    // 23 → pos 35-57
        sb.Append(seuNumero.PadRight(15));     // 15 → pos 58-72
        sb.Append("PAGADOR NOME TESTE  ");     // 20 → pos 73-92
        sb.Append("09000");                    // 5  → pos 93-97
        sb.Append(vencimento);                 // 8  → pos 98-105
        sb.Append(valorTitulo);               // 15 → pos 106-120
        sb.Append("756");                      // 3  → pos 121-123
        sb.Append("000");                      // 3  → pos 124-126
        sb.Append(dataSaldo);                  // 8  → pos 127-134
        sb.Append(valorSaldo);                // 15 → pos 135-149
        sb.Append("000000000000000");          // 15 → pos 150-164
        sb.Append(' ');                        // 1  → pos 165
        sb.Append("000000000000100");          // 15 → pos 166-180
        sb.Append("000000000000000");          // 15 → pos 181-195
        sb.Append("000000000000000");          // 15 → pos 196-210
        sb.Append("000000000000000");          // 15 → pos 211-225
        sb.Append(codigoMov);                  // 2  → pos 226-227
        sb.Append("             ");            // 13 → pos 228-240

        var linha = sb.ToString();
        linha.Should().HaveLength(240, "fixture de segmento T deve ter 240 chars");
        return linha;
    }

    /// <summary>Segmento U complementar padrão.</summary>
    private static string SegmentoU(
        string dataOcorrencia = "02052026",                // pos 126-133
        string dataCredito    = "03052026",                // pos 134-141
        string valorLiquido   = "000000000010100",         // pos 78-92  (101.00)
        string lote           = "0001",
        string seq            = "00002")
    {
        var sb = new StringBuilder(240);
        sb.Append("756");                      // 3  → pos 1-3
        sb.Append(lote.PadLeft(4, '0'));       // 4  → pos 4-7
        sb.Append('3');                        // 1  → pos 8
        sb.Append(seq.PadLeft(5, '0'));        // 5  → pos 9-13
        sb.Append('U');                        // 1  → pos 14
        sb.Append('0');                        // 1  → pos 15
        sb.Append("06");                       // 2  → pos 16-17
        sb.Append("000000000000100");          // 15 → pos 18-32  [juros acumulados]
        sb.Append("000000000000000");          // 15 → pos 33-47  [desconto concedido]
        sb.Append("000000000000000");          // 15 → pos 48-62  [abatimento]
        sb.Append("000000000000000");          // 15 → pos 63-77  [IOF]
        sb.Append(valorLiquido);              // 15 → pos 78-92  [valor líquido]
        sb.Append("000000000000000");          // 15 → pos 93-107 [outras despesas]
        sb.Append("000000000000000");          // 15 → pos 108-122[outros créditos]
        sb.Append("   ");                      // 3  → pos 123-125
        sb.Append(dataOcorrencia);             // 8  → pos 126-133
        sb.Append(dataCredito);               // 8  → pos 134-141
        sb.Append("     ");                    // 5  → pos 142-146
        sb.Append("0000000001");               // 10 → pos 147-156 [doc pagador]
        sb.Append("00001");                    // 5  → pos 157-161 [ag pagador]
        sb.Append("000000001234");             // 12 → pos 162-173 [cc pagador]
        sb.Append('5');                        // 1  → pos 174
        sb.Append("PAGADOR NOME COMPLETO AQUI T    "); // 32 chars → pos 175-206 … need 40
        sb.Append("        ");                 // 8 → fill to 40 for pos 175-214
        sb.Append("MENSAGEM PARA PAGADOR     "); // 26 → pos 215-240

        var linha = sb.ToString();
        linha.Should().HaveLength(240, "fixture de segmento U deve ter 240 chars");
        return linha;
    }

    private static Stream ToStream(params string[] linhas)
    {
        var texto = string.Join("\n", linhas) + "\n";
        return new MemoryStream(Encoding.Latin1.GetBytes(texto));
    }

    // -----------------------------------------------------------------------
    // Testes
    // -----------------------------------------------------------------------

    [Fact]
    public void Parsear_ArquivoVazio_RetornaListaVazia()
    {
        var stream = ToStream();
        var resultado = _sut.Parsear(stream);
        resultado.Should().BeEmpty();
    }

    [Fact]
    public void Parsear_SomenteHeaderTrailer_RetornaListaVazia()
    {
        // Cabeçalho arquivo (tipo 0) + rodapé arquivo (tipo 9) sem detalhes
        var header = "756" + "0000" + "0" + new string(' ', 232);
        var trailer = "756" + "9999" + "9" + new string(' ', 232);

        header.Should().HaveLength(240);
        trailer.Should().HaveLength(240);

        var stream = ToStream(header, trailer);
        var resultado = _sut.Parsear(stream);
        resultado.Should().BeEmpty();
    }

    [Fact]
    public void Parsear_ParTUValido_RetornaUmRegistroComCamposCorretos()
    {
        var stream = ToStream(SegmentoT(), SegmentoU());
        var resultado = _sut.Parsear(stream);

        resultado.Should().HaveCount(1);
        var reg = resultado[0];

        reg.NossoNumero.Should().Be("00000000000000000000001");
        reg.SeuNumero.Should().Be("123456789012345");
        reg.CodigoMovimento.Should().Be("06");
        reg.Vencimento.Should().Be(new DateOnly(2026, 5, 1));
        reg.DataLiquidacao.Should().Be(new DateOnly(2026, 5, 2));
        reg.DataOcorrencia.Should().Be(new DateOnly(2026, 5, 2));
        reg.DataCredito.Should().Be(new DateOnly(2026, 5, 3));
        reg.ValorTitulo.Should().Be(100.00m);
        reg.ValorLiquidado.Should().Be(100.00m);
        reg.ValorJuros.Should().Be(1.00m);
        reg.ValorLiquidoCredito.Should().Be(101.00m);
    }

    [Fact]
    public void Parsear_MultiplosParesTU_RetornaTodosRegistros()
    {
        var stream = ToStream(
            SegmentoT(nossoNumero: "00000000000000000000001", seq: "00001"),
            SegmentoU(seq: "00002"),
            SegmentoT(nossoNumero: "00000000000000000000002", seq: "00003"),
            SegmentoU(seq: "00004"));

        var resultado = _sut.Parsear(stream);

        resultado.Should().HaveCount(2);
        resultado[0].NossoNumero.Should().Be("00000000000000000000001");
        resultado[1].NossoNumero.Should().Be("00000000000000000000002");
    }

    [Fact]
    public void Parsear_NossoNumeroVazio_RegistroIgnorado()
    {
        // NossoNumero em branco → inválido (ValidatorRule NotEmpty)
        var t = SegmentoT(nossoNumero: new string(' ', 23));
        var stream = ToStream(t, SegmentoU());

        var resultado = _sut.Parsear(stream);

        resultado.Should().BeEmpty("registro com nosso número vazio deve ser descartado");
    }

    [Fact]
    public void Parsear_ValorTituloZero_RegistroIgnorado()
    {
        var t = SegmentoT(valorTitulo: "000000000000000");
        var stream = ToStream(t, SegmentoU());

        var resultado = _sut.Parsear(stream);

        resultado.Should().BeEmpty("registro com valor zero deve ser descartado");
    }

    [Fact]
    public void Parsear_LinhaTruncada_LinhaIgnorada()
    {
        var linhaCurta = "756000130000100001T".PadRight(100); // apenas 100 chars
        var stream = ToStream(linhaCurta, SegmentoT(), SegmentoU());

        var resultado = _sut.Parsear(stream);

        // A linha truncada é ignorada; o par T+U seguinte é parseado normalmente
        resultado.Should().HaveCount(1);
    }

    [Fact]
    public void Parsear_SegmentoTSemU_UltimoTDescartado()
    {
        // T sem U correspondente: nenhum registro gerado
        var stream = ToStream(SegmentoT());

        var resultado = _sut.Parsear(stream);

        resultado.Should().BeEmpty();
    }

    [Fact]
    public void Parsear_DataInvalida_DataNula()
    {
        // vencimento "00000000" → DateOnly? null
        var t = SegmentoT(vencimento: "00000000");
        var stream = ToStream(t, SegmentoU());

        var resultado = _sut.Parsear(stream);

        // ValorTitulo > 0, NossoNumero não vazio → registro válido mas Vencimento nulo
        resultado.Should().HaveCount(1);
        resultado[0].Vencimento.Should().BeNull();
    }
}
