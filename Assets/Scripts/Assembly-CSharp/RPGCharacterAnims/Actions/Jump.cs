using RPGCharacterAnims.Lookups;

namespace RPGCharacterAnims.Actions
{
	public class Jump : MovementActionHandler<EmptyContext>
	{
		public Jump(RPGCharacterMovementController movement)
			: base(movement)
		{
		}

		public override bool CanStartAction(RPGCharacterController controller)
		{
			if ((movement.canJump || movement.canDoubleJump) && controller.maintainingGround)
			{
				return controller.canAction;
			}
			return false;
		}

		protected override void _StartAction(RPGCharacterController controller, EmptyContext context)
		{
			movement.currentState = CharacterState.Jump;
		}

		public override bool IsActive()
		{
			if (movement.currentState != null)
			{
				return (CharacterState)(object)movement.currentState == CharacterState.Jump;
			}
			return false;
		}
	}
}
