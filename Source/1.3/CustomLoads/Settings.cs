using CustomLoads.Bullet;
using CustomLoads.UI;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CustomLoads;

public class Settings : ModSettings
{
    public List<CustomLoad> CustomAmmo = new();

    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_Collections.Look(ref CustomAmmo, "CustomAmmo", LookMode.Deep);
        CustomAmmo ??= new List<CustomLoad>();
    }

    public void Draw(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        if (listing.ButtonText("Edit custom ammo"))
        {
            Window_CustomLoadEditor.Open();
        }

        listing.End();
    }
}