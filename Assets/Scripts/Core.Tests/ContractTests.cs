#nullable enable

using Armada.Core;
using NUnit.Framework;

namespace Armada.Core.Tests
{
    /// <summary>
    /// Pins the wire-facing parts of the contract frozen at M0. These are not interesting tests to
    /// read; they are the ones that fail loudly when someone renumbers an enum or edits an error
    /// string, both of which are breaking protocol changes.
    /// </summary>
    [TestFixture]
    public sealed class ContractTests
    {
        [Test]
        public void ProtocolVersion_IsOne()
        {
            Assert.That(GameProtocol.Version, Is.EqualTo(1));
            Assert.That(GameProtocol.IsCompatible(1), Is.True);
            Assert.That(GameProtocol.IsCompatible(2), Is.False);
            Assert.That(GameProtocol.IsCompatible(0), Is.False);
        }

        // Enum values travel on the wire, so their numbers are part of the contract.
        [Test]
        public void ShipClass_NumbersAreFrozen()
        {
            Assert.That((byte)ShipClass.Carrier, Is.EqualTo(0));
            Assert.That((byte)ShipClass.Battleship, Is.EqualTo(1));
            Assert.That((byte)ShipClass.Cruiser, Is.EqualTo(2));
            Assert.That((byte)ShipClass.Submarine, Is.EqualTo(3));
            Assert.That((byte)ShipClass.Destroyer, Is.EqualTo(4));
            Assert.That((byte)ShipClass.PatrolBoat, Is.EqualTo(5));
        }

        [Test]
        public void ShotOutcome_NumbersAreFrozen()
        {
            Assert.That((byte)ShotOutcome.Miss, Is.EqualTo(0));
            Assert.That((byte)ShotOutcome.Hit, Is.EqualTo(1));
            Assert.That((byte)ShotOutcome.Sunk, Is.EqualTo(2));
            Assert.That((byte)ShotOutcome.Win, Is.EqualTo(3));
        }

        [Test]
        public void MatchPhase_NumbersAreFrozen()
        {
            Assert.That((byte)MatchPhase.Created, Is.EqualTo(0));
            Assert.That((byte)MatchPhase.Placement, Is.EqualTo(1));
            Assert.That((byte)MatchPhase.InProgress, Is.EqualTo(2));
            Assert.That((byte)MatchPhase.Finished, Is.EqualTo(3));
        }

        [Test]
        public void MatchEndReason_NumbersAreFrozen()
        {
            Assert.That((byte)MatchEndReason.FleetDestroyed, Is.EqualTo(0));
            Assert.That((byte)MatchEndReason.Forfeit, Is.EqualTo(1));
            Assert.That((byte)MatchEndReason.Abandoned, Is.EqualTo(2));
            Assert.That((byte)MatchEndReason.TimeoutStrikes, Is.EqualTo(3));
            Assert.That((byte)MatchEndReason.Cancelled, Is.EqualTo(4));
        }

        [Test]
        public void ErrorCodes_FormatAsCodePipeParameters()
        {
            Assert.That(ErrorCodes.Format(ErrorCodes.StaleAction, 42), Is.EqualTo("ERR_STALE_ACTION|42"));
            Assert.That(ErrorCodes.Format(ErrorCodes.FleetMismatch, 5, 4), Is.EqualTo("ERR_FLEET_MISMATCH|5|4"));
            Assert.That(ErrorCodes.Format(ErrorCodes.MatchNotFound), Is.EqualTo("ERR_MATCH_NOT_FOUND"));
        }

        [Test]
        public void ErrorCodes_FormatUsesInvariantCulture_SoLogsAreStableAcrossLocales()
        {
            Assert.That(ErrorCodes.Format(ErrorCodes.RateLimited, 1.5), Is.EqualTo("ERR_RATE_LIMITED|1.5"));
        }

        [Test]
        public void ErrorCodes_CodeOfStripsParameters()
        {
            Assert.That(ErrorCodes.CodeOf("ERR_STALE_ACTION|42"), Is.EqualTo("ERR_STALE_ACTION"));
            Assert.That(ErrorCodes.CodeOf("ERR_MATCH_NOT_FOUND"), Is.EqualTo("ERR_MATCH_NOT_FOUND"));
            Assert.That(ErrorCodes.CodeOf(string.Empty), Is.EqualTo(string.Empty));
        }

        [Test]
        public void ErrorCodes_AllStartWithTheErrPrefix_SoLogsStayGreppable()
        {
            string[] codes =
            {
                ErrorCodes.ProtocolMismatch, ErrorCodes.MinVersion, ErrorCodes.FeatureDisabled,
                ErrorCodes.MatchNotFound, ErrorCodes.MatchFinished, ErrorCodes.WrongPhase,
                ErrorCodes.NotYourTurn, ErrorCodes.StaleAction, ErrorCodes.OutOfBounds,
                ErrorCodes.CellAlreadyTargeted, ErrorCodes.InvalidPlacement, ErrorCodes.FleetMismatch,
                ErrorCodes.ShotCountInvalid, ErrorCodes.AlreadyInQueue, ErrorCodes.RateLimited,
                ErrorCodes.InsufficientFunds, ErrorCodes.ItemAlreadyOwned, ErrorCodes.PurchaseInvalid,
                ErrorCodes.Internal
            };

            foreach (string code in codes)
            {
                Assert.That(code, Does.StartWith("ERR_"));
                Assert.That(code, Does.Not.Contain("|"));
            }
        }

        [Test]
        public void ShipPlacement_CellAtWalksAlongTheOrientation()
        {
            ShipPlacement horizontal = new ShipPlacement(ShipClass.Carrier, new Coord(2, 3), Orientation.Horizontal);
            ShipPlacement vertical = new ShipPlacement(ShipClass.Carrier, new Coord(2, 3), Orientation.Vertical);

            Assert.That(horizontal.CellAt(0), Is.EqualTo(new Coord(2, 3)));
            Assert.That(horizontal.CellAt(4), Is.EqualTo(new Coord(6, 3)));
            Assert.That(vertical.CellAt(4), Is.EqualTo(new Coord(2, 7)));
        }

        [Test]
        public void MatchState_SlotLookupResolvesBothPlayersAndRejectsStrangers()
        {
            MatchState state = new MatchState { PlayerA = "alice", PlayerB = "bob" };

            Assert.That(state.SlotOf("alice"), Is.EqualTo(PlayerSlot.A));
            Assert.That(state.SlotOf("bob"), Is.EqualTo(PlayerSlot.B));
            Assert.That(state.SlotOf("mallory"), Is.Null);
        }

        [Test]
        public void PlayerBoard_CountsShipsStillAfloat()
        {
            PlayerBoard board = PlayerBoard.CreateEmpty();
            board.Fleet = new[]
            {
                new ShipRecord { Class = ShipClass.Destroyer, Length = 2, HitCount = 2, SunkOnShotIndex = 7 },
                new ShipRecord { Class = ShipClass.Cruiser, Length = 3, HitCount = 1, SunkOnShotIndex = -1 }
            };

            Assert.That(board.ShipsAfloat, Is.EqualTo(1));
            Assert.That(board.Fleet[0].IsSunk, Is.True);
            Assert.That(board.Fleet[1].IsSunk, Is.False);
        }
    }
}
