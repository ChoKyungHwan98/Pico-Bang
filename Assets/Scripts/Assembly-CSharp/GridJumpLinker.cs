using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class GridJumpLinker : MonoBehaviour
{
	[Header("스캔 설정")]
	public float scanStep = 0.5f;

	public LayerMask groundLayer;

	[Header("점프 조건")]
	public float playerJumpHeight = 3f;

	public float maxJumpDistance = 1f;

	public float minStepHeight = 0.5f;

	[Header("링크 설정")]
	public float linkWidth = 1f;

	private List<Vector3> placedLocations = new List<Vector3>();

	[ContextMenu("Scan and Generate Links")]
	public void GenerateLinks()
	{
		Bounds bounds = new Bounds(base.transform.position, Vector3.zero);
		Renderer[] componentsInChildren = GetComponentsInChildren<Renderer>();
		Collider[] componentsInChildren2 = GetComponentsInChildren<Collider>();
		if (componentsInChildren.Length != 0)
		{
			Renderer[] array = componentsInChildren;
			foreach (Renderer renderer in array)
			{
				bounds.Encapsulate(renderer.bounds);
			}
		}
		else
		{
			if (componentsInChildren2.Length == 0)
			{
				return;
			}
			Collider[] array2 = componentsInChildren2;
			foreach (Collider collider in array2)
			{
				bounds.Encapsulate(collider.bounds);
			}
		}
		ClearLinks();
		float num = bounds.max.y + 1f;
		Debug.Log($"[스캔 시작] 높이: {num}, 최대 점프 거리: {maxJumpDistance}");
		int count = 0;
		for (float num2 = bounds.min.x; num2 <= bounds.max.x; num2 += scanStep)
		{
			for (float num3 = bounds.min.z; num3 <= bounds.max.z; num3 += scanStep)
			{
				if (Physics.Raycast(new Vector3(num2, num, num3), Vector3.down, out var hitInfo, 100f, groundLayer))
				{
					if (IsEdge(hitInfo.point, Vector3.forward))
					{
						CheckGapAndConnect(hitInfo.point, Vector3.forward, ref count);
					}
					if (IsEdge(hitInfo.point, Vector3.back))
					{
						CheckGapAndConnect(hitInfo.point, Vector3.back, ref count);
					}
					if (IsEdge(hitInfo.point, Vector3.left))
					{
						CheckGapAndConnect(hitInfo.point, Vector3.left, ref count);
					}
					if (IsEdge(hitInfo.point, Vector3.right))
					{
						CheckGapAndConnect(hitInfo.point, Vector3.right, ref count);
					}
				}
			}
		}
		Debug.Log($"[완료] 총 {count}개의 점프대를 설치했습니다.");
	}

	private bool IsEdge(Vector3 pos, Vector3 dir)
	{
		Vector3 origin = pos + dir * 0.5f;
		origin.y++;
		if (Physics.Raycast(origin, Vector3.down, out var hitInfo, 5f, groundLayer) && Mathf.Abs(pos.y - hitInfo.point.y) < minStepHeight)
		{
			return false;
		}
		return true;
	}

	private void CheckGapAndConnect(Vector3 startPos, Vector3 direction, ref int count)
	{
		float num = Mathf.Min(scanStep, maxJumpDistance);
		if (num <= 0.1f)
		{
			num = 0.1f;
		}
		for (float num2 = num; num2 <= maxJumpDistance + 0.01f; num2 += num)
		{
			Vector3 origin = startPos + direction * num2;
			origin.y += 0.5f;
			if (Physics.Raycast(origin, Vector3.down, out var hitInfo, 100f, groundLayer))
			{
				float f = startPos.y - hitInfo.point.y;
				if (!(Mathf.Abs(f) > playerJumpHeight) && (num2 > 0.5f || Mathf.Abs(f) > minStepHeight) && NavMesh.SamplePosition(hitInfo.point, out var hit, 1f, -1) && !IsAlreadyPlaced(hit.position))
				{
					CreateLink(startPos, hit.position, direction);
					count++;
					break;
				}
			}
		}
	}

	private bool IsAlreadyPlaced(Vector3 pos)
	{
		foreach (Vector3 placedLocation in placedLocations)
		{
			if (Vector3.Distance(placedLocation, pos) < 0.5f)
			{
				return true;
			}
		}
		return false;
	}

	private void CreateLink(Vector3 start, Vector3 end, Vector3 dir)
	{
		GameObject gameObject = new GameObject("AutoJumpLink");
		gameObject.transform.parent = base.transform;
		if (dir != Vector3.zero)
		{
			gameObject.transform.rotation = Quaternion.LookRotation(dir);
		}
		NavMesh.SamplePosition(start, out var hit, 2f, -1);
		NavMesh.SamplePosition(end, out var hit2, 2f, -1);
		Vector3 position = (hit.hit ? hit.position : start);
		Vector3 vector = (hit2.hit ? hit2.position : end);
		gameObject.transform.position = position;
		NavMeshLink navMeshLink = gameObject.AddComponent<NavMeshLink>();
		navMeshLink.startPoint = Vector3.zero;
		navMeshLink.endPoint = gameObject.transform.InverseTransformPoint(vector);
		navMeshLink.width = linkWidth;
		navMeshLink.bidirectional = true;
		navMeshLink.costModifier = 0f;
		navMeshLink.autoUpdate = false;
		placedLocations.Add(vector);
	}

	[ContextMenu("Clear Links")]
	public void ClearLinks()
	{
		placedLocations.Clear();
		List<GameObject> list = new List<GameObject>();
		foreach (Transform item in base.transform)
		{
			if (item.name == "AutoJumpLink")
			{
				list.Add(item.gameObject);
			}
		}
		foreach (GameObject item2 in list)
		{
			Object.DestroyImmediate(item2);
		}
	}
}
