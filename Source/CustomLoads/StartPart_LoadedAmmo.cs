using CombatExtended;
using CustomLoads.Bullet;
using JetBrains.Annotations;
using RimWorld;
using System;
using Verse;

namespace CustomLoads;

[UsedImplicitly]
public class StartPart_LoadedAmmo : StatPart
{
    private AmmoModExtension TryGetStatExtForRequest(in StatRequest req)
    {
        if (!req.HasThing)
            return null;

        var ammoUser = req.Thing.TryGetComp<CompAmmoUser>();
        return ammoUser?.CurrentAmmo?.GetModExtension<AmmoModExtension>();
    }

    public override void TransformValue(StatRequest req, ref float val)
    {
        var ext = TryGetStatExtForRequest(req);
        if (ext == null)
            return;

        if (!ext.statMods.TryGetValue(parentStat, out var found))
            return;

        float factor = 1f;
        float offset = 0f;
        foreach (var mod in found)
        {
            factor += mod.Mod.Coefficient - 1f;
            offset += mod.Mod.Offset;
        }

        val *= factor;
        val += offset;
    }

    public override string ExplanationPart(StatRequest req)
    {
        var ext = TryGetStatExtForRequest(req);
        if (ext == null)
            return null;

        string txt = "CE_StatsReport_LoadedAmmo".Translate() + ":\n";
        bool any = false;

        if (ext.statMods.TryGetValue(parentStat, out var found))
        {
            foreach (var item in found)
            {
                string part = item.BulletPart switch
                {
                    BulletPart.BulletCore => "core",
                    BulletPart.BulletJacket => "jacket",
                    BulletPart.BulletTip => "tip",
                    BulletPart.Casing => "cartridge casing",
                    BulletPart.Primer => "primer",
                    BulletPart.Powder => "propellant",
                    _ => throw new ArgumentOutOfRangeException()
                };
                string label = $"{item.Material.TechLabel.CapitalizeFirst()} {part}";

                any = true;
                if (parentStat == CE_StatDefOf.TicksBetweenBurstShots)
                {
                    // Lower values increase rate of fire.
                    if (item.Mod.Coefficient != 1f)
                    {
                        float div = 1f / item.Mod.Coefficient;
                        float multi = div - 1f;

                        txt += $"  - {label}: ";
                        if (multi > 0f)
                            txt += '+';
                        txt += $"{multi:P0} (raw {item.Mod.Coefficient})\n";
                    }

                    if (item.Mod.Offset != 0f)
                        txt += $"  - {label}: {(item.Mod.Offset > 0f ? "+" : "")}{item.Mod.Offset:0.##}\n";

                }
                else
                {
                    if (item.Mod.Coefficient != 1f)
                        txt += $"  - {label}: {(item.Mod.Coefficient>1f?"+":"")}{item.Mod.Coefficient-1f:P0}\n";

                    if (item.Mod.Offset != 0f)
                        txt += $"  - {label}: {(item.Mod.Offset > 0f ? "+" : "")}{item.Mod.Offset:0.##}\n";
                }
            }
        }

        return any ? txt : null;
    }
}