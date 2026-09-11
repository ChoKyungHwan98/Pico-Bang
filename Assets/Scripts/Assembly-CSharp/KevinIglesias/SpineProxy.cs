using UnityEngine;

namespace KevinIglesias
{
	public class SpineProxy : MonoBehaviour
	{
		[SerializeField]
		private Transform originalSpine;

		private Quaternion rotationOffset = Quaternion.identity;

		private void Awake()
		{
			if (originalSpine != null)
			{
				rotationOffset = Quaternion.Inverse(base.transform.rotation) * originalSpine.rotation;
			}
		}

		private void LateUpdate()
		{
			if (!(originalSpine == null))
			{
				originalSpine.rotation = base.transform.rotation * rotationOffset;
			}
		}
	}
}
