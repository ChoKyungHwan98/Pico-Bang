using RPGCharacterAnims.Lookups;

namespace RPGCharacterAnims.Actions
{
	public class DiveRoll : MovementActionHandler<DiveRollType>
	{
		public DiveRoll(RPGCharacterMovementController movement)
			: base(movement)
		{
		}

		public override bool CanStartAction(RPGCharacterController controller)
		{
			return controller.canAction;
		}

		protected override void _StartAction(RPGCharacterController controller, DiveRollType rollType)
		{
			controller.DiveRoll(rollType);
			movement.currentState = CharacterState.DiveRoll;
		}

		public override bool IsActive()
		{
			if (movement.currentState != null)
			{
				return (CharacterState)(object)movement.currentState == CharacterState.DiveRoll;
			}
			return false;
		}
	}
}
