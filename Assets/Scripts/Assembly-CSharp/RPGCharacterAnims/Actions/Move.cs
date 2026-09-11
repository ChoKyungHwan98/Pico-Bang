using RPGCharacterAnims.Lookups;

namespace RPGCharacterAnims.Actions
{
	public class Move : MovementActionHandler<EmptyContext>
	{
		public Move(RPGCharacterMovementController movement)
			: base(movement)
		{
		}

		public override bool CanStartAction(RPGCharacterController controller)
		{
			if (controller.canMove && controller.moveInput.sqrMagnitude > 0.1f)
			{
				return controller.maintainingGround;
			}
			return false;
		}

		protected override void _StartAction(RPGCharacterController controller, EmptyContext context)
		{
			movement.currentState = CharacterState.Move;
		}

		public override bool IsActive()
		{
			if (movement.currentState != null)
			{
				return (CharacterState)(object)movement.currentState == CharacterState.Move;
			}
			return false;
		}
	}
}
