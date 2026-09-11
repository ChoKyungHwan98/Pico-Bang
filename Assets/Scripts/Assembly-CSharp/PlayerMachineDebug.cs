using UnityEngine;

public class PlayerMachineDebug : MonoBehaviour
{
	[SerializeField]
	private PlayerMachine playerMachine;

	private float timeScale = 1f;

	private void Awake()
	{
		if (playerMachine == null)
		{
			playerMachine = GetComponent<PlayerMachine>();
		}
	}

	private void OnGUI()
	{
		GUI.Box(new Rect(10f, 10f, 200f, 100f), "Player Machine");
		GUI.TextField(new Rect(20f, 40f, 180f, 20f), $"State: {playerMachine.currentState}");
		timeScale = GUI.HorizontalSlider(new Rect(20f, 70f, 180f, 20f), timeScale, 0f, 1f);
		Time.timeScale = timeScale;
	}
}
