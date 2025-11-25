using System;

namespace CustomLoads.Bullet;

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

public static class BulletPartExtensions
{
    public static string Label(this BulletPart part) => part switch
    {
        BulletPart.BulletCore => "core",
        BulletPart.BulletJacket => "jacket",
        BulletPart.BulletTip => "tip",
        BulletPart.Casing => "casing",
        BulletPart.Primer => "primer",
        BulletPart.Powder => "powder",
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null)
    };
}