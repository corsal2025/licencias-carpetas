using ClosedXML.Excel;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Statistics;

namespace LicenciasCarpetas.Reporting;

/// <summary>The comparative KPI screen as a workbook: one sheet per block, same numbers as the page.</summary>
public static class SedeKpiExcelExporter
{
    public static byte[] Export(SedeKpiReport report)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.AddWorksheet("Resumen");
        summary.Cell(1, 1).Value = $"KPI por sede — {report.Period.Label} ({report.Period.From:dd-MM-yyyy} a {report.Period.To:dd-MM-yyyy})";
        summary.Cell(1, 1).Style.Font.Bold = true;
        string[] headers =
        [
            "Sede", "Casos", "Otorgado", "Denegado", "En curso", "Sin decisión", "Pendiente",
            "Días promedio", "Días mediana", "N con fecha", "Decididos sin fecha", "% sin decisión",
            "% decididos sin fecha", "Backlog <7 días", "Backlog 7-14 días", "Backlog ≥15 días"
        ];
        WriteHeader(summary, 3, headers);
        var row = 4;
        foreach (var kpi in report.Offices.Append(report.Total))
        {
            object?[] values =
            [
                kpi.Label, kpi.Total, kpi.Otorgado, kpi.Denegado, kpi.EnCurso, kpi.SinDecision, kpi.Pendiente,
                kpi.AverageDays, kpi.MedianDays, kpi.DaysSample, kpi.DecidedWithoutDate, kpi.PercentWithoutDecision,
                kpi.PercentDecidedWithoutDate, kpi.BacklogNormal, kpi.BacklogWarning, kpi.BacklogOverdue
            ];
            for (var column = 0; column < values.Length; column++)
            {
                summary.Cell(row, column + 1).Value = XLCellValue.FromObject(values[column]);
            }

            row++;
        }

        summary.Row(row - 1).Style.Font.Bold = true;
        summary.Columns().AdjustToContents();

        var states = workbook.AddWorksheet("Estados");
        WriteHeader(states, 1, ["Sede", "Estado de carpeta", "Casos"]);
        row = 2;
        foreach (var kpi in report.Offices)
        {
            foreach (var state in kpi.States)
            {
                states.Cell(row, 1).Value = kpi.Label;
                states.Cell(row, 2).Value = state.State;
                states.Cell(row, 3).Value = state.Count;
                row++;
            }
        }

        states.Columns().AdjustToContents();

        var trend = workbook.AddWorksheet("Tendencia");
        WriteHeader(trend, 1, ["Año", "Mes", "Sede", "Casos", "Otorgado"]);
        row = 2;
        foreach (var point in report.Trend)
        {
            trend.Cell(row, 1).Value = point.Year;
            trend.Cell(row, 2).Value = point.Month;
            trend.Cell(row, 3).Value = OfficeCatalog.Display(point.Office);
            trend.Cell(row, 4).Value = point.Total;
            trend.Cell(row, 5).Value = point.Otorgado;
            row++;
        }

        trend.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(row, column + 1).Value = headers[column];
        }

        sheet.Row(row).Style.Font.Bold = true;
    }
}
