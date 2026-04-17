using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace RunHistoryReplay.RunHistoryReplayCode;

public class SingleCombatRoom : CombatRoom
{
    public SingleCombatRoom(EncounterModel encounter, IRunState? runState) : base(encounter, runState)
    {
    }

    public SingleCombatRoom(CombatState combatState) : base(combatState)
    {
    }

    public override Task Exit(IRunState? runState)
    {
        base.Exit(runState);
        return RunManager.Instance.EnterRoom(
            new EventRoom(ModelDb.GetById<EventModel>(ModelDb.GetId<TheArchitect>())));
    }
}