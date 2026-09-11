using UnityEngine;

namespace KevinIglesias
{
	public class HumanSoldierChangeWeaponSMB : StateMachineBehaviour
	{
		public SoldierWeapons weaponToDraw;

		private HumanSoldierController hSC;

		public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
		{
			if (hSC == null)
			{
				hSC = animator.GetComponent<HumanSoldierController>();
			}
			if ((bool)hSC)
			{
				hSC.ChangeWeapon(weaponToDraw);
			}
		}
	}
}
