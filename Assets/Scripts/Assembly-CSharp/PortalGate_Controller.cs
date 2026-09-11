using System.Collections;
using System.Linq;
using UnityEngine;

public class PortalGate_Controller : MonoBehaviour
{
	[Header("Applied to the effects at start")]
	[SerializeField]
	private Color portalEffectColor;

	[Header("Changing these might `break` the effects")]
	[Space(20f)]
	[SerializeField]
	private Renderer portalRenderer;

	[SerializeField]
	private ParticleSystem[] effectsPartSystems;

	[SerializeField]
	private Light portalLight;

	[SerializeField]
	private Transform symbolTF;

	[SerializeField]
	private AudioSource portalAudio;

	[SerializeField]
	private AudioSource flashAudio;

	private bool portalActive;

	private bool inTransition;

	private float transitionF;

	private float lightF;

	private Material portalMat;

	private Material portalEffectMat;

	private Vector3 symbolStartPos;

	private Coroutine transitionCor;

	private Coroutine symbolMovementCor;

	private void OnEnable()
	{
		Material[] array = portalRenderer.materials.ToArray();
		portalMat = array[0];
		portalEffectMat = array[1];
		portalMat.SetColor("_EmissionColor", portalEffectColor);
		portalMat.SetFloat("_EmissionStrength", 0f);
		portalEffectMat.SetColor("_ColorMain", portalEffectColor);
		portalEffectMat.SetFloat("_PortalFade", 0f);
		symbolStartPos = symbolTF.localPosition;
		symbolTF.GetComponent<Renderer>().material = portalMat;
		portalLight.color = portalEffectColor;
		lightF = portalLight.intensity;
		portalLight.intensity = 0f;
		ParticleSystem[] array2 = effectsPartSystems;
		for (int i = 0; i < array2.Length; i++)
		{
			ParticleSystem.MainModule main = array2[i].main;
			main.startColor = portalEffectColor;
		}
	}

	public void F_TogglePortalGate(bool _activate)
	{
		if (inTransition || portalActive == _activate)
		{
			return;
		}
		portalActive = _activate;
		if (_activate)
		{
			ParticleSystem[] array = effectsPartSystems;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Play();
			}
			portalAudio.Play();
			flashAudio.Play();
			symbolMovementCor = StartCoroutine(SymbolMovement());
		}
		else if (!_activate)
		{
			ParticleSystem[] array = effectsPartSystems;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Stop();
			}
		}
		if (!inTransition)
		{
			transitionCor = StartCoroutine(PortalTransition());
		}
	}

	private IEnumerator PortalTransition()
	{
		inTransition = true;
		if (portalActive)
		{
			while (transitionF < 1f)
			{
				transitionF = Mathf.MoveTowards(transitionF, 1f, Time.deltaTime * 0.2f);
				portalMat.SetFloat("_EmissionStrength", transitionF);
				portalEffectMat.SetFloat("_PortalFade", transitionF * 0.4f);
				portalLight.intensity = lightF * transitionF;
				portalAudio.volume = transitionF * 0.8f;
				yield return new WaitForSeconds(Time.deltaTime);
			}
			inTransition = false;
			StopCoroutine(transitionCor);
		}
		else if (!portalActive)
		{
			while (transitionF > 0f)
			{
				transitionF = Mathf.MoveTowards(transitionF, 0f, Time.deltaTime * 0.4f);
				portalMat.SetFloat("_EmissionStrength", transitionF);
				portalEffectMat.SetFloat("_PortalFade", transitionF * 0.4f);
				portalLight.intensity = lightF * transitionF;
				portalAudio.volume = transitionF * 0.8f;
				yield return new WaitForSeconds(Time.deltaTime);
			}
			portalAudio.Stop();
			inTransition = false;
			StopCoroutine(symbolMovementCor);
			StopCoroutine(transitionCor);
		}
	}

	private IEnumerator SymbolMovement()
	{
		Vector3 randomPos = symbolStartPos;
		float lerpF = 0f;
		while (true)
		{
			if (symbolTF.localPosition == randomPos)
			{
				randomPos[1] = Random.Range(-0.08f, 0.08f);
				randomPos[2] = Random.Range(-0.08f, 0.08f);
				randomPos = symbolStartPos + randomPos;
				lerpF = 0f;
			}
			else
			{
				symbolTF.localPosition = Vector3.Slerp(symbolTF.localPosition, randomPos, lerpF);
				lerpF += 0.001f;
			}
			yield return new WaitForSeconds(0.04f);
		}
	}
}
