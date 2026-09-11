using UnityEngine;

public class Portal : MonoBehaviour
{
	private void OnTriggerEnter(Collider other)
	{
		if (other.CompareTag("Player") && GameFlowManager.Instance != null)
		{
			GameFlowManager.Instance.TriggerGameClear();
		}
	}
}
