using LicenciasCarpetas.Statistics;

namespace LicenciasCarpetas.Tests;

/// <summary>Días hábiles (lunes a viernes) entre citación y decisión, feriados chilenos configurables.</summary>
public class BusinessDayCalculatorTests
{
    private static IBusinessDayCalculator Calculator(params string[] holidays)
    {
        var options = new KpiOptions { Holidays = [.. holidays] };
        return new BusinessDayCalculator(options);
    }

    [Fact]
    public void Monday_to_friday_is_four_business_days()
    {
        var calculator = Calculator();

        var result = calculator.BusinessDaysBetween(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));

        Assert.Equal(4, result);
    }

    [Fact]
    public void Configured_holiday_is_excluded_from_the_count()
    {
        // Lunes 2026-01-05 a viernes 2026-01-09, feriado el miércoles 2026-01-07.
        var calculator = Calculator("2026-01-07");

        var result = calculator.BusinessDaysBetween(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));

        Assert.Equal(3, result);
    }

    [Fact]
    public void Weekend_days_never_count_as_business_days()
    {
        var calculator = Calculator();

        // Sábado 2026-01-03 a lunes 2026-01-05: solo el lunes cuenta.
        var result = calculator.BusinessDaysBetween(new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 5));

        Assert.Equal(1, result);
    }

    [Fact]
    public void Same_day_is_zero_business_days()
    {
        var calculator = Calculator();

        var result = calculator.BusinessDaysBetween(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5));

        Assert.Equal(0, result);
    }

    [Fact]
    public void An_inverted_range_where_from_is_after_to_is_zero_business_days()
    {
        // Nunca debe contar "hacia atrás" ni lanzar: un rango invertido no tiene sentido de negocio
        // (citación siempre antes que decisión), pero el calculador no debe romperse si llega así.
        var calculator = Calculator();

        var result = calculator.BusinessDaysBetween(new DateOnly(2026, 1, 9), new DateOnly(2026, 1, 5));

        Assert.Equal(0, result);
    }

    [Fact]
    public void Empty_holiday_list_falls_back_to_weekday_only_rule_and_exposes_that_holidays_are_not_configured()
    {
        var calculator = Calculator();

        Assert.False(calculator.HolidaysConfigured(2026));
    }

    [Fact]
    public void Holidays_configured_for_the_queried_year_reports_true()
    {
        var calculator = Calculator("2026-01-07");

        Assert.True(calculator.HolidaysConfigured(2026));
        Assert.False(calculator.HolidaysConfigured(2027));
    }

    [Fact]
    public void A_year_missing_from_the_configured_holidays_falls_back_to_weekday_only_rule()
    {
        // Feriados cargados solo para 2026: 2028 debe seguir contando días hábiles (lun-vie) sin
        // feriados y reportar que no hay feriados configurados para ese año — nunca lanzar.
        var calculator = Calculator("2026-01-07");

        var result = calculator.BusinessDaysBetween(new DateOnly(2028, 1, 3), new DateOnly(2028, 1, 9));

        Assert.Equal(4, result);
        Assert.False(calculator.HolidaysConfigured(2028));
    }

    [Fact]
    public void Holidays_written_in_an_unparsable_format_are_ignored_instead_of_throwing()
    {
        var calculator = Calculator("no-es-una-fecha", "2026-01-07");

        Assert.True(calculator.HolidaysConfigured(2026));
        Assert.Equal(3, calculator.BusinessDaysBetween(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9)));
    }
}
