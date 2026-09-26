using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Statistics;

/// <summary>One day of the statistics screen — the "ESCANEADAS Y SUBIDAS" sheet, rebuilt.</summary>
public sealed record DailyStatisticsRow(
    DateOnly Date,
    int? Scanned,
    int? Uploaded,
    IReadOnlyDictionary<Office, (int Scheduled, int Attended)> ByOffice)
{
    public int ScheduledTotal => ByOffice.Values.Sum(entry => entry.Scheduled);
    public int AttendedTotal => ByOffice.Values.Sum(entry => entry.Attended);

    /// <summary>Attendance rate for one office, null when nobody was scheduled there that day.</summary>
    public double? AttendanceRate(Office office)
        => ByOffice.TryGetValue(office, out var entry) && entry.Scheduled > 0
            ? (double)entry.Attended / entry.Scheduled
            : null;
}

public sealed record MonthlyStatistics(
    int Year,
    int Month,
    IReadOnlyList<DailyStatisticsRow> Days,
    IReadOnlyList<(FolderState? State, int Count)> FolderStates,
    IReadOnlyList<(FinalDecision? Decision, int Count)> FinalDecisions,
    IReadOnlyList<(LicenceClass Licence, int Count)> LicenceClasses)
{
    /// <summary>Total de licencias tramitadas en el mes. Un caso con dos clases cuenta dos veces:
    /// son dos licencias, aunque sea una sola persona.</summary>
    public int LicencesTotal => LicenceClasses.Sum(entry => entry.Count);

    public int ProfessionalLicences => LicenceClasses
        .Where(entry => LicenceClassCatalog.IsProfessional(entry.Licence))
        .Sum(entry => entry.Count);

    public int ScannedTotal => Days.Sum(day => day.Scanned ?? 0);
    public int UploadedTotal => Days.Sum(day => day.Uploaded ?? 0);
    public int ScheduledTotal => Days.Sum(day => day.ScheduledTotal);
    public int AttendedTotal => Days.Sum(day => day.AttendedTotal);

    public double? AttendanceRate => ScheduledTotal > 0 ? (double)AttendedTotal / ScheduledTotal : null;

    public int ScheduledFor(Office office) => Days.Sum(day => day.ByOffice.TryGetValue(office, out var e) ? e.Scheduled : 0);
    public int AttendedFor(Office office) => Days.Sum(day => day.ByOffice.TryGetValue(office, out var e) ? e.Attended : 0);
}

/// <summary>
/// Rebuilds the workbook's statistics sheet from the cases themselves: only "escaneadas" and
/// "subidas" stay manual counters, everything else is counted, so the numbers can no longer drift
/// away from the agenda they summarise.
/// </summary>
public sealed class StatisticsService(IFolderCaseRepository cases, IDailyCounterRepository counters)
{
    /// <summary><paramref name="scope"/> limits the case-derived figures to the user's offices; the
    /// manual scanned/uploaded counters are department-wide and are not split by office.</summary>
    public MonthlyStatistics ForMonth(int year, int month, OfficeScope? scope = null)
    {
        var allowed = scope?.AllowedOffices;
        var attendance = cases.DailyAttendance(year, month, allowed);
        var monthCounters = counters.ForMonth(year, month).ToDictionary(counter => counter.Date);

        var dates = attendance
            .Select(entry => entry.Date)
            .Concat(monthCounters.Keys)
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        var days = new List<DailyStatisticsRow>(dates.Count);
        foreach (var date in dates)
        {
            var byOffice = attendance
                .Where(entry => entry.Date == date)
                .ToDictionary(entry => entry.Office, entry => (entry.Scheduled, entry.Attended));

            monthCounters.TryGetValue(date, out var counter);
            days.Add(new DailyStatisticsRow(date, counter?.Scanned, counter?.Uploaded, byOffice));
        }

        return new MonthlyStatistics(
            year,
            month,
            days,
            cases.FolderStateBreakdown(year, month, office: null, allowed),
            cases.FinalDecisionBreakdown(year, month, office: null, allowed),
            cases.LicenceClassBreakdown(year, month, office: null, allowed));
    }

    public MonthlyUserStatistics UserStatsForMonth(int year, int month, OfficeScope? scope = null)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var start = new DateTimeOffset(new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc));
        var end = new DateTimeOffset(new DateTime(year, month, daysInMonth, 23, 59, 59, DateTimeKind.Utc));

        var logs = cases.GetAuditLogsForPeriod(start, end, scope?.AllowedOffices);

        var scatterPoints = new List<UserActivityScatterPoint>();
        var userActions = new Dictionary<string, List<CaseAuditEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var log in logs)
        {
            if (string.IsNullOrWhiteSpace(log.ChangedBy)) continue;
            var user = log.ChangedBy.Trim();
            if (!userActions.TryGetValue(user, out var list))
            {
                list = [];
                userActions[user] = list;
            }
            list.Add(log);

            var localTime = log.ChangedAt.ToLocalTime();
            var decimalHour = localTime.Hour + (localTime.Minute / 60.0);
            scatterPoints.Add(new UserActivityScatterPoint(
                user,
                localTime.Day,
                Math.Round(decimalHour, 2),
                log.FieldName,
                log.FieldName,
                log.FolderCaseId,
                localTime.ToString("dd/MM HH:mm")
            ));
        }

        var summaries = new List<UserActivitySummary>();
        var dailyCountsByUser = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var (user, userLogs) in userActions)
        {
            var casesModified = userLogs.Select(l => l.FolderCaseId).Distinct().Count();
            var subidas = userLogs.Count(l => l.FieldName == "Estado carpeta" && (l.NewValue?.StartsWith("Subida", StringComparison.OrdinalIgnoreCase) == true || l.NewValue?.Contains("Subid", StringComparison.OrdinalIgnoreCase) == true));
            var asistencias = userLogs.Count(l => l.FieldName == "Asistencia");
            var folios = userLogs.Count(l => l.FieldName == "Folio licencia" && !string.IsNullOrWhiteSpace(l.NewValue));
            var activeDays = userLogs.Select(l => l.ChangedAt.ToLocalTime().Date).Distinct().ToList();
            var activeDaysCount = activeDays.Count;
            var dailyAvg = activeDaysCount > 0 ? Math.Round((double)userLogs.Count / activeDaysCount, 1) : 0;
            var lastActive = userLogs.Max(l => l.ChangedAt).ToLocalTime().ToString("dd-MM-yyyy HH:mm");

            var dailyArray = new int[daysInMonth + 1];
            foreach (var log in userLogs)
            {
                var day = log.ChangedAt.ToLocalTime().Day;
                if (day >= 1 && day <= daysInMonth) dailyArray[day]++;
            }
            dailyCountsByUser[user] = dailyArray;

            summaries.Add(new UserActivitySummary(
                user,
                userLogs.Count,
                casesModified,
                subidas,
                asistencias,
                folios,
                activeDaysCount,
                dailyAvg,
                lastActive
            ));
        }

        // Si no hay logs de auditoría en ese mes, buscar en FolderCase por UpdatedBy
        if (summaries.Count == 0)
        {
            var monthCases = cases.QueryAll(new CaseFilter { Year = year, Month = month });
            var byUpdated = monthCases.Where(c => !string.IsNullOrWhiteSpace(c.UpdatedBy))
                .GroupBy(c => c.UpdatedBy!.Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (var group in byUpdated)
            {
                var u = group.Key;
                var count = group.Count();
                var subidas = group.Count(c => c.FolderState is { } st && (st == FolderState.SubidaAConaset || st == FolderState.SubidaConF8 || st == FolderState.SubidaConOficio));
                var asistencias = group.Count(c => c.Attended);
                var folios = group.Count(c => !string.IsNullOrWhiteSpace(c.FolioLicencia));
                summaries.Add(new UserActivitySummary(u, count, count, subidas, asistencias, folios, 1, count, DateTime.Today.ToString("dd-MM-yyyy")));
            }
        }

        summaries = summaries.OrderByDescending(s => s.TotalActions).ToList();
        var activeUsers = summaries.Select(s => s.UserName).ToList();

        return new MonthlyUserStatistics(year, month, summaries, scatterPoints, activeUsers, dailyCountsByUser);
    }
}
