using RPGCharacterAnims.Lookups;

namespace RPGCharacterAnims.Actions
{
	public class DoubleJump : MovementActionHandler<EmptyContext>
	{
		public DoubleJump(RPGCharacterMovementController movement)
			: base(movement)
		{
		}

		public override bool CanStartAction(RPGCharacterController controller)
		{
			if (controller.isFalling)
			{
				return movement.canDoubleJump;
			}
			return false;
		}

		protected override void _StartAction(RPGCharacterController controller, EmptyContext context)
		{
			movement.currentState = CharacterState.DoubleJump;
		}

		public override bool IsActive()
		{
			if (movement.currentState != null)
			{
				return (CharacterState)(object)movement.currentState == CharacterState.DoubleJump;
			}
			return false;
		}
	}
}
