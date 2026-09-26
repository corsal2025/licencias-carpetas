using System.Globalization;
using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Statistics;

public enum KpiPeriodKind
{
    Mes,
    Semestre,
    Anio
}

/// <summary>A reporting period. <see cref="Index"/> is the month (1–12) for <see cref="KpiPeriodKind.Mes"/>,
/// the semester (1–2) for <see cref="KpiPeriodKind.Semestre"/> and ignored for a whole year.</summary>
public sealed record KpiPeriod(int Year, KpiPeriodKind Kind, int Index)
{
    public DateOnly From => Kind switch
    {
        KpiPeriodKind.Mes => new DateOnly(Year, Math.Clamp(Index, 1, 12), 1),
        KpiPeriodKind.Semestre => new DateOnly(Year, Index == 2 ? 7 : 1, 1),
        _ => new DateOnly(Year, 1, 1)
    };

    public DateOnly To => Kind switch
    {
        KpiPeriodKind.Mes => From.AddMonths(1).AddDays(-1),
        KpiPeriodKind.Semestre => From.AddMonths(6).AddDays(-1),
        _ => new DateOnly(Year, 12, 31)
    };

    public string Label => Kind switch
    {
        KpiPeriodKind.Mes => From.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-CL")),
        KpiPeriodKind.Semestre => $"{(Index == 2 ? "2°" : "1°")} semestre {Year}",
        _ => $"Año {Year}"
    };
}

public sealed record StateCount(string State, int Count);

/// <summary>KPIs of one office (or of every office in scope, when <see cref="Office"/> is null).</summary>
public sealed class SedeKpi
{
    public Office? Office { get; init; }
    public string Label => Office is { } office ? OfficeCatalog.Display(office) : "Total";
    public int Total { get; init; }
    public int Otorgado { get; init; }
    public int Denegado { get; init; }

    /// <summary>A decision other than granted/denied: waiting for an exam, class pending, S/SGL…</summary>
    public int EnCurso { get; init; }
    public int SinDecision { get; init; }
    public int Pendiente => EnCurso + SinDecision;
    public IReadOnlyList<StateCount> States { get; init; } = [];

    /// <summary>Citation → final decision (granted/denied) in days, taken from the audit log.</summary>
    public double? AverageDays { get; init; }
    public double? MedianDays { get; init; }
    public int DaysSample { get; init; }

    /// <summary>Granted/denied cases with no audited decision date (e.g. imported from the workbook).</summary>
    public int DecidedWithoutDate { get; init; }
    public int BacklogNormal { get; init; }
    public int BacklogWarning { get; init; }
    public int BacklogOverdue { get; init; }

    public double PercentWithoutDecision => Percent(SinDecision, Total);
    public double PercentDecidedWithoutDate => Percent(DecidedWithoutDate, Otorgado + Denegado);

    private static double Percent(int part, int whole) => whole == 0 ? 0 : Math.Round(100.0 * part / whole, 1);
}

public sealed record MonthlyTrendPoint(int Year, int Month, Office Office, int Total, int Otorgado);

public sealed class SedeKpiReport
{
    public required KpiPeriod Period { get; init; }
    public IReadOnlyList<SedeKpi> Offices { get; init; } = [];
    public required SedeKpi Total { get; init; }
    public IReadOnlyList<MonthlyTrendPoint> Trend { get; init; } = [];

    public SedeKpi For(Office office) => Offices.Single(o => o.Office == office);
}

/// <summary>
/// Comparative KPIs per office for the management dashboard. There is no decision-date column: the
/// decision date is the last audited change of "Decisión final" to Otorgado/Denegado, so cases
/// imported from the workbook (never edited here) have no date and are reported as such instead of
/// silently skewing the averages.
/// </summary>
public sealed class SedeKpiService(string connectionString)
{
    private sealed record Row(Office Office, DateOnly Citation, FolderState? State, string? StateRaw, FinalDecision? Decision, DateOnly? DecidedOn);

    public SedeKpiReport Build(KpiPeriod period, OfficeScope scope, DateOnly today)
    {
        var rows = Load(period).Where(r => scope.Allows(r.Office)).ToList();
        var offices = scope.Offices;

        var trend = new List<MonthlyTrendPoint>();
        for (var month = period.From; month <= period.To; month = month.AddMonths(1))
        {
            foreach (var office in offices)
            {
                var inMonth = rows.Where(r => r.Office == office && r.Citation.Year == month.Year && r.Citation.Month == month.Month).ToList();
                trend.Add(new MonthlyTrendPoint(month.Year, month.Month, office, inMonth.Count, inMonth.Count(r => r.Decision == FinalDecision.Otorgado)));
            }
        }

        return new SedeKpiReport
        {
            Period = period,
            Offices = [.. offices.Select(office => Compute(office, rows.Where(r => r.Office == office).ToList(), today))],
            Total = Compute(null, rows, today),
            Trend = trend
        };
    }

    private static SedeKpi Compute(Office? office, IReadOnlyList<Row> rows, DateOnly today)
    {
        var decided = rows.Where(r => r.Decision is FinalDecision.Otorgado or FinalDecision.Denegado).ToList();
        var days = decided
            .Where(r => r.DecidedOn is not null)
            .Select(r => (double)Math.Max(0, r.DecidedOn!.Value.DayNumber - r.Citation.DayNumber))
            .Order()
            .ToList();
        var pending = rows.Where(r => r.Decision is not (FinalDecision.Otorgado or FinalDecision.Denegado))
            .Select(r => FolderCase.AgingFor(r.State, r.Citation, today))
            .ToList();

        return new SedeKpi
        {
            Office = office,
            Total = rows.Count,
            Otorgado = rows.Count(r => r.Decision == FinalDecision.Otorgado),
            Denegado = rows.Count(r => r.Decision == FinalDecision.Denegado),
            EnCurso = rows.Count(r => r.Decision is not null and not FinalDecision.Otorgado and not FinalDecision.Denegado),
            SinDecision = rows.Count(r => r.Decision is null),
            States = [.. rows
                .GroupBy(r => r.State is { } s ? FolderStateCatalog.Display(s) : string.IsNullOrWhiteSpace(r.StateRaw) ? "Sin estado" : r.StateRaw!)
                .Select(g => new StateCount(g.Key, g.Count()))
                .OrderByDescending(s => s.Count)
                .ThenBy(s => s.State, StringComparer.Ordinal)],
            DaysSample = days.Count,
            AverageDays = days.Count == 0 ? null : Math.Round(days.Average(), 1),
            MedianDays = days.Count == 0 ? null : Median(days),
            DecidedWithoutDate = decided.Count(r => r.DecidedOn is null),
            BacklogNormal = pending.Count(a => a == CaseAgingAlert.Normal),
            BacklogWarning = pending.Count(a => a == CaseAgingAlert.Warning),
            BacklogOverdue = pending.Count(a => a == CaseAgingAlert.Overdue)
        };
    }

    private static double Median(IReadOnlyList<double> sorted) => sorted.Count % 2 == 1
        ? sorted[sorted.Count / 2]
        : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    private List<Row> Load(KpiPeriod period)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        // Last audited switch to granted/denied per case. The audit log stores enum names.
        command.CommandText = """
            SELECT f.Office, f.CitationDate, f.FolderState, f.FolderStateRaw, f.FinalDecision,
                   (SELECT a.ChangedAt FROM CaseAuditLog a
                    WHERE a.FolderCaseId = f.Id AND a.FieldName = 'Decisión final'
                      AND a.NewValue IN ('Otorgado', 'Denegado')
                    ORDER BY a.ChangedAt DESC, a.Id DESC LIMIT 1)
            FROM FolderCase f
            WHERE f.DeletedAt IS NULL AND f.CitationDate BETWEEN $from AND $to
            """;
        command.Parameters.AddWithValue("$from", period.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", period.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<Row>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new Row(
                (Office)reader.GetInt32(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                ToEnum<FolderState>(reader, 2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                ToEnum<FinalDecision>(reader, 4),
                reader.IsDBNull(5)
                    ? null
                    : DateOnly.FromDateTime(DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture).LocalDateTime)));
        }

        return rows;
    }

    private static T? ToEnum<T>(SqliteDataReader reader, int ordinal) where T : struct, Enum
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetInt32(ordinal);
        return Enum.IsDefined(typeof(T), value) ? (T)(object)value : null;
    }
}
