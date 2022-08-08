using CombatExtended;
using RimWorld;
using System.Linq;
using Verse;

namespace CustomLoads
{
    public class StartPart_LoadedAmmo : StatPart
    {
        private AmmoModExtension TryGetStatExtForRequest(in StatRequest req)
        {
            if (!req.HasThing)
                return null;

            var ammoUser = req.Thing.TryGetComp<CompAmmoUser>();
            if (ammoUser?.CurrentAmmo == null)
                return null;

            var ext = ammoUser.CurrentAmmo.GetModExtension<AmmoModExtension>();
            return ext;
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            var ext = TryGetStatExtForRequest(req);
            if (ext == null)
                return;

            if (ext.factors.TryGetValue(parentStat, out var found))
                val *= 1f + found.Sum(m => m.Mod.value - 1f);
            
            if (ext.offsets.TryGetValue(parentStat, out found))
                val += found.Sum(m => m.Mod.value);
        }

        public override string ExplanationPart(StatRequest req)
        {
            var ext = TryGetStatExtForRequest(req);
            if (ext == null)
                return null;

            string txt = "CE_StatsReport_LoadedAmmo".Translate() + ":\n";

            if (ext.factors.TryGetValue(parentStat, out var found))
            {
                foreach (var item in found)
                {
                    float factor = item.Mod.value - 1f;
                    txt += $"  - {item.Material.TechLabel} {item.BulletPart}: {item.Mod.ToStringAsFactor}\n";
                }
            }

            if (ext.offsets.TryGetValue(parentStat, out found))
            {
                foreach (var item in found)
                {
                    txt += $"  - {item.Material.TechLabel} {item.BulletPart}: {item.Mod.ValueToStringAsOffset}\n";
                }
            }

            return txt;
        }
    }
}
