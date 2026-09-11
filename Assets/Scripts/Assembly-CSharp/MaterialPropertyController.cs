using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class MaterialPropertyController : MonoBehaviour
{
	[TextArea(2, 5)]
	public string HowToUse = "For the effect to appear, Add the material(s) in this list as a Full Screen Pass Renderer Feature to the active Universal Renderer Data asset. For more details check out the Documentation.";

	[Tooltip("Drag and drop your materials here in the Inspector.")]
	public List<Material> materials;

	private string propertyName = "_enabled";

	private bool isEnabled = true;

	private void OnValidate()
	{
		UpdateShaderProperties();
	}

	private void Update()
	{
		if (!Application.isPlaying)
		{
			UpdateShaderProperties();
		}
	}

	private void OnEnable()
	{
		isEnabled = true;
		UpdateShaderProperties();
	}

	private void OnDisable()
	{
		isEnabled = false;
		UpdateShaderProperties();
	}

	private void UpdateShaderProperties()
	{
		if (materials == null || materials.Count <= 0)
		{
			return;
		}
		int value = (isEnabled ? 1 : 0);
		foreach (Material material in materials)
		{
			if (material != null)
			{
				material.SetInt(propertyName, value);
			}
		}
	}
}
