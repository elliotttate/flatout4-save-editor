using FlatOut4SaveEditor.Models;

namespace FlatOut4SaveEditor.ViewModels;

// Reads and writes career medals through the 150 FOCareerEventReward slots the way
// FOCareerDataStruct does: one slot per event id, first empty slot (id 0) for new ones.
public sealed class CareerProgress
{
    public const int SlotCount = 150;
    private const int ScoreNone = 3; // M_COUNT

    private readonly SaveSession session;
    private readonly int[] idOffsets = new int[SlotCount];
    private readonly int[] scoreOffsets = new int[SlotCount];
    private readonly int[] newOffsets = new int[SlotCount];
    private readonly int[] poppedOffsets = new int[SlotCount];

    public CareerProgress(SaveSession session)
    {
        this.session = session;
        for (int i = 0; i < SlotCount; i++)
        {
            string path = $"m_oCareerData.m_vEventRewards[{i}]";
            idOffsets[i] = session.Field($"{path}.m_oId").Definition.Offset;
            scoreOffsets[i] = session.Field($"{path}.m_oScore").Definition.Offset;
            newOffsets[i] = session.Field($"{path}.m_bNew").Definition.Offset;
            poppedOffsets[i] = session.Field($"{path}.m_bPoped").Definition.Offset;
        }
    }

    public int FindSlot(uint id)
    {
        if (id == 0)
        {
            return -1;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (session.ReadUInt32(idOffsets[i]) == id)
            {
                return i;
            }
        }

        return -1;
    }

    // 0 = not completed, 1 = bronze, 2 = silver, 3 = gold.
    public int GetMedalIndex(uint id)
    {
        int slot = FindSlot(id);
        if (slot < 0)
        {
            return 0;
        }

        int score = session.ReadInt32(scoreOffsets[slot]);
        return score is >= 0 and < ScoreNone ? score + 1 : 0;
    }

    // The medal and its reward slot change together, so they undo together.
    public void SetMedalIndex(uint id, int medalIndex, string? label = null) =>
        session.Group(() => ApplyMedal(id, medalIndex), label ?? "Medal");

    private void ApplyMedal(uint id, int medalIndex)
    {
        int slot = FindSlot(id);
        if (medalIndex <= 0)
        {
            // Keep the slot (the game records events it has shown) but clear the medal (M_COUNT).
            if (slot >= 0)
            {
                session.WriteInt32(scoreOffsets[slot], ScoreNone);
            }

            return;
        }

        bool isNewSlot = false;
        if (slot < 0)
        {
            slot = FirstEmptySlot();
            if (slot < 0)
            {
                return;
            }

            isNewSlot = true;
        }

        session.WriteUInt32(idOffsets[slot], id);
        session.WriteInt32(scoreOffsets[slot], medalIndex - 1);
        if (isNewSlot)
        {
            // Same flags the debug Unlock All leaves: marked new, reward popup already shown.
            session.WriteBytes(newOffsets[slot], [1]);
            session.WriteBytes(poppedOffsets[slot], [1]);
        }
    }

    public bool GetFlag(uint id, bool popped)
    {
        int slot = FindSlot(id);
        return slot >= 0 && session.ReadByte(popped ? poppedOffsets[slot] : newOffsets[slot]) != 0;
    }

    public void SetFlag(uint id, bool popped, bool value)
    {
        int slot = FindSlot(id);
        if (slot >= 0)
        {
            session.WriteBytes(popped ? poppedOffsets[slot] : newOffsets[slot], [(byte)(value ? 1 : 0)]);
        }
    }

    private int FirstEmptySlot()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (session.ReadUInt32(idOffsets[i]) == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
