using System.Collections.Generic;
using UnityEngine;

public class SpawnAreaGenerator : MonoBehaviour
{
	[Header("Settings")]
	public Vector3 rangeSize = new Vector3(50f, 10f, 50f);

	public float spacing = 10f;

	public string areaNamePrefix = "SpawnArea_Wall_";

	[Header("Wall Detection")]
	public float rayDistance = 10f;

	[Range(0f, 0.5f)]
	public float verticalThreshold = 0.3f;

	[Header("References")]
	public TargetManager targetManager;

	private List<Vector3> createdPositions = new List<Vector3>();

	private void OnDrawGizmos()
	{
		Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
		Gizmos.DrawCube(base.transform.position, rangeSize);
		Gizmos.color = Color.green;
		Gizmos.DrawWireCube(base.transform.position, rangeSize);
	}
}
