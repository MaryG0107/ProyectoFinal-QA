using FlashStore.Cli.Commands;
using FlashStore.Cli.Core;
using static FlashStore.Cli.Core.Verdicts;

namespace FlashStore.UnitTests;

public class CliArgsTests
{
    [Fact]
    public void Parse_SeparaPosicionalesOpcionesYBanderas()
    {
        var args = ParsedArgs.Parse(["sale", "simulate", "--buyers", "300", "--yes", "--api=http://x:5000"]);

        Assert.Equal("sale", args.At(0));
        Assert.Equal("simulate", args.At(1));
        Assert.Equal("300", args.Get("buyers"));
        Assert.True(args.Has("yes"));
        Assert.Null(args.Get("yes"));
        Assert.Equal("http://x:5000", args.Get("api"));
        Assert.Null(args.At(2));
    }

    [Fact]
    public void Parse_BanderaNoConsumeElSiguientePosicional()
    {
        var args = ParsedArgs.Parse(["env", "up", "--build", "extra"]);

        Assert.True(args.Has("build"));
        Assert.Equal("extra", args.At(2));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("301")]
    [InlineData("abc")]
    public void GetInt_FueraDeRango_LanzaErrorDeUso(string value)
    {
        var args = ParsedArgs.Parse(["--buyers", value]);

        Assert.Throws<UsageException>(() => args.GetInt("buyers", 300, 1, 300));
    }

    [Fact]
    public void GetInt_SinValor_UsaElPorDefecto()
    {
        Assert.Equal(300, ParsedArgs.Parse([]).GetInt("buyers", 300, 1, 300));
    }
}

public class CliEnvAndSshTests
{
    [Fact]
    public void EnvFile_IgnoraComentariosYQuitaComillas()
    {
        var values = EnvFile.Parse(["# comentario", "", "POSTGRES_USER=flashstore", "JWT_KEY=\"a=b=c\"", "SIN_IGUAL", "CRLF=valor\r"]);

        Assert.Equal("flashstore", values["POSTGRES_USER"]);
        Assert.Equal("a=b=c", values["JWT_KEY"]);
        Assert.Equal("valor", values["CRLF"]);
        Assert.False(values.ContainsKey("SIN_IGUAL"));
    }

    [Fact]
    public void SshTarget_ParseaUsuarioHostYPuerto()
    {
        var target = SshTarget.Parse("mary@localhost:2222");

        Assert.Equal("mary", target.User);
        Assert.Equal("localhost", target.Host);
        Assert.Equal(2222, target.Port);
        Assert.Equal(22, SshTarget.Parse("mary@vm").Port);
    }

    [Theory]
    [InlineData("mary@localhost;rm -rf /")]
    [InlineData("mary@$(whoami)")]
    [InlineData("localhost")]
    [InlineData("mary@host:99999")]
    public void SshTarget_RechazaValoresPeligrososOInvalidos(string raw)
    {
        Assert.Throws<UsageException>(() => SshTarget.Parse(raw));
    }

    [Fact]
    public void QuoteForPosix_NeutralizaComillasYComandos()
    {
        Assert.Equal("'product*'", Shell.QuoteForPosix("product*"));
        Assert.Equal("'a'\\''b'", Shell.QuoteForPosix("a'b"));
        Assert.Equal("'$(rm -rf /)'", Shell.QuoteForPosix("$(rm -rf /)"));
    }

    [Fact]
    public void RequireService_SoloAceptaServiciosDelCompose()
    {
        Assert.Equal("api", EnvCommands.RequireService("api"));
        Assert.Throws<UsageException>(() => EnvCommands.RequireService("jenkins"));
        Assert.Throws<UsageException>(() => EnvCommands.RequireService(null));
    }
}

public class CliVerdictTests
{
    private static SaleTally Tally(int stockBefore = 5, int stockAfter = 0, int created = 5, int buyers = 300, int loggedIn = 300, int serverErrors = 0) =>
        new(stockBefore, stockAfter, 1, buyers, loggedIn, created, loggedIn - created, buyers - loggedIn, serverErrors, 0);

    [Fact]
    public void VentaAtomica_CincoPedidosConCincoUnidades_EsGo()
    {
        var verdict = EvaluateSale(Tally());

        Assert.True(verdict.Passed);
        Assert.False(verdict.Oversold);
        Assert.Empty(verdict.Findings);
    }

    [Fact]
    public void VentaInsegura_MasPedidosQueStock_EsSobreventa()
    {
        var verdict = EvaluateSale(Tally(created: 37, stockAfter: 0));

        Assert.False(verdict.Passed);
        Assert.True(verdict.Oversold);
        Assert.Contains("SOBREVENTA", verdict.Findings[0]);
    }

    [Fact]
    public void StockNegativo_EsSobreventa()
    {
        Assert.True(EvaluateSale(Tally(created: 5, stockAfter: -2)).Oversold);
    }

    [Fact]
    public void CompradoresSinSesion_NoCuentaComoGo()
    {
        var verdict = EvaluateSale(Tally(loggedIn: 20, created: 5));

        Assert.False(verdict.Passed);
        Assert.False(verdict.Oversold);
    }

    [Fact]
    public void ErroresDelServidor_NoEsGo()
    {
        Assert.False(EvaluateSale(Tally(serverErrors: 3)).Passed);
    }

    [Fact]
    public void GoNoGo_ObligatorioPendiente_EsNoGo()
    {
        var decision = Decide(
        [
            new GoNoGoCriterion("Salud", "", true, true, ""),
            new GoNoGoCriterion("API", "", true, null, "Sin ejecutar")
        ]);

        Assert.False(decision.Go);
    }

    [Fact]
    public void GoNoGo_OpcionalPendiente_NoBloquea()
    {
        var decision = Decide(
        [
            new GoNoGoCriterion("Salud", "", true, true, ""),
            new GoNoGoCriterion("E2E", "", false, null, "Sin ejecutar")
        ]);

        Assert.True(decision.Go);
    }

    [Fact]
    public void GoNoGo_OpcionalEjecutadoConFalla_EsNoGo()
    {
        var decision = Decide(
        [
            new GoNoGoCriterion("Salud", "", true, true, ""),
            new GoNoGoCriterion("E2E", "", false, false, "Falló")
        ]);

        Assert.False(decision.Go);
    }

    [Fact]
    public void Percentil95_DeCienValores()
    {
        var values = Enumerable.Range(1, 100).Select(v => (double)v);

        Assert.Equal(95d, SaleCommands.Percentile(values, 0.95));
        Assert.Equal(0d, SaleCommands.Percentile([], 0.95));
    }
}
