using UnityEngine;

public class Portal : MonoBehaviour
{
	public bool IsUnlocked = true;

	private void OnTriggerEnter(Collider other)
	{
		if (IsUnlocked && other.CompareTag("Player") && GameFlowManager.Instance != null)
		{
			PlaytestRecorder.Record("portal_enter", "player", other.transform.position);
			GameFlowManager.Instance.TriggerGameClear();
		}
	}
}
