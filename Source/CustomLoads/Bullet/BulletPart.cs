using System;

namespace CustomLoads.Bullet
{
    [Flags]
    public enum BulletPart
    {
        BulletCore = 1 << 0,
        BulletJacket = 1 << 1,
        BulletTip = 1 << 2,
        Casing = 1 << 3,
        Primer = 1 << 4,
        Powder = 1 << 5,
    }
}
