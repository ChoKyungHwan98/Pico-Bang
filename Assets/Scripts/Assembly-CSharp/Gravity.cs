using UnityEngine;

public class Gravity : MonoBehaviour
{
	[SerializeField]
	private Transform planet;

	private void Update()
	{
		Vector3 normalized = (base.transform.position - planet.position).normalized;
		GetComponent<PlayerMachine>().RotateGravity(normalized);
		base.transform.rotation = Quaternion.FromToRotation(base.transform.up, normalized) * base.transform.rotation;
	}
}
