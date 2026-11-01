using FlatOut4SaveEditor.Models;
using FlatOut4SaveEditor.Services;

namespace FlatOut4SaveEditor.ViewModels;

// Lays the schema out as navigable pages: which fields go together, which arrays become
// tables, and the friendly names for every row. Also answers the global search box.
public sealed class EditorCatalog
{
    public const string OverviewKey = "overview";
    public const string AllFieldsKey = "all";
    public const string ChangesKey = "changes";
    public const string HealthKey = "health";

    private const string Gameplay = "m_oGameOptions.m_oGameplayOptions";
    private const double ChoiceWidth = 170;
    private const double NumberWidth = 118;
    private const double ToggleWidth = 92;
    private const double TextWidth = 150;

    public sealed record SectionInfo(string Key, string Title, string Glyph, string Category, string Subtitle);

    // Navigation order. Category "" sits above the first header.
    public static IReadOnlyList<SectionInfo> Sections { get; } =
    [
        new(OverviewKey, "Overview", "\uE80F", "", "Summary of the open save."),
        new(ChangesKey, "Review changes", "\uE81C", "", "Every unsaved change, old and new value side by side."),
        new(HealthKey, "Save health", "\uE95E", "", "Values the game cannot use, how the file was read, and data kept for newer builds."),
        new("career", "Career", "\uE7C1", "Progress", "Credits, event medals and the cup in progress."),
        new("garage", "Garage", "\uE804", "Progress", "Cars, upgrades, tracks and cosmetics."),
        new("challenges", "Challenges", "\uE7FC", "Progress", "Challenge mode scores."),
        new("trophies", "Trophies", "\uE734", "Progress", "In-game trophy list and the flags behind it."),
        new("records", "Records", "\uE916", "Progress", "Best laps and scores per track."),
        new("stats", "Statistics", "\uE9D2", "Progress", "Lifetime counters behind the profile screen and trophy checks."),
        new("usage", "Favorites", "\uEB51", "Progress", "Play counts the game uses to pick your favorite car, track and mode."),
        new("options", "Options", "\uE713", "Settings", "Gameplay, camera, wheel, VR and audio settings saved in the profile."),
        new("controls", "Controls", "\uE765", "Settings", "Race and menu bindings plus per-device tuning."),
        new("extensions", "Extensions", "\uEA86", "Advanced", "Tagged extension slots (FXT1): Workshop livery and telemetry opt-in."),
        new(AllFieldsKey, "All fields", "\uE8FD", "Advanced", "Every value in the save with its raw name, offset and type.")
    ];

    private readonly SaveSession session;
    private readonly List<PageViewModel> pages = [];

    // Pages without blocks that search should still find, with the words people look for them by.
    private static readonly IReadOnlyDictionary<string, string> PageKeywords = new Dictionary<string, string>
    {
        [OverviewKey] = "summary dashboard home start file",
        [ChangesKey] = "unsaved edits diff compare revert undo history",
        [HealthKey] = "repair fix damaged broken corrupt recovery recovered problems errors"
    };

    // Other words people use for a field than the ones in its label and description.
    private static readonly IReadOnlyDictionary<string, string> FieldSynonyms = new Dictionary<string, string>
    {
        ["Credits"] = "money cash",
        ["Vibration level"] = "rumble",
        ["Speed unit"] = "mph km/h kmh",
        ["AI difficulty"] = "opponents easy hard"
    };

    private static readonly IReadOnlyList<PageViewModel> SearchablePages = Sections
        .Where(section => PageKeywords.ContainsKey(section.Key))
        .Select(section => new PageViewModel
        {
            Key = section.Key,
            Title = section.Title,
            Glyph = section.Glyph,
            Category = section.Category,
            Subtitle = $"{section.Subtitle} {PageKeywords[section.Key]}"
        })
        .ToArray();
    private readonly List<TableRowViewModel> dynamicRows = [];
    private readonly List<CareerEventsBlock> careerBlocks = [];
    private readonly List<TrophyGridBlock> trophyBlocks = [];
    private readonly List<TableBlock> tables = [];
    private readonly NoteBlock extensionNote = new() { Title = "Status" };
    private readonly Dictionary<string, SaveFieldViewModel[]> pageFields = [];
    private HashSet<SaveFieldViewModel>? shownFields;
    private readonly Dictionary<SaveFieldViewModel, Func<ChangeEntry>> changeEntries = [];

    public EditorCatalog(SaveSession session)
    {
        this.session = session;
        Career = new CareerProgress(session);

        pages.Add(BuildCareerPage());
        pages.Add(BuildGaragePage());
        pages.Add(BuildChallengesPage());
        pages.Add(BuildTrophiesPage());
        pages.Add(BuildRecordsPage());
        pages.Add(BuildStatsPage());
        pages.Add(BuildUsagePage());
        pages.Add(BuildOptionsPage());
        pages.Add(BuildControlsPage());
        pages.Add(BuildExtensionsPage());
        SectionInfo all = Sections.First(section => section.Key == AllFieldsKey);
        pages.Add(new PageViewModel
        {
            Key = all.Key,
            Title = all.Title,
            Glyph = all.Glyph,
            Category = all.Category,
            Subtitle = all.Subtitle,
            FieldCount = session.Fields.Count
        });

        foreach (TableBlock table in tables)
        {
            table.Refilter();
        }

        foreach (PageViewModel page in pages)
        {
            pageFields[page.Key] = page.Blocks.SelectMany(FieldsOf).Distinct().ToArray();
        }

        IndexChangeEntries();
        QuickActions = BuildQuickActions();

        RefreshExtensionNote();
        session.DataChanged += OnDataChanged;
    }

    public CareerProgress Career { get; }

    // The Overview's one-click edits, each one undo step. They reuse the pages' own actions.
    public IReadOnlyList<QuickAction> QuickActions { get; private set; } = [];

    public IReadOnlyList<PageViewModel> Pages => pages;

    public PageViewModel? Page(string key) => pages.FirstOrDefault(page => page.Key == key);

    public void Detach() => session.DataChanged -= OnDataChanged;

    // Edited values on a page, for the navigation badges.
    public int ModifiedCount(string pageKey) =>
        pageFields.TryGetValue(pageKey, out SaveFieldViewModel[]? fields) ? fields.Count(field => field.IsModified) : 0;

    // Unsaved changes grouped by the section that shows them, in navigation order.
    public IReadOnlyList<ChangeGroup> ChangeGroups()
    {
        PageViewModel all = Page(AllFieldsKey)!;
        return session.ChangedFields()
            .Select(Describe)
            .GroupBy(entry => entry.Location?.Page ?? all)
            .OrderBy(group => pages.IndexOf(group.Key))
            .Select(group => new ChangeGroup(group.Key.Title, group.Key.Glyph, group.ToArray()))
            .ToArray();
    }

    public int ChangeCount => session.ChangedFields().Count;

    private IReadOnlyList<QuickAction> BuildQuickActions()
    {
        int eventCount = careerBlocks.Sum(block => block.Events.Count);
        var actions = new List<QuickAction>
        {
            new(
                "Gold every career event",
                "\uE735",
                $"A gold medal in all {eventCount} career events, with their result slots.",
                new RelayCommand(() => session.Batch(() =>
                {
                    foreach (CareerEventViewModel careerEvent in careerBlocks.SelectMany(block => block.Events))
                    {
                        Career.SetMedalIndex(careerEvent.Event.Id, 3);
                    }
                }, "Gold every career event")),
                "Every career event now has gold.")
        };

        if (tables.FirstOrDefault(table => table.Title == "Cars")?.Actions.FirstOrDefault(action => action.Label == "Own all") is { } ownCars)
        {
            actions.Add(new QuickAction("Own every car", "\uE804", "Every car in the garage marked Owned. DLC cars are left alone.", ownCars.Command, "Every car is now owned."));
        }

        if (trophyBlocks.FirstOrDefault()?.Actions.FirstOrDefault(action => action.Label == "Unlock all") is { } unlockTrophies)
        {
            actions.Add(new QuickAction("Unlock all trophies", "\uE734", $"All {FlatOut4SaveSchema.TrophyNames.Length} in-game trophies. Steam achievements are separate.", unlockTrophies.Command, "Every trophy is unlocked."));
        }

        SaveFieldViewModel credits = session.Field("m_oCareerData.m_iCredits");
        actions.Add(new QuickAction(
            "Add 1,000,000 credits",
            "\uE8C7",
            "Adds a million to the career wallet.",
            new RelayCommand(() => session.Group(() => credits.WriteInteger(Math.Min(credits.ReadInteger() + 1_000_000, int.MaxValue)), "Add 1,000,000 credits")),
            "Added 1,000,000 credits."));
        return actions;
    }

    // Where a field is shown, so Review changes and Save health can jump straight to it.
    public ChangeEntry Describe(SaveFieldViewModel field) =>
        changeEntries.TryGetValue(field, out Func<ChangeEntry>? entry) ? entry() : new ChangeEntry(field, field.FullName, $"All fields · {field.RawName}", null);

    public IReadOnlyList<SaveHealthIssue> HealthIssues() => FlatOut4SaveHealth.Check(session.Bytes, session.Schema);

    // Issues by kind, in the order the health page lists them.
    public IReadOnlyList<HealthGroup> HealthGroups(IReadOnlyList<SaveHealthIssue> issues) =>
        FlatOut4SaveHealth.Categories
            .Select(category => issues.Where(issue => issue.Category == category).ToArray())
            .Where(group => group.Length > 0)
            .Select(group => new HealthGroup(group[0].Category, group.Select(issue => new HealthIssueViewModel(issue, Describe(session.Field(issue.Field.Name)))).ToArray()))
            .ToArray();

    // One undo step for the lot.
    public void Repair(IReadOnlyList<SaveHealthIssue> issues, string label) => session.Batch(() =>
    {
        foreach (SaveHealthIssue issue in issues)
        {
            session.WriteBytes(issue.Field.Offset, issue.Repair);
        }
    }, label);

    // Where each field is shown first, so a change can jump straight to it.
    private void IndexChangeEntries()
    {
        foreach (PageViewModel page in pages)
        {
            foreach (PageBlock block in page.Blocks)
            {
                string context = $"{page.Title} › {block.Title}";
                switch (block)
                {
                    case FieldGroupBlock group:
                        foreach (SaveFieldViewModel field in group.Fields)
                        {
                            AddChangeEntry(field, field.Label, context, page, block, field);
                        }

                        break;
                    case TrophyGridBlock trophies:
                        foreach (SaveFieldViewModel trophy in trophies.Trophies)
                        {
                            AddChangeEntry(trophy, $"Trophy: {trophy.Label}", context, page, block, trophy);
                        }

                        break;
                    case TableBlock table:
                        foreach (TableRowViewModel row in table.AllRows)
                        {
                            for (int i = 0; i < row.Cells.Count; i++)
                            {
                                if (row.Cells[i].Field is { } field)
                                {
                                    string column = table.Columns[i].Header;
                                    AddChangeEntry(field, string.Empty, context, page, block, row, () => $"{row.Title} · {column}");
                                }
                            }
                        }

                        break;
                }
            }
        }
    }

    private void AddChangeEntry(SaveFieldViewModel field, string title, string context, PageViewModel page, PageBlock block, object target, Func<string>? dynamicTitle = null)
    {
        if (changeEntries.ContainsKey(field))
        {
            return;
        }

        // Table rows can be renamed by edits (a car slot's type), so their titles are taken when listed.
        var location = new SearchResult { Title = title, Context = context, Glyph = page.Glyph, Page = page, Block = block, Target = target };
        changeEntries[field] = () => new ChangeEntry(field, dynamicTitle?.Invoke() ?? title, context, location);
    }

    private static IEnumerable<SaveFieldViewModel> FieldsOf(PageBlock block) => block switch
    {
        FieldGroupBlock group => group.Fields,
        TrophyGridBlock trophies => trophies.Trophies,
        TableBlock table => table.AllRows.SelectMany(row => row.Cells).Select(cell => cell.Field).OfType<SaveFieldViewModel>(),
        _ => []
    };

    public IReadOnlyList<SearchResult> Search(string query, int max = 14)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        var results = new List<SearchResult>();
        foreach (PageViewModel page in SearchablePages)
        {
            AddIfMatch(results, query, page.Title, "Section", page.Glyph, page, null, null, page.Subtitle, bonus: 3);
        }

        foreach (PageViewModel page in pages)
        {
            AddIfMatch(results, query, page.Title, "Section", page.Glyph, page, null, null, page.Subtitle, bonus: 3);
            foreach (PageBlock block in page.Blocks)
            {
                string context = string.IsNullOrEmpty(block.Title) ? page.Title : $"{page.Title} › {block.Title}";
                AddIfMatch(results, query, block.Title, page.Title, page.Glyph, page, block, null, block.Description, bonus: 2);
                switch (block)
                {
                    case FieldGroupBlock group:
                        foreach (SaveFieldViewModel field in group.Fields)
                        {
                            AddIfMatch(results, query, field.Label, context, page.Glyph, page, block, field, $"{field.RawName} {field.Description} {FieldSynonyms.GetValueOrDefault(field.Label)}");
                        }

                        break;
                    case TrophyGridBlock trophies:
                        foreach (SaveFieldViewModel trophy in trophies.Trophies)
                        {
                            AddIfMatch(results, query, trophy.Label, context, "\uE734", page, block, trophy, $"{trophy.RawName} {trophy.Description}");
                        }

                        break;
                    case CareerEventsBlock events:
                        foreach (CareerEventViewModel careerEvent in events.Events)
                        {
                            AddIfMatch(results, query, careerEvent.Event.FullName, context, careerEvent.KindGlyph, page, block, careerEvent, $"{careerEvent.Event.Index} {careerEvent.Subtitle}");
                        }

                        break;
                    case TableBlock table:
                        // Column names only count when every word must match, so "beetle motor" finds
                        // the Beetle's row in Cars without "motor" alone listing every car.
                        string columns = string.Join(" ", table.Columns.Select(column => $"{column.Header} {column.Tooltip}"));
                        foreach (TableRowViewModel row in table.AllRows)
                        {
                            if (table.CanHideEmpty && row.IsEmpty)
                            {
                                continue;
                            }

                            AddIfMatch(results, query, row.Title, context, page.Glyph, page, block, row, row.Subtitle, keywords: columns);
                        }

                        break;
                }
            }
        }

        // Values no section shows (header markers, the version, unknown extension slots) open All
        // fields filtered to them, below anything a section shows.
        PageViewModel all = Page(AllFieldsKey)!;
        shownFields ??= pageFields.Values.SelectMany(fields => fields).ToHashSet();
        foreach (SaveFieldViewModel field in session.Fields)
        {
            if (!shownFields.Contains(field))
            {
                AddIfMatch(results, query, field.Label, $"{all.Title} › {field.Section}", all.Glyph, all, null, field, field.RawName, bonus: -12);
            }
        }

        // Among equal matches, a title that starts with the first word ("brake key": "Brake / reverse"
        // before "Handbrake") reads as the better hit.
        string firstWord = Words(query)[0];
        return results
            .OrderByDescending(result => result.Rank)
            .ThenByDescending(result => result.Title.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase))
            .ThenBy(result => result.Title.Length)
            .Take(max)
            .ToArray();
    }

    private static string[] Words(string query) => query.Split([' ', '·', '›', ','], StringSplitOptions.RemoveEmptyEntries);

    private static bool HasEveryWord(string query, string text)
    {
        string[] words = Words(query);
        return words.Length > 1 && words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddIfMatch(
        List<SearchResult> results,
        string query,
        string title,
        string context,
        string glyph,
        PageViewModel page,
        PageBlock? block,
        object? target,
        string extra,
        int bonus = 0,
        string keywords = "")
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        int rank;
        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            rank = 30;
        }
        else if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            rank = 20;
        }
        else if (extra.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            rank = 10;
        }
        else if (HasEveryWord(query, title))
        {
            // "derby cup 1" finds "Derby · Tier 1 · Cup 1": every word, in any order.
            rank = 8;
        }
        else if (HasEveryWord(query, $"{title} {context} {extra}") ||
                 (keywords.Length > 0 && HasEveryWord(query, $"{title} {context} {extra} {keywords}") &&
                  Words(query).Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase))))
        {
            rank = 4;
        }
        else
        {
            return;
        }

        results.Add(new SearchResult
        {
            Title = title,
            Context = context,
            Glyph = glyph,
            Page = page,
            Block = block,
            Target = target,
            Rank = rank + bonus
        });
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        foreach (TableRowViewModel row in dynamicRows)
        {
            row.RefreshText();
        }

        foreach (CareerEventsBlock block in careerBlocks)
        {
            block.Refresh();
        }

        foreach (TrophyGridBlock block in trophyBlocks)
        {
            block.Refresh();
        }

        RefreshExtensionNote();
    }

    private SaveFieldViewModel F(string rawName) => session.Field(rawName);

    private FieldGroupBlock Group(string title, string description, params string[] rawNames) => new()
    {
        Title = title,
        Description = description,
        Fields = rawNames.Select(F).ToArray()
    };

    private TableBlock Table(string title, string description, string titleHeader, double titleWidth, params TableColumn[] columns)
    {
        var table = new TableBlock(columns, titleWidth, titleHeader)
        {
            Title = title,
            Description = description
        };
        tables.Add(table);
        return table;
    }

    private TableBlock HideableTable(string title, string description, string titleHeader, double titleWidth, string hideLabel, params TableColumn[] columns)
    {
        var table = new TableBlock(columns, titleWidth, titleHeader)
        {
            Title = title,
            Description = description,
            CanHideEmpty = true,
            HideEmptyLabel = hideLabel
        };
        tables.Add(table);
        return table;
    }

    private TableRowViewModel Dynamic(TableRowViewModel row)
    {
        dynamicRows.Add(row);
        return row;
    }

    private static int CountFields(IEnumerable<PageBlock> blocks) => blocks.Sum(block => block switch
    {
        FieldGroupBlock group => group.Fields.Count,
        TrophyGridBlock trophies => trophies.Trophies.Count,
        CareerEventsBlock events => events.Events.Count,
        TableBlock table => table.AllRows.Sum(row => row.Cells.Count(cell => cell.HasField)),
        _ => 0
    });

    private static PageViewModel MakePage(string key, List<PageBlock> blocks)
    {
        SectionInfo info = Sections.First(section => section.Key == key);
        return new PageViewModel
        {
            Key = key,
            Title = info.Title,
            Glyph = info.Glyph,
            Category = info.Category,
            Subtitle = info.Subtitle,
            Blocks = blocks,
            FieldCount = CountFields(blocks)
        };
    }

    private PageViewModel BuildCareerPage()
    {
        var blocks = new List<PageBlock>
        {
            Group("Wallet", string.Empty, "m_oCareerData.m_iCredits")
        };

        foreach (string className in FlatOut4GameData.CareerClasses)
        {
            CareerEventViewModel[] events = FlatOut4GameData.CareerEvents
                .Where(careerEvent => careerEvent.ClassName == className)
                .Select(careerEvent => new CareerEventViewModel(careerEvent, Career))
                .ToArray();

            var block = new CareerEventsBlock
            {
                Title = $"{className} events",
                Description = "Pick the medal for each event. Giving a medal to an event the profile has never finished adds its result slot, like finishing it in game.",
                Events = events
            };
            block.Actions =
            [
                new BlockAction("Gold all", "\uE735", new RelayCommand(() => session.Batch(() =>
                {
                    foreach (CareerEventViewModel careerEvent in events)
                    {
                        Career.SetMedalIndex(careerEvent.Event.Id, 3);
                    }
                }, $"Gold all {className} events")), $"Give every {className} event a gold medal"),
                new BlockAction("Clear medals", "\uE894", new RelayCommand(() => session.Batch(() =>
                {
                    foreach (CareerEventViewModel careerEvent in events)
                    {
                        Career.SetMedalIndex(careerEvent.Event.Id, 0);
                    }
                }, $"Clear {className} medals")), $"Mark every {className} event as not completed")
            ];
            careerBlocks.Add(block);
            blocks.Add(block);
        }

        blocks.Add(Group(
            "Cup in progress",
            "The cup the player is part-way through, and which round they are on.",
            "m_oCareerData.m_oCurrentEvent.m_iCurrentEventIndex",
            "m_oCareerData.m_oCurrentEvent.m_iCurrentRound"));

        TableBlock standings = HideableTable(
            "Cup standings",
            "Points per driver for the cup in progress.",
            "Entry",
            150,
            "Hide empty entries",
            new TableColumn("Driver index", NumberWidth),
            new TableColumn("Points", NumberWidth),
            new TableColumn("Player", ToggleWidth),
            new TableColumn("Name ID", NumberWidth));
        for (int i = 0; i < 32; i++)
        {
            string path = $"m_oCareerData.m_oCurrentEvent.m_vDriversScore[{i}]";
            SaveFieldViewModel index = F($"{path}.m_iIndex");
            standings.AddRow(
                $"Entry {i + 1}",
                [index, F($"{path}.m_iScore"), F($"{path}.m_bPlayer"), F($"{path}.m_oName")],
                isEmpty: () => index.ReadInteger() == -1);
        }

        blocks.Add(standings);

        TableBlock rewards = HideableTable(
            "Event result slots",
            "The raw FOCareerEventReward table behind the medals above.",
            "Event",
            300,
            "Hide empty slots",
            new TableColumn("Event ID", TextWidth),
            new TableColumn("Medal", 130),
            new TableColumn("New", ToggleWidth),
            new TableColumn("Popup shown", ToggleWidth + 18));
        for (int i = 0; i < CareerProgress.SlotCount; i++)
        {
            string path = $"m_oCareerData.m_vEventRewards[{i}]";
            SaveFieldViewModel id = F($"{path}.m_oId");
            int slot = i;
            rewards.AddRow(
                string.Empty,
                [id, F($"{path}.m_oScore"), F($"{path}.m_bNew"), F($"{path}.m_bPoped")],
                isEmpty: () => id.ReadInteger() == 0,
                titleProvider: () => DescribeEventSlot((uint)id.ReadInteger()),
                subtitleProvider: () => $"Slot {slot + 1}");
            Dynamic(rewards.AllRows[^1]);
        }

        blocks.Add(rewards);

        return MakePage("career", blocks);
    }

    private static string DescribeEventSlot(uint id)
    {
        if (id == 0)
        {
            return "Empty slot";
        }

        FlatOut4GameData.CareerEvent? careerEvent = FlatOut4GameData.FindCareerEvent(id);
        return careerEvent?.FullName ?? $"Other event (0x{id:X8})";
    }

    private PageViewModel BuildGaragePage()
    {
        var blocks = new List<PageBlock>();

        TableBlock cars = HideableTable(
            "Cars",
            "Unlock state and installed upgrade level per category. Stock means nothing bought; Derby cars go to 3, Classic to 4, All-Stars to 5.",
            "Car",
            200,
            "Hide empty slots",
            new TableColumn("State", 136),
            new TableColumn("Motor", 86),
            new TableColumn("Gearbox", 86),
            new TableColumn("Exhaust", 86),
            new TableColumn("Nitro", 86),
            new TableColumn("Wheels", 86, "Wheels & brakes"),
            new TableColumn("Chassis", 86));
        for (int i = 0; i < 64; i++)
        {
            string path = $"m_oGarageData.m_vCarsState[{i}]";
            SaveFieldViewModel carType = F($"{path}.m_oUnlockableItem.m_iIndex");
            var cells = new List<SaveFieldViewModel?> { F($"{path}.m_oUnlockableItem.m_eState") };
            cells.AddRange(FlatOut4SaveSchema.UpgradeCategoryNames.Select(category => F($"{path}.m_iUpgradeLevel[{category}]")));
            int slot = i;
            Dynamic(cars.AddRow(
                string.Empty,
                cells,
                isEmpty: () => carType.ReadInteger() == FlatOut4GameData.CarTypeNone,
                titleProvider: () => FlatOut4GameData.CarTypeName(carType.ReadInteger()),
                subtitleProvider: () => $"Garage slot {slot + 1}"));
        }

        cars.Actions = GarageStateActions(cars, "cars");
        blocks.Add(cars);

        TableBlock paint = HideableTable(
            "Selected paint",
            "Paint each car slot drives with. Default paint lets the game pick the car's current skin.",
            "Car",
            240,
            "Hide empty slots",
            new TableColumn("Paint", 340));
        for (int i = 0; i < 64; i++)
        {
            SaveFieldViewModel carType = F($"m_oGarageData.m_vCarsState[{i}].m_oUnlockableItem.m_iIndex");
            int slot = i;
            Dynamic(paint.AddRow(
                string.Empty,
                [F($"m_oGarageData.m_vCarSkins[{i}]")],
                isEmpty: () => carType.ReadInteger() == FlatOut4GameData.CarTypeNone,
                titleProvider: () => FlatOut4GameData.CarTypeName(carType.ReadInteger()),
                subtitleProvider: () => $"Garage slot {slot + 1}"));
        }

        blocks.Add(paint);

        TableBlock loadouts = Table(
            "Selected loadouts",
            "Car, driver and cosmetics picked for each game mode.",
            "Mode",
            180,
            new TableColumn("Car", 190),
            new TableColumn("Driver", 170),
            new TableColumn("Skin", 240),
            new TableColumn("Horn", 150),
            new TableColumn("Boost effect", 150));
        foreach ((string title, string prefix) in new[]
        {
            ("Career", "m_oCareerGarageConfig"),
            ("Single player", "m_oSingleGarageConfig"),
            ("Multiplayer", "m_oMultiGarageConfig")
        })
        {
            loadouts.AddRow(title,
            [
                F($"{prefix}.m_uCarType"),
                F($"{prefix}.m_uDriverType"),
                F($"{prefix}.m_uCarSkin"),
                F($"{prefix}.m_uHornType"),
                F($"{prefix}.m_uBoostFXType")
            ]);
        }

        blocks.Add(loadouts);

        TableBlock tracks = HideableTable(
            "Tracks",
            "Unlock state per track or arena.",
            "Track",
            320,
            "Hide empty slots",
            new TableColumn("State", ChoiceWidth));
        for (int i = 0; i < 64; i++)
        {
            string path = $"m_oGarageData.m_vTrackState[{i}]";
            SaveFieldViewModel trackId = F($"{path}.m_iTrackLocaDiff");
            int slot = i;
            Dynamic(tracks.AddRow(
                string.Empty,
                [F($"{path}.m_eState")],
                isEmpty: () => trackId.ReadInteger() == -1,
                titleProvider: () => FlatOut4GameData.TrackName((int)trackId.ReadInteger()),
                subtitleProvider: () => $"Track slot {slot + 1}"));
        }

        tracks.Actions = GarageStateActions(tracks, "tracks");
        blocks.Add(tracks);

        blocks.Add(UnlockableTable("Car skins", "Paint jobs, named from the game's skin list. Unlisted skins (Workshop liveries, or skins from other builds) show their hash.", "m_oGarageData.m_vCarSkinsState", 512, id => Named(FlatOut4CustomizationNames.CarSkins, id, $"Unlisted skin (0x{(uint)id:X8})")));
        blocks.Add(UnlockableTable("Drivers", "Driver characters.", "m_oGarageData.m_vDriversState", 64, id => Named(FlatOut4CustomizationNames.Drivers, id, $"Driver {id}")));
        blocks.Add(UnlockableTable("Boost effects", "Nitro flame effects.", "m_oGarageData.m_vBoostFxState", 64, id => Named(FlatOut4CustomizationNames.BoostEffects, id, $"Boost effect 0x{(uint)id:X8}")));
        blocks.Add(UnlockableTable("Horns", "Car horns.", "m_oGarageData.m_vCarHornState", 64, id => Named(FlatOut4CustomizationNames.Horns, id, $"Horn 0x{(uint)id:X8}")));

        return MakePage("garage", blocks);
    }

    private static string Named(IReadOnlyDictionary<long, string> names, long id, string fallback) =>
        names.TryGetValue((uint)id, out string? name) ? name : fallback;

    private TableBlock UnlockableTable(string title, string description, string prefix, int count, Func<long, string> name)
    {
        TableBlock table = HideableTable(title, description, title.TrimEnd('s'), 260, "Hide empty slots", new TableColumn("State", ChoiceWidth));
        for (int i = 0; i < count; i++)
        {
            SaveFieldViewModel id = F($"{prefix}[{i}].m_iIndex");
            int slot = i;
            Dynamic(table.AddRow(
                string.Empty,
                [F($"{prefix}[{i}].m_eState")],
                isEmpty: () => id.ReadInteger() == 0,
                titleProvider: () => id.ReadInteger() == 0 ? "Empty slot" : name(id.ReadInteger()),
                subtitleProvider: () => $"Slot {slot + 1}"));
        }

        table.Actions = GarageStateActions(table, title.ToLowerInvariant());
        return table;
    }

    // Garage states that unlocking leaves alone, like FOGarageDataStruct::UnlockAll.
    private static bool IsOwnedOrDlc(long state) => state is 3 or 4 or 5 or 6;

    private IReadOnlyList<BlockAction> GarageStateActions(TableBlock table, string noun) =>
    [
        new BlockAction("Unlock all", "\uE785", new RelayCommand(() => session.Batch(() =>
        {
            foreach (TableRowViewModel row in table.AllRows.Where(row => !row.IsEmpty))
            {
                SaveFieldViewModel state = row.Cells[0].Field!;
                if (!IsOwnedOrDlc(state.ReadInteger()))
                {
                    state.WriteInteger(1);
                }
            }
        }, $"Unlock all {noun}")), $"Mark every locked {noun} entry as New (owned and DLC entries are left alone)"),
        new BlockAction("Own all", "\uE73E", new RelayCommand(() => session.Batch(() =>
        {
            foreach (TableRowViewModel row in table.AllRows.Where(row => !row.IsEmpty))
            {
                SaveFieldViewModel state = row.Cells[0].Field!;
                if (state.ReadInteger() is not (4 or 5 or 6))
                {
                    state.WriteInteger(3);
                }
            }
        }, $"Own all {noun}")), $"Mark every {noun} entry as Owned (DLC entries are left alone)")
    ];

    private PageViewModel BuildChallengesPage()
    {
        var blocks = new List<PageBlock>
        {
            Group("Score", "Sum shown on the challenge menu.", "m_oChallengeModeData.m_iPlayerScore")
        };

        TableBlock challenges = HideableTable(
            "Challenges",
            "Best score per challenge and whether it is still flagged as new.",
            "Challenge",
            200,
            "Hide unplayed challenges",
            new TableColumn("Best score", 140),
            new TableColumn("New", ToggleWidth));
        for (int i = 0; i < 128; i++)
        {
            SaveFieldViewModel record = F($"m_oChallengeModeData.m_oChallengesRecords[{i}]");
            SaveFieldViewModel isNew = F($"m_oChallengeModeData.m_oNewChallenges[{i}]");
            challenges.AddRow($"Challenge {i + 1}", [record, isNew], isEmpty: () => record.ReadInteger() == 0 && !isNew.ReadBool());
        }

        challenges.Actions =
        [
            new BlockAction("Max all", "\uE74A", new RelayCommand(() => session.Batch(() =>
            {
                F("m_oChallengeModeData.m_iPlayerScore").WriteInteger(int.MaxValue);
                foreach (TableRowViewModel row in challenges.AllRows)
                {
                    row.Cells[0].Field!.WriteInteger(int.MaxValue);
                }
            }, "Max all challenges")), "Same as the game's debug unlock: every record and the total set to the maximum")
        ];
        blocks.Add(challenges);

        return MakePage("challenges", blocks);
    }

    private PageViewModel BuildTrophiesPage()
    {
        SaveFieldViewModel[] trophies = FlatOut4SaveSchema.TrophyNames
            .Select(name => F($"m_oTrophies.Unlocked[{name}]"))
            .ToArray();

        var grid = new TrophyGridBlock
        {
            Title = "Trophies",
            Description = "Unlocked trophies are stored as bits in the profile. Steam achievements are separate and are not changed here.",
            Trophies = trophies,
            Actions =
            [
                new BlockAction("Unlock all", "\uE785", new RelayCommand(() => session.Batch(() =>
                {
                    foreach (SaveFieldViewModel trophy in trophies)
                    {
                        trophy.WriteBool(true);
                    }
                }, "Unlock all trophies"))),
                new BlockAction("Lock all", "\uE72E", new RelayCommand(() => session.Batch(() =>
                {
                    foreach (SaveFieldViewModel trophy in trophies)
                    {
                        trophy.WriteBool(false);
                    }
                }, "Lock all trophies")))
            ]
        };

        trophyBlocks.Add(grid);
        var blocks = new List<PageBlock>
        {
            grid,
            Group(
                "Recently unlocked",
                "The hero-stats strip on the profile screen.",
                "m_oStats.m_uNbRecentTrophies",
                "m_oStats.m_vRecentTrophies[0]",
                "m_oStats.m_vRecentTrophies[1]",
                "m_oStats.m_vRecentTrophies[2]",
                "m_oStats.m_vRecentTrophies[3]"),
            Group(
                "Trophy progress flags",
                "One-off conditions the trophy checks remember between sessions.",
                "m_oStats.m_bUnlockedCrashForrestCrash",
                "m_oStats.m_bHighestScoreInStuntReached",
                "m_oStats.m_bCollector",
                "m_oStats.m_bAllStarsCarMaxUpgrades",
                "m_oStats.m_bRaceWonWithoutAnyCrash",
                "m_oStats.m_bDestroyedCarForTheFirstTime",
                "m_oStats.m_bExhaustNitroForTheFirstTime")
        };

        return MakePage("trophies", blocks);
    }

    private PageViewModel BuildRecordsPage()
    {
        TableBlock records = HideableTable(
            "Track records",
            "Best lap times (m:ss.fff) and best scores per track. Type a time, or \"No record\" to clear one.",
            "Track",
            200,
            "Hide tracks without records",
            new TableColumn("Time trial lap", 112),
            new TableColumn("Race lap", 112),
            new TableColumn("Stunt", 88),
            new TableColumn("Carnage", 88),
            new TableColumn("Survivor", 88),
            new TableColumn("Keep the flag", 88),
            new TableColumn("Deathmatch", 88));
        for (int i = 0; i < 64; i++)
        {
            SaveFieldViewModel[] cells =
            [
                F($"m_oRecords.m_vBestLapTimeTrial[{i}]"),
                F($"m_oRecords.m_vBestLapOther[{i}]"),
                F($"m_oRecords.m_vStuntsRecords[{i}]"),
                F($"m_oRecords.m_vCarnageRecords[{i}]"),
                F($"m_oRecords.m_vSurvivorRecords[{i}]"),
                F($"m_oRecords.m_vKTFRecords[{i}]"),
                F($"m_oRecords.m_vDMRecords[{i}]")
            ];
            records.AddRow(
                FlatOut4GameData.TrackName(i),
                cells,
                subtitle: $"Track slot {i + 1}",
                isEmpty: () => cells[0].ReadInteger() == -1 && cells[1].ReadInteger() == -1 && cells.Skip(2).All(cell => cell.ReadInteger() == 0));
        }

        return MakePage("records", [records]);
    }

    private PageViewModel BuildStatsPage()
    {
        var blocks = new List<PageBlock>
        {
            Group(
                "Time",
                "Accumulated durations, in seconds.",
                "m_oStats.m_fPlayTime",
                "m_oStats.m_fPauseDuration",
                "m_oStats.m_fOnlineDuration",
                "m_oStats.m_fAccelerationDuration",
                "m_oStats.m_fBrakeDuration"),
            Group(
                "Driving",
                "Ground distance and the longest jump are in miles, as the trophy checks use them; the other distances are in the game's own units.",
                "m_oStats.m_fDistanceTraveled",
                "m_oStats.m_fDistanceTraveledBackward",
                "m_oStats.m_fTotalGroundDistance",
                "m_oStats.m_uMaxHorizontalDistanceCoveredWithAJump",
                "m_oStats.m_fTotalHorizontalJumpDistance",
                "m_oStats.m_fTotalJumpDuration",
                "m_oStats.m_uNitroLongestBurnDuration",
                "m_oStats.m_fNitroBurnDuration"),
            Group(
                "Destruction",
                string.Empty,
                "m_oStats.m_uNbOfflineKills",
                "m_oStats.m_uNbOnlineKills",
                "m_oStats.m_uNbOfflineDeaths",
                "m_oStats.m_uNbOnlineDeaths",
                "m_oStats.m_uNbOnlineSlams",
                "m_oStats.m_uNbBreakablesDestroyed",
                "m_oStats.m_uNbDriverEjections",
                "m_oStats.m_uMaxNbCarsWreckedInOneRaceInArenaMode",
                "m_oStats.m_uNbArenaDeathMatchDominations"),
            Group(
                "Events",
                string.Empty,
                "m_oStats.m_uNbGamesPlayed",
                "m_oStats.m_uNbStuntsPlayed",
                "m_oStats.m_uNbTrapsPlaced",
                "m_oStats.m_uTrapsTypesUsedInASingleRace",
                "m_oStats.m_uNbFlagsStolenInCaptureTheFlagMode",
                "m_oStats.m_uMaxFlagCaptureDuration",
                "m_oStats.m_fTotalFlagCaptureDuration",
                "m_oStats.m_fTotalSurvivorDuration",
                "m_oStats.m_fTotalEliminatorDuration"),
            Group(
                "Medals and completion",
                "Completion values are bitmasks.",
                "m_oStats.m_uSuntsMedals[Bronze]",
                "m_oStats.m_uSuntsMedals[Silver]",
                "m_oStats.m_uSuntsMedals[Gold]",
                "m_oStats.m_uModesCompleted",
                "m_oStats.m_uCupClassesWon")
        };

        TableBlock modes = HideableTable(
            "By game mode",
            string.Empty,
            "Game mode",
            200,
            "Hide unused modes",
            new TableColumn("Credits earned", 140),
            new TableColumn("Podium streak", 128),
            new TableColumn("First places", 128));
        for (int mode = 0; mode < FlatOut4SaveSchema.GameModeNames.Length; mode++)
        {
            string path = $"m_oStats.m_oGameModeStats[{FlatOut4SaveSchema.GameModeNames[mode]}]";
            SaveFieldViewModel[] cells = [F($"{path}.m_uNbCreditsEarned"), F($"{path}.m_uNbPodiumStreaks"), F($"{path}.m_uNbFirstPos")];
            modes.AddRow(FlatOut4GameData.GameModeTitles[mode], cells, isEmpty: () => cells.All(cell => cell.ReadInteger() == 0));
        }

        blocks.Add(modes);

        TableBlock gameplay = HideableTable(
            "By mode and event type",
            "Wins, losses and best score for each event type inside each game mode. Medals is a bitmask.",
            "Event type",
            220,
            "Hide unplayed combinations",
            new TableColumn("Best score", 124),
            new TableColumn("Wins offline", NumberWidth),
            new TableColumn("Wins online", NumberWidth),
            new TableColumn("Losses offline", NumberWidth),
            new TableColumn("Losses online", NumberWidth),
            new TableColumn("Medals", TextWidth));
        for (int mode = 0; mode < FlatOut4SaveSchema.GameModeNames.Length; mode++)
        {
            for (int type = 0; type < FlatOut4SaveSchema.GamePlayTypeNames.Length; type++)
            {
                string path = $"m_oStats.m_oGameModeStats[{FlatOut4SaveSchema.GameModeNames[mode]}].m_oGameplayStats[{FlatOut4SaveSchema.GamePlayTypeNames[type]}]";
                SaveFieldViewModel[] cells =
                [
                    F($"{path}.m_uMaxScore"),
                    F($"{path}.m_uNbWon[Offline]"),
                    F($"{path}.m_uNbWon[Online]"),
                    F($"{path}.m_uNbLost[Offline]"),
                    F($"{path}.m_uNbLost[Online]"),
                    F($"{path}.m_uMedals")
                ];
                gameplay.AddRow(
                    FlatOut4GameData.GameplayTypeTitles[type],
                    cells,
                    subtitle: FlatOut4GameData.GameModeTitles[mode],
                    isEmpty: () => cells.All(cell => cell.ReadInteger() == 0));
            }
        }

        blocks.Add(gameplay);

        return MakePage("stats", blocks);
    }

    private PageViewModel BuildUsagePage()
    {
        var blocks = new List<PageBlock>
        {
            UsageTable("Cars driven", "m_oFavorites.m_vCarUsed", "Car", i => i < 40 ? FlatOut4GameData.CarTypeName(i) : $"Car slot {i + 1}"),
            UsageTable("Tracks played", "m_oFavorites.m_vTrackPlayed", "Track", FlatOut4GameData.TrackName),
            UsageTable("Event types played", "m_oFavorites.m_vGameplayTypePlayed", "Event type", i => i < FlatOut4GameData.GameplayTypeTitles.Length ? FlatOut4GameData.GameplayTypeTitles[i] : $"Type slot {i + 1}"),
            UsageTable("Stunts played", "m_oFavorites.m_vStuntsPlayed", "Stunt", FlatOut4GameData.StuntName)
        };

        return MakePage("usage", blocks);
    }

    private TableBlock UsageTable(string title, string prefix, string titleHeader, Func<int, string> name)
    {
        TableBlock table = HideableTable(title, string.Empty, titleHeader, 320, "Hide unused entries", new TableColumn("Times", 130));
        for (int i = 0; i < 64; i++)
        {
            SaveFieldViewModel count = F($"{prefix}[{i}]");
            table.AddRow(name(i), [count], isEmpty: () => count.ReadInteger() == 0);
        }

        return table;
    }

    private PageViewModel BuildOptionsPage()
    {
        var blocks = new List<PageBlock>
        {
            Group(
                "General",
                string.Empty,
                $"{Gameplay}.m_uDifficulty",
                $"{Gameplay}.m_uSpeedUnit",
                $"{Gameplay}.m_fVibrationLevel"),
            Group(
                "Camera",
                string.Empty,
                $"{Gameplay}.m_uCamXInversed",
                $"{Gameplay}.m_uCamYInversed",
                $"{Gameplay}.m_bShowDriverInInterior",
                $"{Gameplay}.m_vInteriorCamOffset.x",
                $"{Gameplay}.m_vInteriorCamOffset.y",
                $"{Gameplay}.m_vInteriorCamOffset.z"),
            Group(
                "Comfort",
                "Motion-sickness options for VR.",
                $"{Gameplay}.m_uComfortVignette",
                $"{Gameplay}.m_uDisableCameraShake"),
            Group(
                "Steering wheel",
                string.Empty,
                $"{Gameplay}.m_uFFBStrength",
                $"{Gameplay}.m_uWheelRotationDegrees",
                $"{Gameplay}.m_uPedalSwapOverride",
                $"{Gameplay}.m_uPedalSwapEnabled",
                $"{Gameplay}.m_uManualShift",
                $"{Gameplay}.m_uManualShiftRequireClutch",
                $"{Gameplay}.m_uAutoEngageManualShiftOnHShifter"),
            Group(
                "VR motion steering",
                "Virtual wheel driven by the motion controllers.",
                $"{Gameplay}.m_uVRMotionSteeringEnabled",
                $"{Gameplay}.m_uVRMotionLockToLockDeg",
                $"{Gameplay}.m_uVRMotionDeadzoneDeg",
                $"{Gameplay}.m_uVRMotionSensitivity",
                $"{Gameplay}.m_uVRMotionAutoRecenterIdle",
                $"{Gameplay}.m_uVRMotionDominantHand",
                $"{Gameplay}.m_uVRMotionEnableHaptic"),
            Group(
                "Input",
                string.Empty,
                $"{Gameplay}.m_uInputPrimaryFamily",
                "m_uInputDevice"),
            Group(
                "Audio",
                string.Empty,
                FlatOut4SaveSchema.AudioVolumeNames.Select(name => $"m_oGameOptions.m_iAudioVolumes[{name}]").ToArray()),
            Group(
                "Telemetry",
                "Stored in the tagged extension slots rather than the options block.",
                "m_aOther.TelemetryOutput")
        };

        return MakePage("options", blocks);
    }

    private static readonly Dictionary<string, string> ActionTitles = new()
    {
        ["ACTION_RACE_ACCELERATE"] = "Accelerate",
        ["ACTION_RACE_BRAKE"] = "Brake / reverse",
        ["ACTION_RACE_LEAN_LEFT"] = "Steer left",
        ["ACTION_RACE_LEAN_RIGHT"] = "Steer right",
        ["ACTION_RACE_LEAN"] = "Steer (axis)",
        ["ACTION_RACE_TOGGLE_VIEW"] = "Next camera",
        ["ACTION_RACE_TOGGLE_VIEW_PREV"] = "Previous camera",
        ["ACTION_RACE_REAR_VIEW"] = "Rear view",
        ["ACTION_RACE_LEFT_VIEW"] = "Look left",
        ["ACTION_RACE_RIGHT_VIEW"] = "Look right",
        ["ACTION_RACE_RESPAWN"] = "Reset car",
        ["ACTION_RACE_BOOST"] = "Nitro",
        ["ACTION_RACE_SKID"] = "Skid",
        ["ACTION_RACE_LOOK_BACK"] = "Look back",
        ["ACTION_RACE_HAND_BRAKE"] = "Handbrake",
        ["ACTION_RACE_GEAR_SHIFT_UP"] = "Shift up",
        ["ACTION_RACE_GEAR_SHIFT_DOWN"] = "Shift down",
        ["ACTION_RACE_GEAR_1"] = "Gear 1",
        ["ACTION_RACE_GEAR_2"] = "Gear 2",
        ["ACTION_RACE_GEAR_3"] = "Gear 3",
        ["ACTION_RACE_GEAR_4"] = "Gear 4",
        ["ACTION_RACE_GEAR_5"] = "Gear 5",
        ["ACTION_RACE_GEAR_6"] = "Gear 6",
        ["ACTION_RACE_GEAR_7"] = "Gear 7",
        ["ACTION_RACE_GEAR_R"] = "Reverse gear",
        ["ACTION_RACE_TOGGLE_AUTOPILOT"] = "Toggle autopilot",
        ["ACTION_RACE_HORN"] = "Horn",
        ["ACTION_RACE_TOGGLE_GHOST_VIEW"] = "Toggle ghost view",
        ["ACTION_RACE_AIR_CONTROL_UP_DOWN"] = "Air control",
        ["ACTION_RACE_SET_COMMANDED_RESPAWN"] = "Set respawn point",
        ["ACTION_RACE_COMMANDED_RESPAWN"] = "Respawn at point",
        ["ACTION_RACE_TOGGLE_WIPERS"] = "Wipers",
        ["ACTION_RACE_TOGGLE_HEADLIGHTS"] = "Headlights",
        ["ACTION_RACE_CLUTCH"] = "Clutch",
        ["FO_ACTION_START_RACE"] = "Start race",
        ["FO_DEBUG_TOGGLE_HUD"] = "Toggle HUD (debug)",
        ["FO_ACTION_NEXT_SPECTATE_CAM"] = "Next spectator camera",
        ["FO_ACTION_PREV_SPECTATE_CAM"] = "Previous spectator camera",
        ["FO_ACTION_STUNT_EJECT"] = "Stunt eject",
        ["FO_ACTION_STUNT_EJECT_JUST"] = "Stunt eject (tap)",
        ["FO_ACTION_TRAP_NEXT"] = "Next trap",
        ["FO_ACTION_TRAP_PREV"] = "Previous trap",
        ["FO_ACTION_TRAP_CREATE"] = "Place trap ahead",
        ["FO_ACTION_TRAP_CREATE_BEHIND"] = "Place trap behind",
        ["FO_ACTION_TRAP_LAUNCH_A"] = "Launch trap A",
        ["FO_ACTION_TRAP_LAUNCH_B"] = "Launch trap B",
        ["FO_ACTION_TRAP_LAUNCH_C"] = "Launch trap C",
        ["FO_ACTION_TRAP_LAUNCH_D"] = "Launch trap D",
        ["FO_ACTION_TRAP_LAUNCH_UNSELECTED_A"] = "Launch other trap A",
        ["FO_ACTION_TRAP_LAUNCH_UNSELECTED_B"] = "Launch other trap B",
        ["FO_ACTION_TRAP_LAUNCH_UNSELECTED_C"] = "Launch other trap C",
        ["FO_ACTION_TRAP_LAUNCH_UNSELECTED_D"] = "Launch other trap D",
        ["FO_ACTION_TRAP_SELECT_LAUNCH"] = "Select and launch trap",
        ["FO_ACTION_HUD_SWITCH_PLAYERLIST"] = "Player list",
        ["FO_ACTION_ROTATE_CAMERA"] = "Rotate camera",
        ["FO_DEBUG_SWITCH_DAYTIME_UP"] = "Time of day + (debug)",
        ["FO_DEBUG_SWITCH_DAYTIME_DOWN"] = "Time of day - (debug)",
        ["FO_ACTION_SPECTATE_GAMERTAG"] = "Spectator name tags",
        ["FO_ACTION_SKIP_MUSIC"] = "Skip music track",
        ["FO_ACTION_VR_HMD_RECENTER"] = "Recenter VR view"
    };

    private static readonly string[] MenuActionTitles =
    [
        "Confirm",
        "Back",
        "Secondary action",
        "Next page",
        "Previous page",
        "Pause menu",
        "Up",
        "Down",
        "Left",
        "Right",
        "Recenter view"
    ];

    private static readonly string[] DeviceSettingTitles =
    [
        "Accelerator sensitivity",
        "Accelerator deadzone",
        "Brake sensitivity",
        "Brake deadzone",
        "Steering sensitivity",
        "Steering deadzone",
        "Counter-steer sensitivity",
        "Vibration enabled",
        "Vibration level",
        "Force-feedback level",
        "Force-feedback vibration"
    ];

    private PageViewModel BuildControlsPage()
    {
        TableColumn[] deviceColumns =
        [
            new TableColumn("Keyboard", 180),
            new TableColumn("Gamepad", 180),
            new TableColumn("Steering wheel", 200)
        ];

        TableBlock race = Table(
            "Race bindings",
            "Key, button or axis per device. \"Default\" lets the game use its built-in binding.",
            "Action",
            260,
            deviceColumns);
        foreach (string action in FlatOut4SaveSchema.InputActionNames)
        {
            race.AddRow(
                ActionTitles.GetValueOrDefault(action, action),
                FlatOut4SaveSchema.BindingDeviceNames.Select(device => (SaveFieldViewModel?)F($"m_oGameOptions.m_uUserBindings[{device}][{action}]")).ToArray(),
                subtitle: action);
        }

        race.Actions = [ResetBindingsAction(race, "race")];

        TableBlock menu = Table(
            "Menu bindings",
            "\"Cleared\" (0xFFFFFFFE) means the player removed the binding on purpose.",
            "Action",
            260,
            deviceColumns);
        for (int i = 0; i < FlatOut4SaveSchema.MenuBindingNames.Length; i++)
        {
            string action = FlatOut4SaveSchema.MenuBindingNames[i];
            menu.AddRow(
                MenuActionTitles[i],
                FlatOut4SaveSchema.BindingDeviceNames.Select(device => (SaveFieldViewModel?)F($"m_oGameOptions.m_uMenuBindings[{device}][{action}]")).ToArray(),
                subtitle: action);
        }

        menu.Actions = [ResetBindingsAction(menu, "menu")];

        TableBlock tuning = Table(
            "Device tuning",
            "Per-device response curves from the Controls options. Unused entries hold NaN.",
            "Setting",
            260,
            new TableColumn("Keyboard", NumberWidth + 22),
            new TableColumn("Gamepad", NumberWidth + 22),
            new TableColumn("Steering wheel", NumberWidth + 22));
        for (int i = 0; i < FlatOut4SaveSchema.ControllerSettingNames.Length; i++)
        {
            string setting = FlatOut4SaveSchema.ControllerSettingNames[i];
            tuning.AddRow(
                DeviceSettingTitles[i],
                FlatOut4SaveSchema.BindingDeviceNames.Select(device => (SaveFieldViewModel?)F($"m_oGameOptions.m_fDeviceSettings[{device}][{setting}]")).ToArray());
        }

        return MakePage("controls", [race, menu, tuning]);
    }

    private BlockAction ResetBindingsAction(TableBlock table, string noun) =>
        new("Reset to defaults", "\uE777", new RelayCommand(() => session.Batch(() =>
        {
            foreach (TableRowViewModel row in table.AllRows)
            {
                foreach (TableCell cell in row.Cells)
                {
                    cell.Field?.WriteInteger(0xFFFFFFFF);
                }
            }
        }, $"Reset {noun} bindings")), $"Set every {noun} binding back to the game's built-in default");

    private PageViewModel BuildExtensionsPage()
    {
        var values = Group(
            "Tagged values",
            "Profile values added after the V95 layout was frozen. They live in m_aOther so the live Steam build keeps them when it rewrites the save.",
            "m_aOther.TelemetryOutput",
            "m_aOther[0]",
            "m_aOther[1]",
            "m_aOther[2]");
        values.Actions =
        [
            new BlockAction("Clear debug fill", "\uE75C", new RelayCommand(ClearLeftoverSlots), "Zero the m_aOther slots that hold uninitialised memory (0xCDCDCDCD and similar) from old debug builds. Any other value is kept: a newer build may have stored it."),
            new BlockAction("Clear unknown slots", "\uE74D", new RelayCommand(ClearUnknownSlots), "Zero m_aOther slots 3-1023. They usually hold leftover memory, but a newer build stores its new settings there: skip this if one has used this profile.")
        ];

        var blocks = new List<PageBlock> { extensionNote, values };
        return MakePage("extensions", blocks);
    }

    private void ClearLeftoverSlots()
    {
        session.Batch(() =>
        {
            for (int slot = 0; slot < FlatOut4SaveSchema.OtherSlotCount; slot++)
            {
                int offset = session.Schema.ExtensionSlotOffset(slot);
                if (FlatOut4SaveHealth.IsFillPattern(session.ReadUInt32(offset)))
                {
                    session.WriteUInt32(offset, 0);
                }
            }
        }, "Clear debug fill");
    }

    private void ClearUnknownSlots()
    {
        session.Batch(() =>
        {
            for (int slot = FlatOut4SaveSchema.ExtensionSlotTelemetry + 1; slot < FlatOut4SaveSchema.OtherSlotCount; slot++)
            {
                session.WriteUInt32(session.Schema.ExtensionSlotOffset(slot), 0);
            }
        }, "Clear unknown slots");
    }

    private void RefreshExtensionNote()
    {
        FlatOut4SaveSchema schema = session.Schema;
        bool tagged = session.ReadUInt32(schema.ExtensionSlotOffset(FlatOut4SaveSchema.ExtensionSlotMagic)) == FlatOut4SaveSchema.ExtensionMagicValue;
        int leftovers = 0;
        for (int slot = FlatOut4SaveSchema.ExtensionSlotTelemetry + 1; slot < FlatOut4SaveSchema.OtherSlotCount; slot++)
        {
            if (session.ReadUInt32(schema.ExtensionSlotOffset(slot)) != 0)
            {
                leftovers++;
            }
        }

        // Same stance as Save health: values no build reads yet are usually leftover memory, but a
        // newer build may have stored settings there, so they are kept unless the user clears them.
        string leftoverText = leftovers == 0
            ? "The unused slots (3-1023) are clean."
            : $"{leftovers:N0} unused slots (3-1023) hold values no build reads yet, usually leftover memory. They are saved back unchanged; Clear unknown slots zeroes them if no newer build has used this profile.";
        extensionNote.Message = tagged
            ? $"Tagged (FXT1): the game trusts the values below. {leftoverText}"
            : $"Untagged: the game ignores the stored telemetry value and treats telemetry as ON. Changing the telemetry toggle tags the profile. {leftoverText}";
        extensionNote.Severity = tagged && leftovers == 0 ? "Success" : "Informational";
    }
}
