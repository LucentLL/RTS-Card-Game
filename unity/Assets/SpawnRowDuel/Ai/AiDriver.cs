using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Ai
{
    /// <summary>
    /// Pumps a policy against an engine. This is the whole of "running the AI": ask for a
    /// command, apply it, repeat. There is no coroutine, no timer, and no privileged path into
    /// the rules - which is what lets self-play run a 200-turn match in milliseconds and lets the
    /// view run the same policy one command per animation beat.
    ///
    /// An AI command that the engine REJECTS is a bug in the policy, not a situation to recover
    /// from, so the driver stops dead and reports it rather than skipping ahead. Silent skipping
    /// is how an AI that quietly does nothing survives to ship.
    /// </summary>
    public sealed class AiDriver
    {
        public sealed class Report
        {
            public int CommandsApplied;
            public int Turns;
            public bool Finished;                // the match ended, or nobody had anything to do
            public Rejection FirstRejection;     // None unless the policy proposed something illegal
            public string FirstRejectionCommand;
        }

        readonly DuelEngine _engine;
        readonly ScriptedAiPolicy[] _policies;

        public AiDriver(DuelEngine engine, params ScriptedAiPolicy[] policies)
        {
            _engine = engine;
            _policies = policies;
        }

        /// <summary>
        /// One command from whichever policy has something to say, or false when none does.
        /// The view calls this on a beat; self-play calls it in a loop.
        /// </summary>
        public bool Step(Report report)
        {
            for (int i = 0; i < _policies.Length; i++)
            {
                var cmd = _policies[i].Next(_engine);
                if (cmd == null) continue;

                var r = _engine.Apply(cmd);
                if (r.Status == CommandStatus.Rejected)
                {
                    if (report != null && report.FirstRejection == Rejection.None)
                    {
                        report.FirstRejection = r.Rejection;
                        report.FirstRejectionCommand = cmd.GetType().Name;
                    }
                    return false;
                }
                if (report != null) report.CommandsApplied++;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Run until the match ends, the turn budget runs out, or the policies fall silent -
        /// handing the turn over when a side reaches its End phase and nobody is mid-choice.
        /// </summary>
        public Report Run(int maxTurns)
        {
            var report = new Report();
            var s = _engine.State;

            while (!s.IsOver && report.Turns < maxTurns)
            {
                if (Step(report))
                {
                    if (report.FirstRejection != Rejection.None) return report;
                    continue;
                }
                if (report.FirstRejection != Rejection.None) return report;

                // nobody wants to act: the only legal move left is the hand-off
                if (s.Pending == null && s.Phase == TurnPhase.End)
                {
                    var next = TurnMachine.Other(s.Turn);
                    var begin = new BeginTurnCommand(next);
                    if (_engine.Apply(begin).Applied)
                    {
                        report.Turns++;
                        report.CommandsApplied++;
                        continue;
                    }
                }
                break;                                       // genuinely stuck
            }

            report.Finished = s.IsOver || report.Turns >= maxTurns;
            return report;
        }
    }
}
