using UnityEngine;

public class MapMeasurer : MonoBehaviour
{
	[ContextMenu("Measure Map Size")]
	public void Measure()
	{
		Bounds bounds = new Bounds(base.transform.position, Vector3.zero);
		Renderer[] componentsInChildren = GetComponentsInChildren<Renderer>();
		if (componentsInChildren.Length == 0)
		{
			Debug.LogError("맵 안에 Renderer가 하나도 없습니다!");
			return;
		}
		Renderer[] array = componentsInChildren;
		foreach (Renderer renderer in array)
		{
			bounds.Encapsulate(renderer.bounds);
		}
		Debug.Log("====== 맵 측정 결과 ======");
		Debug.Log($"가로(X): {bounds.size.x:F1}m");
		Debug.Log($"세로(Z): {bounds.size.z:F1}m");
		Debug.Log($"높이(Y): {bounds.size.y:F1}m");
		Debug.Log($"중심점: {bounds.center}");
		Debug.Log("========================");
	}
}
