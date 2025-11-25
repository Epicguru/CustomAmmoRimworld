using JetBrains.Annotations;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CustomLoads.UI;

[UsedImplicitly]
public class CompProperties_OpenEditorGizmo : CompProperties
{
    public CompProperties_OpenEditorGizmo()
    {
        compClass = typeof(Comp_OpenEditorGizmo);
    }
}

public class Comp_OpenEditorGizmo : ThingComp
{
    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (var gizmo in base.CompGetGizmosExtra())
            yield return gizmo;

        yield return MakeMenuGizmo();
    }

    private Gizmo MakeMenuGizmo()
    {
        return new Command_Action
        {
            defaultLabel = "Create/Edit Custom Ammo",
            defaultDesc = "Create, edit or view custom ammo.",
            alsoClickIfOtherInGroupClicked = false,
            icon = Window_CustomLoadEditor.BulletTexture,
            defaultIconColor = Color.cyan,
            action = OnClicked
        };
    }

    private static void OnClicked()
    {
        Window_CustomLoadEditor.Open();
    }
}