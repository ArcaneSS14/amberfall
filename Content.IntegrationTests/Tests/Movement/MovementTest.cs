// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Movement;

/// <summary>
/// Shared setup for tests that need a simple walkable row of tiles.
/// </summary>
public abstract class MovementTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";
    protected static readonly EntProtoId WallPrototype = "WallSolid";

    protected virtual int Tiles => 3;
    protected virtual bool AddWalls => true;

    protected NetEntity? WallLeft;
    protected NetEntity? WallRight;

    [SetUp]
    public override async Task Setup()
    {
        await base.Setup();
        var playerCoordinates = SEntMan.GetCoordinates(PlayerCoords);

        for (var i = -Tiles; i <= Tiles; i++)
        {
            var coordinates = playerCoordinates.Offset(new Vector2(i, 0));
            await SetTile(Plating, SEntMan.GetNetCoordinates(coordinates), MapData.Grid);
        }

        AssertGridCount(1);

        if (!AddWalls)
            return;

        var left = await SpawnEntity(WallPrototype, playerCoordinates.Offset(new Vector2(-Tiles, 0)));
        var right = await SpawnEntity(WallPrototype, playerCoordinates.Offset(new Vector2(Tiles, 0)));
        WallLeft = SEntMan.GetNetEntity(left);
        WallRight = SEntMan.GetNetEntity(right);
    }

    protected float Delta(NetEntity? target = null, NetEntity? other = null)
    {
        target ??= Target;
        if (target == null)
        {
            Assert.Fail("No target specified");
            return 0;
        }

        var targetPosition = Transform.GetWorldPosition(SEntMan.GetEntity(target.Value));
        var otherPosition = Transform.GetWorldPosition(SEntMan.GetEntity(other ?? Player));
        return (targetPosition - otherPosition).X;
    }

    protected float DeltaCoordinates(NetCoordinates? coordinates = null, NetEntity? other = null)
    {
        coordinates ??= TargetCoords;
        other ??= Player;

        var targetPosition = Transform.ToWorldPosition(ToServer(coordinates.Value));
        var otherPosition = Transform.GetWorldPosition(ToServer(other.Value));
        return (targetPosition - otherPosition).X;
    }
}
