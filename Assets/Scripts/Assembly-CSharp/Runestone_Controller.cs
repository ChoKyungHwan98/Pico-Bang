using System.Collections;
using UnityEngine;

public class Runestone_Controller : MonoBehaviour
{
	[Header("Applied to the effects at start")]
	[SerializeField]
	private Color effectsColor;

	[Header("Changing these might `break` the effects")]
	[Space(20f)]
	[SerializeField]
	private Renderer runeStoneRenderer;

	[SerializeField]
	private Transform rocksBaseTF;

	[SerializeField]
	private ParticleSystem[] effectsParticles;

	[SerializeField]
	private Light portalLight;

	[SerializeField]
	private AudioSource implodeAudio;

	[SerializeField]
	private AudioSource forceFieldAudio;

	[SerializeField]
	private AudioSource cracklingAudio;

	[SerializeField]
	private Transform[] runes = new Transform[2];

	[SerializeField]
	private AnimationCurve rockAnimCurve;

	private float maxVolForcefield = 1f;

	private float maxVolCrackling = 1f;

	private float maxIntPortalLight = 2f;

	private float transitionSpeed = 0.5f;

	private Transform myTF;

	private Transform coreParticlesTF;

	private Transform[] rocks = new Transform[4];

	private SpriteRenderer[] runeRenderers = new SpriteRenderer[2];

	private bool inTransition;

	private bool activated;

	private bool animating;

	private Material matInstance;

	private Color runesWantedeColor;

	private float fadeFloat;

	private Coroutine transitionCor;

	private Coroutine animateCor;

	private void Awake()
	{
		Setup();
	}

	public void ToggleRuneStone(bool _activate)
	{
		if (inTransition || activated == _activate)
		{
			return;
		}
		if (_activate)
		{
			implodeAudio.Play();
			activated = true;
			transitionCor = StartCoroutine(TransitionSequence());
			for (int i = 0; i < 2; i++)
			{
				effectsParticles[i].Play();
			}
			forceFieldAudio.Play();
			cracklingAudio.Play();
		}
		else if (!_activate)
		{
			implodeAudio.Play();
			activated = false;
			if (animating)
			{
				StopCoroutine(animateCor);
				animating = false;
			}
			transitionCor = StartCoroutine(TransitionSequence());
		}
	}

	private IEnumerator TransitionSequence()
	{
		inTransition = true;
		float rocksCurrentHeight = rocksBaseTF.localPosition.y;
		Vector3 rocksWantedPosition = rocksBaseTF.localPosition;
		while (inTransition)
		{
			if (activated)
			{
				fadeFloat = Mathf.MoveTowards(fadeFloat, 1f, Time.deltaTime * transitionSpeed);
				rocksWantedPosition.y = fadeFloat * 1.5f;
				if (fadeFloat >= 1f)
				{
					inTransition = false;
					animateCor = StartCoroutine(AnimateActiveEffects());
				}
			}
			else
			{
				fadeFloat = Mathf.MoveTowards(fadeFloat, 0f, Time.deltaTime * transitionSpeed);
				rocksWantedPosition.y = fadeFloat * rocksCurrentHeight;
				if (fadeFloat <= 0f)
				{
					inTransition = false;
					for (int i = 0; i < 2; i++)
					{
						effectsParticles[i].Stop();
					}
					forceFieldAudio.Stop();
					cracklingAudio.Stop();
				}
			}
			forceFieldAudio.volume = maxVolForcefield * fadeFloat;
			cracklingAudio.volume = maxVolCrackling * fadeFloat;
			if (fadeFloat <= 0.7f)
			{
				runesWantedeColor.a = fadeFloat;
				runeRenderers[0].color = runesWantedeColor;
				runeRenderers[1].color = runesWantedeColor;
			}
			runes[0].Rotate(myTF.right, Time.deltaTime * -120f, Space.World);
			runes[1].Rotate(myTF.right, Time.deltaTime * 40f, Space.World);
			matInstance.SetFloat("_EmissionStrength", fadeFloat);
			rocksBaseTF.localPosition = rocksWantedPosition;
			rocksBaseTF.Rotate(myTF.up, Time.deltaTime * (fadeFloat * -120f));
			rocks[0].Rotate(myTF.forward, Time.deltaTime * (fadeFloat * 220f), Space.World);
			rocks[1].Rotate(myTF.forward, Time.deltaTime * (fadeFloat * -280f), Space.World);
			rocks[2].Rotate(myTF.forward, Time.deltaTime * (fadeFloat * 340f), Space.World);
			rocks[3].Rotate(myTF.forward, Time.deltaTime * (fadeFloat * -300f), Space.World);
			portalLight.intensity = maxIntPortalLight * fadeFloat;
			coreParticlesTF.localScale = new Vector3(fadeFloat, fadeFloat, fadeFloat);
			yield return null;
		}
	}

	private IEnumerator AnimateActiveEffects()
	{
		animating = true;
		Vector3 rocksWantedPosition = rocksBaseTF.localPosition;
		float randIntencity = maxIntPortalLight;
		float evalFloat = 0f;
		while (animating)
		{
			evalFloat = Mathf.MoveTowards(evalFloat, 5f, Time.deltaTime * transitionSpeed);
			if (evalFloat == 5f)
			{
				evalFloat = 0f;
			}
			rocksWantedPosition.y = rockAnimCurve.Evaluate(evalFloat) + 1.5f;
			rocksBaseTF.localPosition = rocksWantedPosition;
			rocksBaseTF.Rotate(myTF.up, Time.deltaTime * -120f, Space.World);
			rocks[0].Rotate(myTF.forward, Time.deltaTime * 220f, Space.World);
			rocks[1].Rotate(myTF.forward, Time.deltaTime * -280f, Space.World);
			rocks[2].Rotate(myTF.forward, Time.deltaTime * 340f, Space.World);
			rocks[3].Rotate(myTF.forward, Time.deltaTime * -300f, Space.World);
			runes[0].Rotate(myTF.right, Time.deltaTime * -120f, Space.World);
			runes[1].Rotate(myTF.right, Time.deltaTime * 40f, Space.World);
			if (portalLight.intensity == randIntencity)
			{
				randIntencity = Random.Range(-0.5f, 0.5f) + maxIntPortalLight;
			}
			portalLight.intensity = Mathf.MoveTowards(portalLight.intensity, randIntencity, Time.deltaTime * 1.5f);
			yield return null;
		}
	}

	private void Setup()
	{
		myTF = base.transform;
		coreParticlesTF = effectsParticles[0].transform;
		ParticleSystem[] array = effectsParticles;
		for (int i = 0; i < array.Length; i++)
		{
			ParticleSystem.MainModule main = array[i].main;
			main.startColor = effectsColor;
		}
		matInstance = runeStoneRenderer.material;
		matInstance.SetColor("_EmissionColor", effectsColor);
		matInstance.SetFloat("_EmissionStrength", 0f);
		for (int j = 0; j < rocksBaseTF.childCount; j++)
		{
			rocks[j] = rocksBaseTF.GetChild(j);
			rocksBaseTF.GetChild(j).GetComponent<MeshRenderer>().material = matInstance;
		}
		runesWantedeColor = effectsColor;
		runesWantedeColor.a = 0f;
		for (int k = 0; k < runes.Length; k++)
		{
			runeRenderers[k] = runes[k].GetComponent<SpriteRenderer>();
			runeRenderers[k].color = runesWantedeColor;
		}
		forceFieldAudio.volume = 0f;
		cracklingAudio.volume = 0f;
		portalLight.color = effectsColor;
		portalLight.intensity = 0f;
	}
}
