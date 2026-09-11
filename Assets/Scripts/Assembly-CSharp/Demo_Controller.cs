using System.Collections;
using UnityEngine;

public class Demo_Controller : MonoBehaviour
{
	[SerializeField]
	private Runestone_Controller runestoneScript;

	[SerializeField]
	private Portal_Controller portalSimpleScripts;

	[SerializeField]
	private PortalRound_Controller portalRoundScripts;

	[SerializeField]
	private PortalGate_Controller portalGateScript;

	[SerializeField]
	private Vortex_Controller vortexScript;

	[SerializeField]
	private Rift_Controller riftScript;

	[SerializeField]
	private Transform camBaseTF;

	[SerializeField]
	private Light lightMain;

	[SerializeField]
	private GameObject[] SSCameras;


	private int ssCamsMax;

	private void Start()
	{
		ssCamsMax = SSCameras.Length;
		lightMain.intensity = 0f;
		StartCoroutine(DemoRoutine());
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.Escape))
		{
			Application.Quit();
		}
	}

	private IEnumerator DemoRoutine()
	{
		yield return new WaitForSeconds(1f);
		runestoneScript.ToggleRuneStone(_activate: true);
		yield return new WaitForSeconds(3f);
		StartCoroutine(CameraRoutine());
		yield return new WaitForSeconds(2f);
		portalRoundScripts.F_TogglePortalRound(_activate: true);
		yield return new WaitForSeconds(4f);
		vortexScript.F_ToggleVortex(_activate: true);
		yield return new WaitForSeconds(10f);
		portalGateScript.F_TogglePortalGate(_activate: true);
		yield return new WaitForSeconds(7f);
		portalSimpleScripts.TogglePortal(_activate: true);
		yield return new WaitForSeconds(9f);
		riftScript.F_ToggleRift(_activate: true);
		yield return new WaitForSeconds(3f);
		StartCoroutine(FadeLight());
		yield return new WaitForSeconds(45f);
		yield return new WaitForSeconds(8f);
		runestoneScript.ToggleRuneStone(_activate: false);
		yield return new WaitForSeconds(2f);
		portalRoundScripts.F_TogglePortalRound(_activate: false);
		yield return new WaitForSeconds(5f);
		vortexScript.F_ToggleVortex(_activate: false);
		yield return new WaitForSeconds(9f);
		portalGateScript.F_TogglePortalGate(_activate: false);
		yield return new WaitForSeconds(7f);
		portalSimpleScripts.TogglePortal(_activate: false);
		yield return new WaitForSeconds(10f);
		riftScript.F_ToggleRift(_activate: false);
	}

	private IEnumerator CameraRoutine()
	{
		while (true)
		{
			camBaseTF.Rotate(Vector3.up, Time.deltaTime * -8f);
			yield return null;
		}
	}

	private IEnumerator FadeLight()
	{
		float fadeFloat = 0f;
		while (fadeFloat < 1f)
		{
			fadeFloat = Mathf.MoveTowards(fadeFloat, 1f, Time.deltaTime * 0.02f);
			lightMain.intensity = fadeFloat;
			yield return new WaitForSeconds(Time.deltaTime);
		}
	}
}
