using System.Reflection;
using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class LocalizationParityTests
{
    /// <summary>
    /// Verifica la parità esatta 1:1 di tutte le chiavi di localizzazione tra il dizionario italiano e quello inglese,
    /// prevenendo errori di traduzione mancante nell'interfaccia utente.
    /// </summary>
    [Fact]
    public void Localization_ShouldHaveExactKeyParity_BetweenItalianAndEnglish()
    {
        var service = LocalizationService.Instance;

        var itField = typeof(LocalizationService).GetField("_itStrings", BindingFlags.NonPublic | BindingFlags.Instance);
        var enField = typeof(LocalizationService).GetField("_enStrings", BindingFlags.NonPublic | BindingFlags.Instance);

        itField.Should().NotBeNull();
        enField.Should().NotBeNull();

        var itDict = (Dictionary<string, string>)itField!.GetValue(service)!;
        var enDict = (Dictionary<string, string>)enField!.GetValue(service)!;

        itDict.Should().NotBeEmpty();
        enDict.Should().NotBeEmpty();

        var missingInEn = itDict.Keys.Except(enDict.Keys).ToList();
        var missingInIt = enDict.Keys.Except(itDict.Keys).ToList();

        missingInEn.Should().BeEmpty("All Italian localization keys must also exist in English dictionary");
        missingInIt.Should().BeEmpty("All English localization keys must also exist in Italian dictionary");
    }

    /// <summary>
    /// Verifica che il metodo LocalizationService.Get formatti correttamente i parametri posizionali ({0}, {1}...)
    /// sia in lingua italiana che in lingua inglese.
    /// </summary>
    [Fact]
    public void Localization_Get_ShouldReturnFormattedString_WhenKeyExists()
    {
        var service = LocalizationService.Instance;
        service.SetLanguage(AppLanguage.Italian);

        var text = service.Get("Status_StatsFormat", 10, 20, 5, 2);
        text.Should().Contain("10");
        text.Should().Contain("20");

        service.SetLanguage(AppLanguage.English);
        var enText = service.Get("Status_StatsFormat", 10, 20, 5, 2);
        enText.Should().Contain("10");
        enText.Should().Contain("20");
    }

    /// <summary>
    /// Verifica il meccanismo di fallback che restituisce la chiave stessa come stringa se la chiave richiesta non esiste.
    /// </summary>
    [Fact]
    public void Localization_Get_ShouldFallbackToKey_WhenKeyDoesNotExist()
    {
        var service = LocalizationService.Instance;
        var result = service.Get("NON_EXISTENT_KEY_XYZ_123");
        result.Should().Be("NON_EXISTENT_KEY_XYZ_123");
    }

    /// <summary>
    /// Verifica che il cambio di lingua tramite SetLanguage aggiorni la lingua attiva
    /// e restituisca le traduzioni specifiche per la lingua selezionata.
    /// </summary>
    [Fact]
    public void Localization_SetLanguage_ShouldSwitchActiveLanguageAndTranslations()
    {
        var service = LocalizationService.Instance;

        service.SetLanguage(AppLanguage.Italian);
        service.CurrentLanguage.Should().Be(AppLanguage.Italian);
        var itTitle = service.Get("Menu_File");

        service.SetLanguage(AppLanguage.English);
        service.CurrentLanguage.Should().Be(AppLanguage.English);
        var enTitle = service.Get("Menu_File");

        itTitle.Should().NotBeNullOrWhiteSpace();
        enTitle.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifica che LocalizationService gestisca chiamate a chiavi inesistenti con argomenti di formattazione senza lanciare eccezioni.
    /// </summary>
    [Fact]
    public void Localization_Get_ShouldHandleNonExistentKeyWithFormatArguments_Safely()
    {
        var service = LocalizationService.Instance;
        var act = () => service.Get("NON_EXISTENT_{0}_{1}", "Arg1", 42);
        act.Should().NotThrow();
        act().Should().Contain("NON_EXISTENT");
    }
}
