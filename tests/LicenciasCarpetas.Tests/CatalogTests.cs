using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Tests;

public class CatalogTests
{
    [Theory]
    [InlineData("SUBIDA A CONASET", FolderState.SubidaAConaset)]
    [InlineData("subida a conaset", FolderState.SubidaAConaset)]
    [InlineData("1° LICENCIA", FolderState.PrimeraLicencia)]
    [InlineData("CAMBIO DOM. SUBIDO CON CORREO", FolderState.CambioDomicilioSubidoConCorreo)]
    [InlineData("CAMBIO DE DOM SUBIDO CON CORREO", FolderState.CambioDomicilioSubidoConCorreo)]
    [InlineData("SE ENCUENTRA EN OF.43", FolderState.SeEncuentraEnOficina43)]
    [InlineData("SE ENCUENTRA EN OF. 43", FolderState.SeEncuentraEnOficina43)]
    [InlineData("CANJE", FolderState.CanjeLicenciaExtranjera)]
    [InlineData("CANJE LIC. EXTRANJERA", FolderState.CanjeLicenciaExtranjera)]
    public void FolderState_resolves_every_spelling_used_in_the_workbook(string text, FolderState expected)
        => Assert.Equal(expected, FolderStateCatalog.TryResolve(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("ALGO QUE NO EXISTE")]
    public void FolderState_returns_null_for_unknown_text(string? text)
        => Assert.Null(FolderStateCatalog.TryResolve(text));

    [Theory]
    [InlineData("ESPERA EXÁMEN", FinalDecision.EsperaExamen)]
    [InlineData("ESPERA EXAMEN", FinalDecision.EsperaExamen)]
    [InlineData("S/SGL", FinalDecision.SinSgl)]
    [InlineData("CLASE PENDIENTE ", FinalDecision.ClasePendiente)]
    [InlineData("PARA DENEGAR", FinalDecision.ParaDenegar)]
    [InlineData("EX. MÉDICO", FinalDecision.ExamenMedico)]
    [InlineData("EX. MEDICO", FinalDecision.ExamenMedico)]
    [InlineData("EXAMEN MÉDICO", FinalDecision.ExamenMedico)]
    [InlineData("EX. TEÓRICO", FinalDecision.ExamenTeorico)]
    [InlineData("EXAMEN TEORICO", FinalDecision.ExamenTeorico)]
    [InlineData("EX. PRÁCTICO", FinalDecision.ExamenPractico)]
    [InlineData("EXAMEN PRACTICO", FinalDecision.ExamenPractico)]
    public void FinalDecision_collapses_accent_and_spacing_variants(string text, FinalDecision expected)
        => Assert.Equal(expected, FinalDecisionCatalog.TryResolve(text));

    [Theory]
    [InlineData(FinalDecision.ExamenMedico, "EX. MÉDICO")]
    [InlineData(FinalDecision.ExamenTeorico, "EX. TEÓRICO")]
    [InlineData(FinalDecision.ExamenPractico, "EX. PRÁCTICO")]
    public void FinalDecision_shows_the_short_exam_labels(FinalDecision decision, string expected)
        => Assert.Equal(expected, FinalDecisionCatalog.Display(decision));

    [Fact]
    public void FinalDecision_catalog_lists_every_value_including_the_three_exam_outcomes()
    {
        Assert.Equal(Enum.GetValues<FinalDecision>().Length, FinalDecisionCatalog.All.Count);
        Assert.Contains(FinalDecision.ExamenMedico, FinalDecisionCatalog.All);
        Assert.Contains(FinalDecision.ExamenTeorico, FinalDecisionCatalog.All);
        Assert.Contains(FinalDecision.ExamenPractico, FinalDecisionCatalog.All);
    }

    [Theory]
    [InlineData("ALERTADA", MoralIdoneity.Alertada)]
    [InlineData("revisar", MoralIdoneity.Revisar)]
    public void MoralIdoneity_resolves_catalog_values(string text, MoralIdoneity expected)
        => Assert.Equal(expected, MoralIdoneityCatalog.TryResolve(text));

    [Theory]
    [InlineData("SI, EN AV. ARGENTINA", Office.AvenidaArgentina)]
    [InlineData("AV. ARGENTINA", Office.AvenidaArgentina)]
    [InlineData("SI, EN PLACILLA", Office.Placilla)]
    [InlineData("MERC. PUERTO", Office.MercadoPuerto)]
    [InlineData("MERCADO PUERTO", Office.MercadoPuerto)]
    public void Office_resolves_from_attention_text(string text, Office expected)
        => Assert.Equal(expected, OfficeCatalog.TryResolve(text));

    [Fact]
    public void Office_returns_null_when_the_text_names_no_office()
        => Assert.Null(OfficeCatalog.TryResolve("SI, ATENDIDO"));
}
