namespace LicenciasCarpetas.Statistics;

public interface IBusinessDayCalculator
{
    /// <summary>Días hábiles entre dos fechas (lunes a viernes), excluyendo feriados configurados.
    /// Cuenta desde el día siguiente a <paramref name="from"/> hasta <paramref name="to"/> inclusive
    /// (ej. lunes a viernes de la misma semana = 4 días hábiles). Cero si <paramref name="to"/> no
    /// es posterior a <paramref name="from"/> (mismo día o rango invertido).</summary>
    int BusinessDaysBetween(DateOnly from, DateOnly to);

    /// <summary>Si hay al menos un feriado configurado para ese año. Cuando es false, el llamador
    /// debe mostrar la advertencia "feriados no considerados para este año" — el cálculo igual
    /// aplica la regla lunes-viernes, nunca lanza ni bloquea.</summary>
    bool HolidaysConfigured(int year);
}

/// <summary>Calcula días hábiles (lun-vie) menos feriados chilenos configurables por
/// <see cref="KpiOptions"/>. Sin lista de feriados para el año consultado, cae a la regla
/// lunes-viernes solamente — nunca lanza ni bloquea el cálculo.</summary>
public sealed class BusinessDayCalculator(KpiOptions options) : IBusinessDayCalculator
{
    private readonly HashSet<DateOnly> _holidays = [.. options.Holidays
        .Select(TryParse)
        .Where(date => date is not null)
        .Select(date => date!.Value)];

    private static DateOnly? TryParse(string text)
        => DateOnly.TryParse(text, out var date) ? date : null;

    public int BusinessDaysBetween(DateOnly from, DateOnly to)
    {
        if (to <= from)
        {
            return 0;
        }

        var count = 0;
        for (var date = from.AddDays(1); date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            if (_holidays.Contains(date))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public bool HolidaysConfigured(int year) => _holidays.Any(date => date.Year == year);
}
