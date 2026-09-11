using RPGCharacterAnims.Lookups;

namespace RPGCharacterAnims.Actions
{
	public class Idle : MovementActionHandler<EmptyContext>
	{
		public Idle(RPGCharacterMovementController movement)
			: base(movement)
		{
		}

		public override bool CanStartAction(RPGCharacterController controller)
		{
			if (controller.isMoving)
			{
				return controller.moveInput.magnitude < 0.2f;
			}
			if (!controller.maintainingGround)
			{
				return controller.acquiringGround;
			}
			return true;
		}

		protected override void _StartAction(RPGCharacterController controller, EmptyContext context)
		{
			movement.currentState = CharacterState.Idle;
		}

		public override bool IsActive()
		{
			if (movement.currentState != null)
			{
				return (CharacterState)(object)movement.currentState == CharacterState.Idle;
			}
			return false;
		}
	}
}
