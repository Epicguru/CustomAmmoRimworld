using CustomLoads.Bullet;
using System.Collections.Generic;
using Verse;

namespace CustomLoads;

public class GunpowderEffectsDef : Def
{
    public List<Stage> stages = new List<Stage>();
    public ThingDef material;
    public float costPerBullet = 0.1f;

    public BulletPartMod TryGetMod(int powder)
    {
        foreach (var stage in stages)
        {
            if (stage.powder == powder)
                return stage.effects;
        }
        return null;
    }

    public class Stage
    {
        public int powder;
        public BulletPartMod effects = new BulletPartMod();
    }
}