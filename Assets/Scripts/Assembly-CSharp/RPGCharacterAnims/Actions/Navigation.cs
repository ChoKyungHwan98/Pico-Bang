using UnityEngine;

namespace RPGCharacterAnims.Actions
{
	public class Navigation : BaseActionHandler<Vector3>
	{
		private RPGCharacterNavigationController navigation;

		public Navigation(RPGCharacterNavigationController navigation)
		{
			this.navigation = navigation;
		}

		public override bool CanStartAction(RPGCharacterController controller)
		{
			return navigation != null;
		}

		public override bool CanEndAction(RPGCharacterController controller)
		{
			if (navigation != null)
			{
				return navigation.isNavigating;
			}
			return false;
		}

		protected override void _StartAction(RPGCharacterController controller, Vector3 context)
		{
			navigation.MeshNavToPoint(context);
		}

		public override bool IsActive()
		{
			return navigation.isNavigating;
		}

		protected override void _EndAction(RPGCharacterController controller)
		{
			navigation.StopNavigating();
		}
	}
}
