using FlatOut4SaveEditor.Models;

namespace FlatOut4SaveEditor.Services;

public sealed record FlatOut4UnlockAllResult(
    int CareerEventsCompleted,
    int GarageEntriesUnlocked,
    int ChallengeValuesMaxed,
    int TotalChangedValues)
{
    public bool Changed => TotalChangedValues > 0;
}

public static class FlatOut4SaveUnlocker
{
    private const int GarageStateNew = 1;
    private const int GarageStateOwned = 3;
    private const int GarageStateDlcLocked = 4;
    private const int GarageStateDlcOwned = 5;
    private const int GarageStateCount = 6;
    private const int ScoreObjectiveGold = 2;
    private const uint CarTypeNone = 39;

    private static readonly string[] CurrentCareerEventNames =
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

    private static readonly uint[] CurrentCareerEventIds = CurrentCareerEventNames
        .Select(FnvHashUpperInvariant)
        .ToArray();

    public static FlatOut4UnlockAllResult ApplyDebugUnlockAll(FlatOut4SaveDocument document, FlatOut4SaveSchema schema)
    {
        var editor = new SaveBytesEditor(document.Bytes, schema);

        int changed = 0;
        int careerEventsCompleted = CompleteCareerEvents(editor, ref changed);
        int garageEntriesUnlocked = UnlockGarage(editor, ref changed);
        int challengeValuesMaxed = MaxChallengeMode(editor, ref changed);

        return new FlatOut4UnlockAllResult(
            careerEventsCompleted,
            garageEntriesUnlocked,
            challengeValuesMaxed,
            changed);
    }

    private static int CompleteCareerEvents(SaveBytesEditor editor, ref int changed)
    {
        var eventIds = new HashSet<uint>(CurrentCareerEventIds);
        for (int reward = 0; reward < 150; reward++)
        {
            uint existingId = editor.ReadUInt32(EventRewardField(reward, "m_oId"));
            if (existingId != 0)
            {
                eventIds.Add(existingId);
            }
        }

        int completed = 0;
        foreach (uint eventId in eventIds)
        {
            if (eventId == 0 || !TryFindOrAddEventRewardSlot(editor, eventId, out int rewardIndex, out bool isNewSlot))
            {
                continue;
            }

            changed += editor.WriteUInt32(EventRewardField(rewardIndex, "m_oId"), eventId);
            changed += editor.WriteInt32(EventRewardField(rewardIndex, "m_oScore"), ScoreObjectiveGold);
            if (isNewSlot)
            {
                changed += editor.WriteBool8(EventRewardField(rewardIndex, "m_bNew"), true);
            }

            changed += editor.WriteBool8(EventRewardField(rewardIndex, "m_bPoped"), true);
            completed++;
        }

        return completed;
    }

    private static bool TryFindOrAddEventRewardSlot(SaveBytesEditor editor, uint eventId, out int rewardIndex, out bool isNewSlot)
    {
        int firstEmpty = -1;
        for (int reward = 0; reward < 150; reward++)
        {
            uint currentId = editor.ReadUInt32(EventRewardField(reward, "m_oId"));
            if (currentId == eventId)
            {
                rewardIndex = reward;
                isNewSlot = false;
                return true;
            }

            if (currentId == 0 && firstEmpty < 0)
            {
                firstEmpty = reward;
            }
        }

        rewardIndex = firstEmpty;
        isNewSlot = firstEmpty >= 0;
        return isNewSlot;
    }

    private static int UnlockGarage(SaveBytesEditor editor, ref int changed)
    {
        int unlocked = 0;

        for (int car = 0; car < 64; car++)
        {
            string prefix = $"m_oGarageData.m_vCarsState[{car}].m_oUnlockableItem";
            uint carIndex = editor.ReadUInt32($"{prefix}.m_iIndex");
            if (carIndex != CarTypeNone)
            {
                unlocked += UnlockGarageState(editor, $"{prefix}.m_eState", ref changed);
            }
        }

        unlocked += UnlockIndexedItems(editor, "m_oGarageData.m_vCarSkinsState", 512, ref changed);
        unlocked += UnlockIndexedItems(editor, "m_oGarageData.m_vDriversState", 64, ref changed);
        unlocked += UnlockIndexedItems(editor, "m_oGarageData.m_vBoostFxState", 64, ref changed);
        unlocked += UnlockIndexedItems(editor, "m_oGarageData.m_vCarHornState", 64, ref changed);

        for (int track = 0; track < 64; track++)
        {
            string prefix = $"m_oGarageData.m_vTrackState[{track}]";
            if (editor.ReadInt32($"{prefix}.m_iTrackLocaDiff") != -1)
            {
                unlocked += UnlockGarageState(editor, $"{prefix}.m_eState", ref changed);
            }
        }

        return unlocked;
    }

    private static int UnlockIndexedItems(SaveBytesEditor editor, string prefix, int count, ref int changed)
    {
        int unlocked = 0;
        for (int item = 0; item < count; item++)
        {
            string itemPrefix = $"{prefix}[{item}]";
            if (editor.ReadUInt32($"{itemPrefix}.m_iIndex") != 0)
            {
                unlocked += UnlockGarageState(editor, $"{itemPrefix}.m_eState", ref changed);
            }
        }

        return unlocked;
    }

    private static int UnlockGarageState(SaveBytesEditor editor, string fieldName, ref int changed)
    {
        int currentState = editor.ReadInt32(fieldName);
        if (currentState is GarageStateOwned or GarageStateDlcLocked or GarageStateDlcOwned or GarageStateCount)
        {
            return 0;
        }

        changed += editor.WriteInt32(fieldName, GarageStateNew);
        return 1;
    }

    private static int MaxChallengeMode(SaveBytesEditor editor, ref int changed)
    {
        int maxed = 0;
        maxed += editor.WriteInt32("m_oChallengeModeData.m_iPlayerScore", int.MaxValue);

        for (int challenge = 0; challenge < 128; challenge++)
        {
            maxed += editor.WriteInt32($"m_oChallengeModeData.m_oChallengesRecords[{challenge}]", int.MaxValue);
        }

        changed += maxed;
        return maxed;
    }

    private static string EventRewardField(int reward, string member) =>
        $"m_oCareerData.m_vEventRewards[{reward}].{member}";

    private static uint FnvHashUpperInvariant(string value)
    {
        uint hash = 0;
        foreach (char c in value.ToUpperInvariant())
        {
            hash += (hash << 1) + (hash << 4) + (hash << 7) + (hash << 8) + (hash << 24);
            hash ^= (uint)c;
        }

        return hash;
    }

    private sealed class SaveBytesEditor
    {
        private readonly byte[] bytes;
        private readonly IReadOnlyDictionary<string, SaveFieldDefinition> fields;

        public SaveBytesEditor(byte[] bytes, FlatOut4SaveSchema schema)
        {
            this.bytes = bytes;
            fields = schema.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        }

        public int ReadInt32(string name) => BitConverter.ToInt32(bytes, GetField(name).Offset);

        public uint ReadUInt32(string name) => BitConverter.ToUInt32(bytes, GetField(name).Offset);

        public int WriteInt32(string name, int value)
        {
            SaveFieldDefinition field = GetField(name);
            int currentValue = BitConverter.ToInt32(bytes, field.Offset);
            if (currentValue == value)
            {
                return 0;
            }

            WriteBytes(field, BitConverter.GetBytes(value));
            return 1;
        }

        public int WriteUInt32(string name, uint value)
        {
            SaveFieldDefinition field = GetField(name);
            uint currentValue = BitConverter.ToUInt32(bytes, field.Offset);
            if (currentValue == value)
            {
                return 0;
            }

            WriteBytes(field, BitConverter.GetBytes(value));
            return 1;
        }

        public int WriteBool8(string name, bool value)
        {
            SaveFieldDefinition field = GetField(name);
            byte byteValue = value ? (byte)1 : (byte)0;
            if (bytes[field.Offset] == byteValue)
            {
                return 0;
            }

            bytes[field.Offset] = byteValue;
            return 1;
        }

        private SaveFieldDefinition GetField(string name)
        {
            if (!fields.TryGetValue(name, out SaveFieldDefinition? field))
            {
                throw new InvalidOperationException($"The save schema does not contain field {name}.");
            }

            return field;
        }

        private void WriteBytes(SaveFieldDefinition field, byte[] value)
        {
            Array.Copy(value, 0, bytes, field.Offset, field.Length);
        }
    }
}
