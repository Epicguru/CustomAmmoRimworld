using HarmonyLib;
using Verse;

namespace CustomLoads.Patches;

/// <summary>
/// When <see cref="Current.Game"/> is null,
/// Thing creation fails because the ID generator depends on the current Game.
/// But I want to create Things in the menu, so I bypass the generator when necessary.
/// </summary>
[HarmonyPatch(typeof(ThingIDMaker), nameof(ThingIDMaker.GiveIDTo))]
public class Patch_ThingIDMaker_GiveIDTo
{
    public static bool Active;

    public static bool Prefix(Thing t)
    {
        if (!Active)
            return true;

        t.thingIDNumber = Rand.Range(int.MinValue, int.MaxValue);
        return false;
    }
}