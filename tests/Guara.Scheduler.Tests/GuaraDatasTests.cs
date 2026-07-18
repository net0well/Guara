using Guara.Scheduler;
using Xunit;

namespace Guara.Scheduler.Tests;

public class GuaraDatasTests
{
    private static readonly DateTimeOffset Instante =
        new(2026, 7, 16, 10, 42, 37, 456, TimeSpan.Zero);

    [Fact]
    public void SegundoExato_RemoveFracaoDeSegundo()
        => Assert.Equal(new DateTimeOffset(2026, 7, 16, 10, 42, 37, TimeSpan.Zero), GuaraDatas.SegundoExato(Instante));

    [Fact]
    public void MinutoExato_RemoveSegundos()
        => Assert.Equal(new DateTimeOffset(2026, 7, 16, 10, 42, 0, TimeSpan.Zero), GuaraDatas.MinutoExato(Instante));

    [Fact]
    public void HoraExata_RemoveMinutos()
        => Assert.Equal(new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero), GuaraDatas.HoraExata(Instante));

    [Fact]
    public void HojeAs_RetornaHorarioPedidoNoFuso()
    {
        var resultado = GuaraDatas.HojeAs(3, 30);

        Assert.Equal(3, resultado.Hour);
        Assert.Equal(30, resultado.Minute);
        Assert.Equal(TimeSpan.Zero, resultado.Offset);
    }

    [Fact]
    public void AmanhaAs_EhUmDiaDepoisDeHojeAs()
    {
        var hoje = GuaraDatas.HojeAs(8, 0);
        var amanha = GuaraDatas.AmanhaAs(8, 0);

        Assert.Equal(hoje.AddDays(1), amanha);
    }

    [Fact]
    public void ProximoDiaUtil_CaiDeSegundaASexta_NoFuturo()
    {
        var resultado = GuaraDatas.ProximoDiaUtil();

        Assert.NotEqual(DayOfWeek.Saturday, resultado.DayOfWeek);
        Assert.NotEqual(DayOfWeek.Sunday, resultado.DayOfWeek);
        Assert.True(resultado > DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, resultado.TimeOfDay);
    }

    [Theory]
    [InlineData(24, 0)]
    [InlineData(-1, 0)]
    [InlineData(8, 60)]
    public void HojeAs_HorarioInvalido_Lanca(int hora, int minuto)
        => Assert.Throws<ArgumentOutOfRangeException>(() => GuaraDatas.HojeAs(hora, minuto));
}
