namespace RPGCharacterAnims.Actions
{
	public abstract class MovementActionHandler<TContext> : InstantActionHandler<TContext>
	{
		protected RPGCharacterMovementController movement;

		public MovementActionHandler(RPGCharacterMovementController movement)
		{
			this.movement = movement;
		}
	}
}
