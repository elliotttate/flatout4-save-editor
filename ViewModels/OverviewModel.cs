using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.Services;

namespace FlatOut4SaveEditor.ViewModels;

public sealed class StatTile : ObservableObject
{
    private string value = string.Empty;
    private string caption = string.Empty;
    private double progress;

    public string Title { get; init; } = string.Empty;

    public string Glyph { get; init; } = string.Empty;

    public string PageKey { get; init; } = string.Empty;

    public bool HasProgress { get; init; }

    public string Value
    {
        get => value;
        set => SetField(ref this.value, value);
    }

    public string Caption
    {
        get => caption;
        set => SetField(ref caption, value);
    }

    public double Progress
    {
        get => progress;
        set => SetField(ref progress, value);
    }
}

public sealed class OverviewModel : ObservableObject
{
    private readonly SaveSession session;
    private readonly EditorCatalog catalog;
    private readonly StatTile credits = new() { Title = "Credits", Glyph = "\uE8C7", PageKey = "career" };
    private readonly StatTile medals = new() { Title = "Career", Glyph = "\uE7C1", PageKey = "career", HasProgress = true };
    private readonly StatTile trophies = new() { Title = "Trophies", Glyph = "\uE734", PageKey = "trophies", HasProgress = true };
    private readonly StatTile garage = new() { Title = "Garage", Glyph = "\uE804", PageKey = "garage", HasProgress = true };
    private readonly StatTile tracks = new() { Title = "Tracks", Glyph = "\uE707", PageKey = "garage", HasProgress = true };
    private readonly StatTile races = new() { Title = "Races played", Glyph = "\uE7FC", PageKey = "stats" };
    private readonly StatTile playTime = new() { Title = "Play time", Glyph = "\uE916", PageKey = "stats" };
    private readonly StatTile challenge = new() { Title = "Challenge score", Glyph = "\uE74A", PageKey = "challenges" };

    public OverviewModel(SaveSession session, EditorCatalog catalog)
    {
        this.session = session;
        this.catalog = catalog;
        Tiles = [credits, medals, trophies, garage, tracks, races, playTime, challenge];
        Sections = catalog.Pages;
        Refresh();
        session.DataChanged += (_, _) => Refresh();
    }

    public IReadOnlyList<StatTile> Tiles { get; }

    public IReadOnlyList<PageViewModel> Sections { get; }

    public SaveSession Session => session;

    public int HealthIssueCount => catalog.HealthIssues().Count;

    // For the file card: whether anything needs repair, or how the file had to be read.
    public string HealthLabel
    {
        get
        {
            int issues = HealthIssueCount;
            if (issues > 0)
            {
                return issues == 1 ? "1 value to repair" : $"{issues:N0} values to repair";
            }

            return session.Document.Recovery is { } recovery ? recovery.Title : "Healthy";
        }
    }

    public string FileName => session.FileName;

    public string FilePath => session.Document.Path ?? string.Empty;

    public string SourceLabel => session.Origin?.KindLabel ?? GuessSource(FilePath);

    public string SourceDetail => session.Origin?.Title ?? string.Empty;

    public string VersionLabel
    {
        get
        {
            uint original = session.Document.OriginalVersion;
            if (session.Document.Recovery is { } recovery)
            {
                return recovery.PreservesLayout
                    ? $"V{original} · newer layout kept"
                    : $"V{original} · recovered as V{FlatOut4SaveSchema.CurrentVersion}";
            }

            return original == FlatOut4SaveSchema.CurrentVersion
                ? $"V{FlatOut4SaveSchema.CurrentVersion} · current layout"
                : original == FlatOut4SaveSchema.RcTelemetryVersion
                    ? $"V{original} (RC10-RC17) · read as V{FlatOut4SaveSchema.CurrentVersion}"
                    : $"V{original} · migrated to V{FlatOut4SaveSchema.CurrentVersion}";
        }
    }

    public string SizeLabel => $"{session.Bytes.Length:N0} bytes";

    public string ModifiedLabel
    {
        get
        {
            try
            {
                return File.Exists(FilePath) ? $"Last written {File.GetLastWriteTime(FilePath):g}" : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public string ExtensionLabel
    {
        get
        {
            bool tagged = session.ReadUInt32(session.Schema.ExtensionSlotOffset(FlatOut4SaveSchema.ExtensionSlotMagic)) == FlatOut4SaveSchema.ExtensionMagicValue;
            bool telemetry = session.Field("m_aOther.TelemetryOutput").BoolValue;
            return $"Telemetry {(telemetry ? "on" : "off")} · extension slots {(tagged ? "tagged" : "untagged")}";
        }
    }

    public void Refresh()
    {
        credits.Value = session.ReadInt32(session.Field("m_oCareerData.m_iCredits").Definition.Offset).ToString("N0");
        credits.Caption = "Career wallet";

        int completed = 0;
        int gold = 0;
        foreach (FlatOut4GameData.CareerEvent careerEvent in FlatOut4GameData.CareerEvents)
        {
            int medal = catalog.Career.GetMedalIndex(careerEvent.Id);
            completed += medal > 0 ? 1 : 0;
            gold += medal == 3 ? 1 : 0;
        }

        int eventCount = FlatOut4GameData.CareerEvents.Count;
        medals.Value = $"{completed} / {eventCount}";
        medals.Caption = $"events completed · {gold} gold";
        medals.Progress = (double)completed / eventCount;

        int unlockedTrophies = FlatOut4SaveSchema.TrophyNames.Count(name => session.Field($"m_oTrophies.Unlocked[{name}]").BoolValue);
        trophies.Value = $"{unlockedTrophies} / {FlatOut4SaveSchema.TrophyNames.Length}";
        trophies.Caption = "unlocked";
        trophies.Progress = (double)unlockedTrophies / FlatOut4SaveSchema.TrophyNames.Length;

        (int carSlots, int carsAvailable, int carsOwned) = CountGarage("m_oGarageData.m_vCarsState[{0}].m_oUnlockableItem", slot => session.Field($"m_oGarageData.m_vCarsState[{slot}].m_oUnlockableItem.m_iIndex").ReadInteger() != FlatOut4GameData.CarTypeNone);
        garage.Value = $"{carsAvailable} / {carSlots}";
        garage.Caption = $"cars unlocked · {carsOwned} owned";
        garage.Progress = carSlots == 0 ? 0 : (double)carsAvailable / carSlots;

        (int trackSlots, int tracksAvailable, _) = CountGarage("m_oGarageData.m_vTrackState[{0}]", slot => session.Field($"m_oGarageData.m_vTrackState[{slot}].m_iTrackLocaDiff").ReadInteger() != -1);
        tracks.Value = $"{tracksAvailable} / {trackSlots}";
        tracks.Caption = "tracks unlocked";
        tracks.Progress = trackSlots == 0 ? 0 : (double)tracksAvailable / trackSlots;

        races.Value = session.Field("m_oStats.m_uNbGamesPlayed").ReadInteger().ToString("N0");
        races.Caption = $"{session.Field("m_oStats.m_uNbStuntsPlayed").ReadInteger():N0} stunts";

        float seconds = session.ReadSingle(session.Field("m_oStats.m_fPlayTime").Definition.Offset);
        string duration = SaveFieldViewModel.FormatDuration(seconds);
        playTime.Value = string.IsNullOrEmpty(duration) ? "0 s" : duration;
        playTime.Caption = "time in races";

        challenge.Value = session.Field("m_oChallengeModeData.m_iPlayerScore").ReadInteger().ToString("N0");
        challenge.Caption = "total score";

        OnPropertyChanged(nameof(ExtensionLabel));
    }

    private (int Slots, int Available, int Owned) CountGarage(string pathFormat, Func<int, bool> isUsed)
    {
        int slots = 0;
        int available = 0;
        int owned = 0;
        for (int slot = 0; slot < 64; slot++)
        {
            if (!isUsed(slot))
            {
                continue;
            }

            slots++;
            long state = session.Field(string.Format(pathFormat, slot) + ".m_eState").ReadInteger();
            if (state is 1 or 2 or 3 or 5)
            {
                available++;
            }

            if (state is 3 or 5)
            {
                owned++;
            }
        }

        return (slots, available, owned);
    }

    private static string GuessSource(string path)
    {
        if (path.Contains(@"\userdata\", StringComparison.OrdinalIgnoreCase))
        {
            return "Steam Cloud";
        }

        return Path.GetFileName(path).StartsWith("DEBUGSave", StringComparison.OrdinalIgnoreCase) ? "Developer test slot" : "Save file";
    }
}
