using System.Collections;
using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims
{
	public class IKHands : MonoBehaviour
	{
		private Animator animator;

		private RPGCharacterWeaponController rpgCharacterWeaponController;

		public Transform leftHandObj;

		public Transform attachLeft;

		public bool canBeUsed;

		public bool isUsed;

		[Range(0f, 1f)]
		public float leftHandPositionWeight;

		[Range(0f, 1f)]
		public float leftHandRotationWeight;

		private Transform blendToTransform;

		private Coroutine co;

		private void Awake()
		{
			animator = GetComponent<Animator>();
			rpgCharacterWeaponController = GetComponentInParent<RPGCharacterWeaponController>();
		}

		private void OnAnimatorIK(int layerIndex)
		{
			if ((bool)leftHandObj)
			{
				animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftHandPositionWeight);
				animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftHandRotationWeight);
				if ((bool)attachLeft)
				{
					animator.SetIKPosition(AvatarIKGoal.LeftHand, attachLeft.position);
					animator.SetIKRotation(AvatarIKGoal.LeftHand, attachLeft.rotation);
				}
			}
		}

		public void BlendIK(bool blendOn, float delay, float timeToBlend, Weapon weapon)
		{
			if (weapon.Is2HandedWeapon() && blendOn)
			{
				isUsed = true;
			}
			if (canBeUsed & isUsed)
			{
				StopAllCoroutines();
				co = StartCoroutine(_BlendIK(blendOn, delay, timeToBlend, weapon));
			}
			if (!blendOn)
			{
				isUsed = false;
			}
		}

		private IEnumerator _BlendIK(bool blendOn, float delay, float timeToBlend, Weapon weapon)
		{
			GetCurrentWeaponAttachPoint(weapon);
			yield return new WaitForSeconds(delay);
			float t = 0f;
			int blendTo = 0;
			int blendFrom = 0;
			if (blendOn)
			{
				blendTo = 1;
			}
			else
			{
				blendFrom = 1;
			}
			while (t < 1f)
			{
				t += Time.deltaTime / timeToBlend;
				attachLeft = blendToTransform;
				leftHandPositionWeight = Mathf.Lerp(blendFrom, blendTo, t);
				leftHandRotationWeight = Mathf.Lerp(blendFrom, blendTo, t);
				yield return null;
			}
		}

		public void SetIKPause(float pauseTime)
		{
			if (canBeUsed && isUsed)
			{
				StopAllCoroutines();
				co = StartCoroutine(_SetIKPause(pauseTime));
			}
		}

		private IEnumerator _SetIKPause(float pauseTime)
		{
			float t = 0f;
			while (t < 1f)
			{
				t += Time.deltaTime / 0.1f;
				leftHandPositionWeight = Mathf.Lerp(1f, 0f, t);
				leftHandRotationWeight = Mathf.Lerp(1f, 0f, t);
				yield return null;
			}
			yield return new WaitForSeconds(pauseTime - 0.2f);
			t = 0f;
			while (t < 1f)
			{
				t += Time.deltaTime / 0.1f;
				leftHandPositionWeight = Mathf.Lerp(0f, 1f, t);
				leftHandRotationWeight = Mathf.Lerp(0f, 1f, t);
				yield return null;
			}
		}

		private void GetCurrentWeaponAttachPoint(Weapon weapon)
		{
			if (weapon == Weapon.TwoHandSword)
			{
				blendToTransform = rpgCharacterWeaponController.twoHandSword.transform.GetChild(0).transform;
			}
		}
	}
}
