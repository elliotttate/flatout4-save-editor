namespace FlatOut4SaveEditor.Services;

// Display data for ids stored in the save, taken from the game's own tables
// (FODefines.h enums, Events*Level*.cfg, FOTrophiesDesc.inl, LOCALISATION.h).
public static class FlatOut4GameData
{
    public const long CarTypeNone = 39;

    public sealed record CareerEvent(string Index, uint Id, string ClassName, int Tier, string Kind, int Number)
    {
        public string DisplayName => $"{Kind} {Number}";

        public string FullName => $"{ClassName} · Tier {Tier + 1} · {DisplayName}";
    }

    public sealed record TrophyInfo(string Id, string Title, string Description);

    // Index strings from Flatout/Data/Common/Tuning/Factories/Events{Derby,Classic,AllStars}Level{0,1,2}.cfg.
    private static readonly string[] CareerEventIndices =
    [
        "DerbyLevel0Cup1",
        "DerbyLevel0Cup2",
        "DerbyTimeTrial0Event1",
        "DerbySurvivor0Event2",
        "DerbyLevel1Cup1",
        "DerbyLevel1Cup2",
        "DerbyLevel1Cup3",
        "DerbyTimeTrial1Event1",
        "DerbySurvivor1Event2",
        "DerbySurvivor1Event3",
        "DerbyLevel2Cup1",
        "DerbyLevel2Cup2",
        "DerbyLevel2Cup3",
        "DerbySurvivor2Event1",
        "DerbyTimeTrial2Event2",
        "DerbyTimeTrial2Event3",
        "ClassicLevel0Cup1",
        "ClassicLevel0Cup2",
        "ClassicLevel0Cup3",
        "ClassicSurvivor0Event1",
        "ClassicTimeTrial0Event2",
        "ClassicSurvivor0Event3",
        "ClassicLevel1Cup1",
        "ClassicLevel1Cup2",
        "ClassicLevel1Cup3",
        "ClassicTimeTrial1Event1",
        "ClassicSurvivor1Event2",
        "ClassicTimeTrial1Event3",
        "ClassicLevel2Cup1",
        "ClassicLevel2Cup2",
        "ClassicSurvivor2Event1",
        "ClassicTimeTrial2Event2",
        "AllstarsLevel0Cup1",
        "AllstarsLevel0Cup2",
        "AllstarsSurvivor0Event1",
        "AllstarsTimeTrial0Event2",
        "AllstarsLevel1Cup1",
        "AllstarsLevel1Cup2",
        "AllstarsLevel1Cup3",
        "AllstarsSurvivor1Event1",
        "AllstarsSurvivor1Event2",
        "AllstarsTimeTrial1Event3",
        "AllstarsLevel2Cup1",
        "AllstarsLevel2Cup2",
        "AllstarsLevel2Cup3",
        "AllstarsTimeTrial2Event1",
        "AllstarsSurvivor2Event2",
        "AllstarsTimeTrial2Event3"
    ];

    public static IReadOnlyList<CareerEvent> CareerEvents { get; } = CareerEventIndices.Select(ParseCareerEvent).ToArray();

    public static IReadOnlyDictionary<long, string> CareerEventLabels { get; } = BuildCareerEventLabels();

    public static IReadOnlyList<string> CareerClasses { get; } = ["Derby", "Classic", "All-Stars"];

    // FOCarType (FODefines.h). The value stored in each garage car slot.
    private static readonly string[] CarTypeIds =
    [
        "DERBY_Buick",
        "DERBY_Beetle",
        "DERBY_Chevy",
        "DERBY_Jeep",
        "DERBY_Furd",
        "DERBY_Viper",
        "DERBY_Phaeton",
        "DERBY_Tustang",
        "DERBY_IceCreamVan",
        "CLASSIC_Buick",
        "CLASSIC_Beetle",
        "CLASSIC_Chevy",
        "CLASSIC_Jeep",
        "CLASSIC_Furd",
        "CLASSIC_Viper",
        "CLASSIC_Phaeton",
        "CLASSIC_Tustang",
        "CLASSIC_IceCreamVan",
        "ALLSTARS_Buick",
        "ALLSTARS_Beetle",
        "ALLSTARS_Chevy",
        "ALLSTARS_Jeep",
        "ALLSTARS_Furd",
        "ALLSTARS_Viper",
        "ALLSTARS_Phaeton",
        "ALLSTARS_Tustang",
        "ALLSTARS_IceCreamVan",
        "CAR_DLC_0",
        "CAR_DLC_1",
        "CAR_DLC_2",
        "CAR_DLC_3",
        "CAR_DLC_4",
        "CAR_DLC_5",
        "CAR_DLC_6",
        "ALLSTARS_BulletGT",
        "MOTORCYCLE_KTM_450SXF",
        "MOTORCYCLE_BMW_S1000RR",
        "CLASSIC_RoboquestVan",
        "STUNT_Tustang",
        "_None_"
    ];

    public static IReadOnlyDictionary<long, string> CarTypeLabels { get; } = BuildCarTypeLabels();

    // Track slots are LocaId - TRACK_ENV_DESERT_01 (LOCALISATION.h, English text).
    private static readonly string[] TrackNames =
    [
        "Val Verde",
        "Historic Route 78",
        "Abandoned Depot",
        "Desert Outskirts",
        "Industrial City Petrochemicals",
        "Water Canal",
        "ICP Restricted Area 3F",
        "ICP Corrosive Liquids Storage",
        "Roberts and Sons Lumber Mill",
        "The Forest Trail",
        "Bruxton River Run",
        "Roberts' Storage Yard",
        "McKinsley Barrage",
        "NW Loop 8B",
        "World's End, AK",
        "Zero Degrees",
        "Redhook Invitational: Collision Course",
        "FlatOut West Coast Raceway \"The Ocho\"",
        "ICP Main Sewerline D2",
        "Val Verde City Limits: The Dragon",
        "Desert Arena",
        "Factory Arena",
        "Forest Arena",
        "Mountain Arena",
        "Square Cylinder Arena",
        "Barrage Arena",
        "Stadium",
        "Stunt: Total Destruction",
        "Stunt: Rings of Fire",
        "Stunt: Long Jump",
        "Stunt: Golf",
        "Stunt: Soccer",
        "Stunt: Baseball",
        "Stunt: Stone Skipping",
        "Stunt: Curling",
        "Stunt: Cup Pong",
        "Stunt: Billiards",
        "Stunt: Finnish Pins",
        "Stunt: High Jump",
        "Stunt: Escape the Vault",
        "Stunt: Punching Ball",
        "Stunt: Jackpot",
        "Stunt: Sorting Shapes",
        "Stunt 17",
        "Stunt 18",
        "Stunt 19",
        "Stunt 20",
        "Modern Warehouses",
        "New Docks",
        "Concrete Pillars Arena",
        "Stone Ark Place",
        "Mud Valley",
        "Earthen Halfpipe Arena",
        "Area 15",
        "Dead End Furnace",
        "Hot Spot Arena",
        "DLC track 10",
        "DLC track 11",
        "DLC track 12",
        "DLC track 13",
        "DLC track 14",
        "DLC track 15"
    ];

    public static IReadOnlyDictionary<long, string> TrackLabels { get; } = BuildTrackLabels();

    // FO_TROPHY order (FOTrophies.h) with titles/descriptions from LOCALISATION.h.
    public static IReadOnlyList<TrophyInfo> Trophies { get; } =
    [
        new("FO_TROPHY_COLLECTOR", "Collector", "Buy at least two cars in each class: Derby, Classic and All-Stars."),
        new("FO_TROPHY_ON_PODIUM", "On Podium", "Finish on the podium 10 times in a row in Career Mode."),
        new("FO_TROPHY_MONEY_MONEY", "Money Money", "Earn 123,456 cash in Career Mode."),
        new("FO_TROPHY_DESTROYER", "Destroyer", "Destroy 20 opponents online."),
        new("FO_TROPHY_PAINT_SHOP", "Paint Shop", "Get all available skins for a Derby car."),
        new("FO_TROPHY_FLATOUT_WAY", "FlatOut Way", "Kill three opponents in the same race (offline)."),
        new("FO_TROPHY_NO_SPEED_LIMIT", "No Speed Limits", "Burn nitro continuously for at least 5 seconds in a race."),
        new("FO_TROPHY_ROAD_TO_GLORY", "Road to Glory", "Complete FlatOut Mode."),
        new("FO_TROPHY_LIGHTNING_MASTER", "Lightning Master", "Complete FlatOut Mode with all gold."),
        new("FO_TROPHY_STUNTMAN_NEWBORN", "Stuntman Newbie", "Complete all stunts with at least bronze in FlatOut Mode."),
        new("FO_TROPHY_STUNTMAN_MASTER", "Stuntman Master", "Complete all stunts with gold in FlatOut Mode."),
        new("FO_TROPHY_SPEED_JUNKIE", "Speed Junkie", "Complete Career Mode."),
        new("FO_TROPHY_WELCOME_TO_THE_WORLD", "Welcome to the World!", "Win an online race."),
        new("FO_TROPHY_BACHELOR_OF_ONLINE", "Online 1st Degree", "Win 10 online races."),
        new("FO_TROPHY_ONLINE_MASTER", "Online Master", "Win 25 online races."),
        new("FO_TROPHY_HOT_ROD", "Hot Rod", "Fully upgrade an All-Stars car."),
        new("FO_TROPHY_JAY_WILL_BE_PROUD", "Jay Will Be Proud!", "Purchase every car in the game."),
        new("FO_TROPHY_GOLD_CRUSHER", "Gold Crusher", "Destroy 2,000 breakables."),
        new("FO_TROPHY_SO_SORRY", "I'm So Sorry…", "Destroy your first opponent (offline)."),
        new("FO_TROPHY_LOVE_IT", "…But I Love It!", "Destroy 50 opponents (offline)."),
        new("FO_TROPHY_SWEETHEART", "Sweetheart", "Win a race without any crash (100% life)."),
        new("FO_TROPHY_ANTI_SOCIAL", "Anti-Social", "Slam 100 opponents in online races."),
        new("FO_TROPHY_READY_FOR_JUNKYARD", "Ready for Junkyard", "Destroy your car for the first time."),
        new("FO_TROPHY_HAMSTER_KING", "Hampster King", "Win 15 races in Arena Mode."),
        new("FO_TROPHY_BABY_HAMSTER", "Baby Hampster", "Complete your first race in Arena Mode."),
        new("FO_TROPHY_WRECK_FEST", "Wreck-fest", "Wreck 5 vehicles during one round in Arena Mode."),
        new("FO_TROPHY_FLAG_THIEF", "Flag Thief", "Recover the flag 10 times in Keep the Flag."),
        new("FO_TROPHY_THE_COYOTE", "The Coyote", "Win a race in Assault Mode."),
        new("FO_TROPHY_JUNKYARD_ORGY", "Trigger Happy", "Hit 3 cars with the same weapon in Assault Mode."),
        new("FO_TROPHY_CATCH_THE_ROAD_RUNNER", "Go Catch the Road Runner", "Use all 4 weapons in Assault Mode."),
        new("FO_TROPHY_LETS_PLAY_A_GAME", "Let's Play a Game", "Win 5 Assault Mode races online."),
        new("FO_TROPHY_TRAP_ASTIC", "Lay Waste", "Win 20 Assault Mode races online."),
        new("FO_TROPHY_ROAD_ATTACK", "Road Attack", "Beat a Time Trial with gold in Career Mode."),
        new("FO_TROPHY_SMOKIN", "SSSSSMOKIN'!", "Upgrade a car once."),
        new("FO_TROPHY_UNDERCOVER", "Undercover", "Win a race with the maximum car parts fitted."),
        new("FO_TROPHY_CLASSES_WINNER", "Class Winner", "Win a race in each of the Derby, Classic and All-Stars classes."),
        new("FO_TROPHY_THAT_CAME_AS_A_SURPRISE", "That Came as a Surprise", "Eject your driver 100 times."),
        new("FO_TROPHY_RELOAD", "Reload!", "Empty a full nitro tank for the first time."),
        new("FO_TROPHY_CRASH_FOREST_CRASH", "Crash Forest, Crash!", "Drive a total distance of 500 miles."),
        new("FO_TROPHY_AIRFORCE_ONE", "AirForce One", "Jump at least 100 m with your car."),
        new("FO_TROPHY_ITS_JUST_THE_BEGINNING", "It's Just the Beginning", "Complete your first cup in Career Mode."),
        new("FO_TROPHY_RICH_BRAT", "Filthy Rich", "Score over 100,000 in a race."),
        new("FO_TROPHY_GENIUS_OR_LUCKY", "Genius or Lucky?", "Make the perfect score in a stunt game."),
        new("FO_TROPHY_DEVIL_IN_66_SECONDS", "Devil in 66 Seconds", "Keep the flag for 66 seconds in Keep the Flag."),
        new("FO_TROPHY_SURVIMINATOR", "SurviMinator", "Be both survivor and eliminator in 5 Arena Deathmatches.")
    ];

    public static readonly string[] GameModeTitles =
    [
        "Career",
        "Challenge",
        "Party",
        "Online",
        "Quick events",
        "Online lobby",
        "Unassigned"
    ];

    public static readonly string[] GameplayTypeTitles =
    [
        "Time trial",
        "Lap race",
        "Assault race",
        "Derby race",
        "Bomb race",
        "Arena survivor",
        "Arena deathmatch",
        "Keep the flag",
        "Stunt",
        "All types",
        "Unassigned"
    ];

    public static string CarTypeName(long carType) =>
        CarTypeLabels.TryGetValue(carType, out string? name) ? name : $"Car type {carType}";

    // Ids past the stock list come from tracks added later (e.g. the FlatOut 2 ports); their
    // localisation ids differ between builds, so they are shown by number rather than guessed.
    public static string TrackName(int slot) =>
        slot >= 0 && slot < TrackNames.Length ? TrackNames[slot] : $"Added track (ID {slot})";

    // Favorites index stunts from STUNT_01, which follows the 27 track/arena ids.
    private const int FirstStuntTrackSlot = 27;

    public static string StuntName(int slot)
    {
        string name = TrackName(FirstStuntTrackSlot + slot);
        return name.StartsWith("Stunt: ", StringComparison.Ordinal) ? name["Stunt: ".Length..] : $"Stunt {slot + 1}";
    }

    public static CareerEvent? FindCareerEvent(uint id) =>
        CareerEvents.FirstOrDefault(careerEvent => careerEvent.Id == id);

    // FOUtils hash used for career event ids: FNV-style multiply over the upper-cased index string.
    public static uint HashEventIndex(string value)
    {
        uint hash = 0;
        foreach (char c in value.ToUpperInvariant())
        {
            hash += (hash << 1) + (hash << 4) + (hash << 7) + (hash << 8) + (hash << 24);
            hash ^= c;
        }

        return hash;
    }

    private static CareerEvent ParseCareerEvent(string index)
    {
        string className = index.StartsWith("Derby", StringComparison.Ordinal) ? "Derby"
            : index.StartsWith("Classic", StringComparison.Ordinal) ? "Classic"
            : "All-Stars";

        string rest = index[(index.StartsWith("Allstars", StringComparison.Ordinal) ? "Allstars".Length : className.Length)..];
        string kind;
        if (rest.StartsWith("Level", StringComparison.Ordinal))
        {
            kind = "Cup";
            rest = rest["Level".Length..];
        }
        else if (rest.StartsWith("TimeTrial", StringComparison.Ordinal))
        {
            kind = "Time trial";
            rest = rest["TimeTrial".Length..];
        }
        else
        {
            kind = "Survivor";
            rest = rest["Survivor".Length..];
        }

        int tier = rest[0] - '0';
        int number = rest[^1] - '0';
        return new CareerEvent(index, HashEventIndex(index), className, tier, kind, number);
    }

    private static IReadOnlyDictionary<long, string> BuildCareerEventLabels()
    {
        var labels = new Dictionary<long, string> { [0] = "None" };
        foreach (CareerEvent careerEvent in CareerEvents)
        {
            labels[careerEvent.Id] = careerEvent.FullName;
        }

        return labels;
    }

    private static IReadOnlyDictionary<long, string> BuildCarTypeLabels()
    {
        var labels = new Dictionary<long, string>();
        for (int i = 0; i < CarTypeIds.Length; i++)
        {
            labels[i] = FormatCarType(CarTypeIds[i]);
        }

        return labels;
    }

    private static string FormatCarType(string id)
    {
        if (id == "_None_")
        {
            return "Empty slot";
        }

        if (id.StartsWith("CAR_DLC_", StringComparison.Ordinal))
        {
            return $"DLC car {int.Parse(id["CAR_DLC_".Length..]) + 1}";
        }

        int split = id.IndexOf('_');
        string prefix = id[..split];
        string model = id[(split + 1)..] switch
        {
            "IceCreamVan" => "Ice Cream Van",
            "BulletGT" => "Bullet GT",
            "KTM_450SXF" => "KTM 450 SX-F",
            "BMW_S1000RR" => "BMW S1000RR",
            "RoboquestVan" => "Roboquest Van",
            string other => other
        };

        string className = prefix switch
        {
            "DERBY" => "Derby",
            "CLASSIC" => "Classic",
            "ALLSTARS" => "All-Stars",
            "MOTORCYCLE" => "Motorcycle",
            "STUNT" => "Stunt",
            _ => prefix
        };

        return $"{model} ({className})";
    }

    private static IReadOnlyDictionary<long, string> BuildTrackLabels()
    {
        var labels = new Dictionary<long, string> { [-1] = "Empty slot" };
        for (int i = 0; i < TrackNames.Length; i++)
        {
            labels[i] = TrackNames[i];
        }

        return labels;
    }
}
