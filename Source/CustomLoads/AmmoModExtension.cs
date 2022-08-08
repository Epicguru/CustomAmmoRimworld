using CustomLoads.Bullet;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace CustomLoads
{
    public class AmmoModExtension : DefModExtension
    {
        public Dictionary<StatDef, List<StatMod>> factors = new Dictionary<StatDef, List<StatMod>>();
        public Dictionary<StatDef, List<StatMod>> offsets = new Dictionary<StatDef, List<StatMod>>();
    }

    public class StatMod
    {
        public StatDef Stat => Mod.stat;

        public BulletPart BulletPart;
        public BulletMaterialDef Material;
        public StatModifier Mod;
    }
}
