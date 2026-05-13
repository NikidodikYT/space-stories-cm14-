using Content.Shared._Stories.CombatMech;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._Stories.CombatMech;

public sealed class CombatMechDamageFlashSystem : EntitySystem
{
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<CombatMechComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var mech, out var mechSprite))
        {
            if (mech.BodyOverlayEntity is not { } overlay || Deleted(overlay))
                continue;

            if (TryComp<SpriteComponent>(overlay, out var overlaySprite) &&
                overlaySprite.Color != mechSprite.Color)
            {
                overlaySprite.Color = mechSprite.Color;
            }
        }
    }
}
