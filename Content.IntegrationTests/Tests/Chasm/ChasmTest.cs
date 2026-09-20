using Content.IntegrationTests.Tests.Movement;
using Content.Shared.Chasm;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Chasm;

/// <summary>
/// A test for chasms, which delete entities when a player walks over them.
/// </summary>
[TestOf(typeof(ChasmComponent))]
public sealed class ChasmTest : MovementTest
{
    private readonly EntProtoId _chasmProto = "FloorChasmEntity";
    private readonly EntProtoId _catWalkProto = "Catwalk";

    /// <summary>
    /// Test that a player falls into the chasm when walking over it.
    /// </summary>
    [Test]
    public async Task ChasmFallTest()
    {
        // Spawn a chasm.
        await SpawnTarget(_chasmProto);
        Assert.That(Delta(), Is.GreaterThan(0.5), "Player did not spawn left of the chasm.");

        // Attempt (and fail) to walk past the chasm.
        // If you are modifying the default value of ChasmFallingComponent.DeletionTime this time might need to be adjusted.
        await Move(DirectionFlag.East, 0.5f);

        // We should be falling right now.
        Assert.That(TryComp<ChasmFallingComponent>(Player, out var falling), "Player is not falling after walking over a chasm.");

        var fallTime = (float)falling.DeletionTime.TotalSeconds;

        // Wait until we get deleted.
        await Pair.RunSeconds(fallTime);

        // Check that the player was deleted.
        AssertDeleted(Player);
    }

    /// <summary>
    /// Test that a catwalk placed over a chasm will protect a player from falling.
    /// </summary>
    [Test]
    public async Task ChasmCatwalkTest()
    {
        // Spawn a chasm.
        await SpawnTarget(_chasmProto);
        Assert.That(Delta(), Is.GreaterThan(0.5), "Player did not spawn left of the chasm.");

        // Spawn a catwalk over the chasm.
        var catwalk = await Spawn(_catWalkProto);

        // Attempt to walk past the chasm.
        await Move(DirectionFlag.East, 1f);

        // We should be on the other side.
        Assert.That(Delta(), Is.LessThan(-0.5), "Player was unable to walk over a chasm with a catwalk.");

        // Check that the player is not deleted.
        AssertExists(Player);

        // Make sure the player is not falling right now.
        Assert.That(HasComp<ChasmFallingComponent>(Player), Is.False, "Player has ChasmFallingComponent after walking over a catwalk.");

        // Delete the catwalk.
        await Delete(catwalk);

        // Attempt (and fail) to walk past the chasm.
        await Move(DirectionFlag.West, 1f);

        // Wait until we get deleted.
        await Pair.RunSeconds(5f);

        // Check that the player was deleted
        AssertDeleted(Player);
    }

}
