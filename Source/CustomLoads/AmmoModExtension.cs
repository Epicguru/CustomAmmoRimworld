using CustomLoads.Bullet;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace CustomLoads
{
    public class AmmoModExtension : DefModExtension
    {
        public Dictionary<StatDef, List<StatMod>> statMods = new Dictionary<StatDef, List<StatMod>>();
    }

    public class StatMod
    {
        public StatDef Stat => DefDatabase<StatDef>.GetNamed(Mod.StatDefName);

        public BulletPart BulletPart;
        public BulletMaterialDef Material;
        public BulletPartMod.ModData Mod;
    }
}
