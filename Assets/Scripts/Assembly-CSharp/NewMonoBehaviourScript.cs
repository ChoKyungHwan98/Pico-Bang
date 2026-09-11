using System.Collections.Generic;
using UnityEngine;

public class NewMonoBehaviourScript : MonoBehaviour
{
	private void Start()
	{
		foreach (string item in new List<string> { "Sword", "Shield", "Potion", "Armor", "Coffee" })
		{
			Debug.Log("보유 아이템: " + item);
		}
	}

	private void Update()
	{
	}
}
