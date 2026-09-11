using UnityEngine;
using UnityEngine.UI;

namespace MatthewAssets
{
	public class PrefabManager : MonoBehaviour
	{
		public GameObject[] prefabs;

		public Collider floorCollider;

		public Transform cameraPivot;

		public float cameraRotationSpeed = 10f;

		public float destroyDelay = 2f;

		public Text infoText;

		private int currentIndex;

		private void Start()
		{
			UpdateInfoText();
		}

		private void Update()
		{
			if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
			{
				SelectPreviousPrefab();
			}
			if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
			{
				SelectNextPrefab();
			}
			if (Input.GetMouseButtonDown(0) && floorCollider.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out var hitInfo, 1000f))
			{
				Object.Destroy(Object.Instantiate(prefabs[currentIndex], hitInfo.point, Quaternion.identity), destroyDelay);
			}
			cameraPivot.Rotate(Vector3.up * (cameraRotationSpeed * Time.deltaTime));
		}

		private void SelectPreviousPrefab()
		{
			currentIndex--;
			if (currentIndex < 0)
			{
				currentIndex = prefabs.Length - 1;
			}
			UpdateInfoText();
		}

		private void SelectNextPrefab()
		{
			currentIndex++;
			if (currentIndex >= prefabs.Length)
			{
				currentIndex = 0;
			}
			UpdateInfoText();
		}

		private void UpdateInfoText()
		{
			int num = currentIndex + 1;
			int num2 = prefabs.Length;
			infoText.text = $"({num}/{num2}) \nCurrent effect: {prefabs[currentIndex].name} ";
		}
	}
}
