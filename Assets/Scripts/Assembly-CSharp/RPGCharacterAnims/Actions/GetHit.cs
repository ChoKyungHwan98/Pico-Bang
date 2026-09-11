using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims.Actions
{
	public class GetHit : MovementActionHandler<HitContext>
	{
		public GetHit(RPGCharacterMovementController movement)
			: base(movement)
		{
		}

		public override bool CanStartAction(RPGCharacterController controller)
		{
			if (!controller.isKnockback)
			{
				return !controller.isKnockdown;
			}
			return false;
		}

		protected override void _StartAction(RPGCharacterController controller, HitContext context)
		{
			int num = context.number;
			Vector3 vector = context.direction;
			float force = context.force;
			float variableForce = context.variableForce;
			if (num == -1)
			{
				num = (int)AnimationVariations.Hits.TakeRandom();
				vector = AnimationData.HitDirection((HitType)num);
				vector = controller.transform.rotation * vector;
			}
			else if (context.relative)
			{
				vector = controller.transform.rotation * vector;
			}
			controller.GetHit(num);
			movement.KnockbackForce(vector, force, variableForce);
		}
	}
}
