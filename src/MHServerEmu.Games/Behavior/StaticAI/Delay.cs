﻿
namespace MHServerEmu.Games.Behavior.StaticAI
{
    public class Delay : IAIState
    {
        public void End(AIController ownerController, StaticBehaviorReturnType state)
        {
            throw new NotImplementedException();
        }

        public void Start(IStateContext context)
        {
            throw new NotImplementedException();
        }

        public StaticBehaviorReturnType Update(IStateContext context)
        {
            throw new NotImplementedException();
        }

        public bool Validate(IStateContext context)
        {
            throw new NotImplementedException();
        }
    }

    public class DelayContext : IStateContext
    {
        public DelayContext(AIController ownerController) : base(ownerController)
        {
        }
    }
}
