namespace RPGCharacterAnims.Actions
{
	public abstract class InstantActionHandler<TContext> : BaseActionHandler<TContext>
	{
		public override void StartAction(RPGCharacterController controller, object context)
		{
			base.StartAction(controller, context);
			base.EndAction(controller);
		}

		public override bool IsActive()
		{
			return false;
		}

		public override bool CanEndAction(RPGCharacterController controller)
		{
			return true;
		}

		protected override void _EndAction(RPGCharacterController controller)
		{
		}
	}
}
