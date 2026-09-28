namespace LicenciasCarpetas.Statistics;

/// <summary>
/// Feriados chilenos usados para el cálculo de días hábiles del dashboard KPI. Lista plana
/// (no separada por año) porque el calculador la filtra por año consultado.
///
/// El feriado 2026-06-21 / 2027-06-21 (Día Nacional de los Pueblos Indígenas) está incluido en
/// <c>appsettings.json</c>/<c>appsettings.Example.json</c> con una nota: su fecha exacta depende de
/// un decreto anual que fija el equinoccio/solsticio correspondiente y aún no está confirmado para
/// esos años al momento de escribir esto. Si el decreto cambia la fecha, hay que actualizar la
/// configuración — el calculador no tiene lógica astronómica, solo lee lo que se le configura.
/// </summary>
public sealed class KpiOptions
{
    public const string SectionName = "Kpi";

    /// <summary>Fechas en formato "yyyy-MM-dd". Vacía o sin configurar para un año = solo se aplica
    /// la regla lunes-viernes para ese año, y el dashboard debe avisarlo.</summary>
    public List<string> Holidays { get; set; } = [];
}
