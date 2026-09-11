using System.Collections;
using UnityEngine;

namespace KevinIglesias
{
	public class HumanSoldierController : MonoBehaviour
	{
		public Animator animator;

		public SoldierWeapons equippedWeapon;

		public SoldierPosition position;

		public SoldierAction action;

		public SoldierMovement movement;

		public GameObject[] weapons;

		private IEnumerator changingWeaponsCoroutine;

		private int currentWeapon;

		private void Update()
		{
			animator.SetTrigger(equippedWeapon.ToString());
			animator.SetTrigger(position.ToString());
			if (action != SoldierAction.Nothing && action != SoldierAction.ChangeWeapons)
			{
				animator.SetTrigger(action.ToString());
			}
			if (action == SoldierAction.ChangeWeapons)
			{
				if (changingWeaponsCoroutine == null)
				{
					changingWeaponsCoroutine = ChangingWeapons();
					StartCoroutine(changingWeaponsCoroutine);
				}
			}
			else if (changingWeaponsCoroutine != null)
			{
				StopCoroutine(changingWeaponsCoroutine);
				changingWeaponsCoroutine = null;
			}
			animator.SetTrigger(movement.ToString());
		}

		private IEnumerator ChangingWeapons()
		{
			currentWeapon++;
			if (currentWeapon > 4)
			{
				currentWeapon = 0;
			}
			Animator obj = animator;
			UnsheatheWeapons unsheatheWeapons = (UnsheatheWeapons)currentWeapon;
			obj.SetTrigger(unsheatheWeapons.ToString());
			yield return new WaitForSeconds(1.5f);
			changingWeaponsCoroutine = ChangingWeapons();
			StartCoroutine(changingWeaponsCoroutine);
		}

		public void ChangeWeapon(SoldierWeapons newWeapon)
		{
			for (int i = 0; i < weapons.Length; i++)
			{
				weapons[i].SetActive(value: false);
			}
			weapons[(int)(newWeapon - 1)].SetActive(value: true);
			if (newWeapon == SoldierWeapons.DualGun)
			{
				weapons[3].SetActive(value: true);
			}
		}
	}
}
