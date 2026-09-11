using System;

namespace RPGCharacterAnims.Actions
{
	public class SimpleActionHandler : BaseActionHandler<EmptyContext>
	{
		public SimpleActionHandler(Action onStart, Action onEnd)
		{
			AddStartListener(onStart);
			AddEndListener(onEnd);
		}

		public override bool CanStartAction(RPGCharacterController controller)
		{
			return !active;
		}

		public override bool CanEndAction(RPGCharacterController controller)
		{
			return active;
		}

		protected override void _StartAction(RPGCharacterController controller, EmptyContext context)
		{
		}

		protected override void _EndAction(RPGCharacterController controller)
		{
		}
	}
}
