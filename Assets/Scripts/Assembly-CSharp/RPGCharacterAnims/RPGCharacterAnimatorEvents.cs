using UnityEngine;
using UnityEngine.Events;

namespace RPGCharacterAnims
{
	[HelpURL("https://docs.unity3d.com/Manual/script-AnimationWindowEvent.html")]
	public class RPGCharacterAnimatorEvents : MonoBehaviour
	{
		public UnityEvent OnHit = new UnityEvent();

		public UnityEvent OnShoot = new UnityEvent();

		public UnityEvent OnFootR = new UnityEvent();

		public UnityEvent OnFootL = new UnityEvent();

		public UnityEvent OnLand = new UnityEvent();

		public UnityEvent OnWeaponSwitch = new UnityEvent();

		public AnimatorMoveEvent OnMove = new AnimatorMoveEvent();

		private RPGCharacterController rpgCharacterController;

		private Animator animator;

		private void Awake()
		{
			rpgCharacterController = GetComponentInParent<RPGCharacterController>();
			animator = GetComponent<Animator>();
		}

		public void Hit()
		{
			OnHit.Invoke();
		}

		public void Shoot()
		{
			OnShoot.Invoke();
		}

		public void FootR()
		{
			OnFootR.Invoke();
		}

		public void FootL()
		{
			OnFootL.Invoke();
		}

		public void Land()
		{
			OnLand.Invoke();
		}

		public void WeaponSwitch()
		{
			OnWeaponSwitch.Invoke();
		}

		private void OnAnimatorMove()
		{
			if ((bool)animator && !rpgCharacterController.isNavigating)
			{
				OnMove.Invoke(animator.deltaPosition, animator.rootRotation);
			}
		}
	}
}
