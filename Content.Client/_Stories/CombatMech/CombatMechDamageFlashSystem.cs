using Content.Shared._Stories.CombatMech;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._Stories.CombatMech;

// The body overlay sprite must mirror the mech body color every frame. RMC's damage flash tweens the
// mech sprite color from red back to white over multiple frames; an event-driven sync only catches the
// initial hit and leaves the overlay stuck at the flashed color until the next damage event. There is
// no public hook into the tween, so per-frame mirroring is the only reliable option. Mech count per
// round is bounded (event/admin spawn), so the per-frame cost is negligible.
public sealed class CombatMechDamageFlashSystem : EntitySystem
{
    private EntityQuery<SpriteComponent> _spriteQuery;

    public override void Initialize()
    {
        base.Initialize();
        _spriteQuery = GetEntityQuery<SpriteComponent>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<CombatMechComponent, SpriteComponent>();
        while (query.MoveNext(out _, out var mech, out var mechSprite))
        {
            if (mech.BodyOverlayEntity is not { } overlay || Deleted(overlay))
                continue;

            if (_spriteQuery.TryComp(overlay, out var overlaySprite) &&
                overlaySprite.Color != mechSprite.Color)
            {
                overlaySprite.Color = mechSprite.Color;
            }
        }
    }
}
