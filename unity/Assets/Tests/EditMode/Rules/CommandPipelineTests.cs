using System.Collections.Generic;
using NUnit.Framework;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// The funnel itself: gates fire before handlers, Execute always re-validates, a parked
    /// choice freezes everything but its answer. Handlers are faked here - the real ones land
    /// per milestone behind this exact contract.
    /// </summary>
    public class CommandPipelineTests
    {
        private sealed class CountingHandler : ICommandHandler
        {
            public int ValidateCalls, ExecuteCalls;
            public Rejection Answer = Rejection.None;
            public bool ParkPending;
            public bool ConsumePending;   // what a real Respond handler does

            public Rejection Validate(GameState s, ICommand cmd, ICardCatalog cat)
            {
                ValidateCalls++;
                return Answer;
            }

            public void Execute(GameState s, ICommand cmd, ICardCatalog cat, EventSink events)
            {
                ExecuteCalls++;
                if (ConsumePending) s.Pending = null;
                if (ParkPending)
                    s.Pending = new ResponseWindowRequest(cmd.Actor, TrapTrigger.Attack, null);
                events.Add(new TurnStarted(cmd.Actor, s.TurnNumber));
            }
        }

        private static GameState Fresh()
        {
            return MatchSetup.NewMatch(TestData.Catalog,
                new CommanderId("fire"), new CommanderId("water"), 1, RulesOptions.JsParity);
        }

        [Test]
        public void UnregisteredCommand_IsRejectedAsUnknown()
        {
            var s = Fresh();
            var p = new CommandProcessor(TestData.Catalog);
            var r = p.Execute(s, new EndTurnCommand(Side.You), new EventSink());
            Assert.AreEqual(CommandStatus.Rejected, r.Status);
            Assert.AreEqual(Rejection.UnknownCommand, r.Rejection);
        }

        [Test]
        public void GameOver_GatesEverything()
        {
            var s = Fresh();
            s.IsOver = true;
            var p = new CommandProcessor(TestData.Catalog);
            var h = new CountingHandler();
            p.Register(typeof(EndTurnCommand), h);

            var r = p.Execute(s, new EndTurnCommand(Side.You), new EventSink());
            Assert.AreEqual(Rejection.GameOver, r.Rejection);
            Assert.AreEqual(0, h.ValidateCalls, "the gate fires before the handler is even consulted");
        }

        [Test]
        public void Execute_RunsValidateExactlyOnce_ThenTheHandler()
        {
            var s = Fresh();
            var p = new CommandProcessor(TestData.Catalog);
            var h = new CountingHandler();
            p.Register(typeof(EndTurnCommand), h);

            var events = new EventSink();
            var r = p.Execute(s, new EndTurnCommand(Side.You), events);

            Assert.AreEqual(CommandStatus.Applied, r.Status);
            Assert.AreEqual(1, h.ValidateCalls, "Execute must validate internally - no trusted entry point");
            Assert.AreEqual(1, h.ExecuteCalls);
            Assert.AreEqual(1, events.Count, "handler events land in the sink");
        }

        [Test]
        public void RejectingValidator_BlocksTheHandlerBody()
        {
            var s = Fresh();
            var p = new CommandProcessor(TestData.Catalog);
            var h = new CountingHandler();
            h.Answer = Rejection.WrongPhase;
            p.Register(typeof(EndTurnCommand), h);

            var r = p.Execute(s, new EndTurnCommand(Side.You), new EventSink());
            Assert.AreEqual(Rejection.WrongPhase, r.Rejection);
            Assert.AreEqual(0, h.ExecuteCalls);
        }

        [Test]
        public void ParkedChoice_FreezesEverythingExceptRespond()
        {
            var s = Fresh();
            var p = new CommandProcessor(TestData.Catalog);
            var endTurn = new CountingHandler();
            var respond = new CountingHandler();
            respond.ConsumePending = true;
            p.Register(typeof(EndTurnCommand), endTurn);
            p.Register(typeof(RespondCommand), respond);

            s.Pending = new ResponseWindowRequest(Side.Foe, TrapTrigger.Attack, null);

            var blocked = p.Execute(s, new EndTurnCommand(Side.You), new EventSink());
            Assert.AreEqual(Rejection.ChoicePending, blocked.Rejection);
            Assert.AreEqual(0, endTurn.ValidateCalls);

            var answered = p.Execute(s, new RespondCommand(Side.Foe, TrapChosen.Passed), new EventSink());
            Assert.AreEqual(CommandStatus.Applied, answered.Status,
                "the RespondCommand is the one door through a parked choice");
            Assert.AreEqual(1, respond.ExecuteCalls);
        }

        [Test]
        public void Respond_WithoutAPendingRequest_IsRejected()
        {
            var s = Fresh();
            var p = new CommandProcessor(TestData.Catalog);
            p.Register(typeof(RespondCommand), new CountingHandler());

            var r = p.Execute(s, new RespondCommand(Side.You, TrapChosen.Passed), new EventSink());
            Assert.AreEqual(Rejection.NoPendingRequest, r.Rejection);
        }

        [Test]
        public void HandlerThatParks_ReturnsAwaitingChoice()
        {
            var s = Fresh();
            var p = new CommandProcessor(TestData.Catalog);
            var h = new CountingHandler();
            h.ParkPending = true;
            p.Register(typeof(DeclareAttackCommand), h);

            var r = p.Execute(s,
                new DeclareAttackCommand(Side.You, new CellRef(RowKey.YouFront, 2), 5,
                    new WallTarget(Side.Foe)),
                new EventSink());

            Assert.AreEqual(CommandStatus.AwaitingChoice, r.Status);
            Assert.IsNotNull(s.Pending);
            Assert.AreEqual(PendingKind.ResponseWindow, s.Pending.Kind);
        }

        [Test]
        public void DuelEngine_WiresStateProcessorAndEvents()
        {
            var s = Fresh();
            var p = new CommandProcessor(TestData.Catalog);
            var h = new CountingHandler();
            p.Register(typeof(EndTurnCommand), h);

            var engine = new DuelEngine(s, p);
            Assert.AreSame(s, engine.State);
            Assert.IsNull(engine.Pending);

            var r = engine.Apply(new EndTurnCommand(Side.You));
            Assert.IsTrue(r.Applied);
            Assert.AreEqual(1, engine.Events.Count);

            var drained = engine.DrainEvents();
            Assert.AreEqual(1, drained.Count);
            Assert.AreEqual(0, engine.Events.Count, "drain hands over and clears");

            Assert.AreEqual(StateCodec.Hash(s), engine.Hash());
        }

        [Test]
        public void JsParity_IsTheDefaultConstructedValue()
        {
            // Adding a flag with a non-JS default must be a visible mistake (design 01 s8).
            Assert.AreEqual(0, RulesOptions.JsParity.FlagBits);
            Assert.AreEqual(0, default(RulesOptions).FlagBits);
            Assert.AreEqual(0, RulesOptions.JsParity.ActiveFlagCount);
        }

        [Test]
        public void EventSink_DrainIsDestructive_ClearIsSilent()
        {
            var sink = new EventSink();
            sink.Add(new MatchEnded(MatchOutcome.YouWin));
            sink.Add(null);   // ignored, never throws
            Assert.AreEqual(1, sink.Count);

            var got = sink.Drain();
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(0, sink.Count);
        }
    }
}
