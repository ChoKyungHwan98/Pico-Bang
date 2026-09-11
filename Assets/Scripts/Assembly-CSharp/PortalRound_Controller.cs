using System.Collections;
using UnityEngine;

public class PortalRound_Controller : MonoBehaviour
{
	[Header("Applied to the effects at start")]
	[SerializeField]
	private Color effectsColor;

	[Header("Changing these might `break` the effects")]
	[Space(20f)]
	[SerializeField]
	private ParticleSystem[] effectsPartSystems;

	[SerializeField]
	private Light portalLight;

	[SerializeField]
	private Transform portalRoundMeshTF;

	[SerializeField]
	private AudioSource portalAudio;

	[Space(10f)]
	[SerializeField]
	private bool floatingAnimationOn = true;

	[SerializeField]
	private AnimationCurve floatingCurve;

	private bool portalActive;

	private bool inTransition;

	private bool isFloating;

	private float transitionF;

	private float lightF;

	private float evalFloat;

	private float floatSpeed = 0.2f;

	private Material portalMaterial;

	private Transform portalTF;

	private Vector3 originalPosition;

	private Coroutine transitionCor;

	private Coroutine floatingMovementCor;

	private void OnEnable()
	{
		portalTF = base.transform;
		originalPosition = portalTF.position;
		portalMaterial = portalRoundMeshTF.GetComponent<Renderer>().material;
		portalMaterial.SetColor("_EmissionColor", effectsColor);
		portalMaterial.SetFloat("_EmissionStrength", 0f);
		portalLight.color = effectsColor;
		lightF = portalLight.intensity;
		portalLight.intensity = 0f;
		ParticleSystem[] array = effectsPartSystems;
		for (int i = 0; i < array.Length; i++)
		{
			ParticleSystem.MainModule main = array[i].main;
			main.startColor = effectsColor;
		}
	}

	private IEnumerator PortalTransition()
	{
		inTransition = true;
		if (portalActive)
		{
			while (transitionF < 1f)
			{
				transitionF = Mathf.MoveTowards(transitionF, 1f, Time.deltaTime * 0.1f);
				portalMaterial.SetFloat("_EmissionStrength", transitionF);
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
				portalMaterial.SetFloat("_EmissionStrength", transitionF);
				portalLight.intensity = lightF * transitionF;
				portalAudio.volume = transitionF * 0.8f;
				yield return new WaitForSeconds(Time.deltaTime);
			}
			portalAudio.Stop();
			inTransition = false;
			StopCoroutine(transitionCor);
		}
	}

	private IEnumerator FloatingMovement()
	{
		isFloating = true;
		Vector3 wantedPosition = originalPosition;
		while (true)
		{
			if (evalFloat >= 1f)
			{
				evalFloat = 0f;
			}
			evalFloat += Time.deltaTime * floatSpeed;
			wantedPosition[1] = originalPosition.y + floatingCurve.Evaluate(evalFloat);
			portalTF.position = wantedPosition;
			yield return new WaitForSeconds(Time.deltaTime);
		}
	}

	public void F_TogglePortalRound(bool _activate)
	{
		if (portalActive == _activate)
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
			if (floatingAnimationOn && !isFloating)
			{
				floatingMovementCor = StartCoroutine(FloatingMovement());
			}
		}
		else if (!_activate)
		{
			ParticleSystem[] array = effectsPartSystems;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Stop();
			}
			if (isFloating)
			{
				StopCoroutine(floatingMovementCor);
				isFloating = false;
			}
		}
		if (!inTransition)
		{
			transitionCor = StartCoroutine(PortalTransition());
		}
	}
}
