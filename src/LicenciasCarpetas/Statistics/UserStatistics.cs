namespace LicenciasCarpetas.Statistics;

/// <summary>
/// Resumen de productividad y balance de trabajo mensual para un funcionario/usuario.
/// </summary>
public sealed record UserActivitySummary(
    string UserName,
    int TotalActions,
    int CasesModified,
    int SubidasRealizadas,
    int AsistenciasMarcadas,
    int FoliosAsignados,
    int ActiveDaysCount,
    double DailyAverage,
    string LastActiveDate);

/// <summary>
/// Punto para el gráfico de dispersión (Scatter Plot) de actividad y evolución temporal.
/// Eje X: Día del mes (1..31). Eje Y: Hora del día (en formato decimal, ej 14.5 = 14:30).
/// </summary>
public sealed record UserActivityScatterPoint(
    string UserName,
    int Day,
    double Hour,
    string ActionType,
    string FieldName,
    long CaseId,
    string FormattedTime);

/// <summary>
/// Conjunto completo de estadísticas por usuario del mes.
/// </summary>
public sealed record MonthlyUserStatistics(
    int Year,
    int Month,
    IReadOnlyList<UserActivitySummary> Summaries,
    IReadOnlyList<UserActivityScatterPoint> ScatterPoints,
    IReadOnlyList<string> ActiveUsers,
    IReadOnlyDictionary<string, int[]> DailyCountsByUser);
