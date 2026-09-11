using UnityEngine;

public class ProximityActivate : MonoBehaviour
{
	public Transform distanceActivator;

	public Transform lookAtActivator;

	public float distance;

	public Transform activator;

	public bool activeState;

	public CanvasGroup target;

	public bool lookAtCamera = true;

	public bool enableInfoPanel;

	public GameObject infoIcon;

	private float alpha;

	public CanvasGroup infoPanel;

	private Quaternion originRotation;

	private Quaternion targetRotation;

	private void Start()
	{
		originRotation = base.transform.rotation;
		alpha = (activeState ? 1 : (-1));
		if (activator == null)
		{
			activator = Camera.main.transform;
		}
		infoIcon.SetActive(infoPanel != null);
	}

	private bool IsTargetNear()
	{
		if ((distanceActivator.position - activator.position).sqrMagnitude < distance * distance)
		{
			if (lookAtActivator != null)
			{
				Vector3 vector = lookAtActivator.position - activator.position;
				if (Vector3.Dot(activator.forward, vector.normalized) > 0.95f)
				{
					return true;
				}
			}
			Vector3 vector2 = target.transform.position - activator.position;
			if (Vector3.Dot(activator.forward, vector2.normalized) > 0.95f)
			{
				return true;
			}
		}
		return false;
	}

	private void Update()
	{
		if (!activeState)
		{
			if (IsTargetNear())
			{
				alpha = 1f;
				activeState = true;
			}
		}
		else if (!IsTargetNear())
		{
			alpha = -1f;
			activeState = false;
			enableInfoPanel = false;
		}
		target.alpha = Mathf.Clamp01(target.alpha + alpha * Time.deltaTime);
		if (infoPanel != null)
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				enableInfoPanel = !enableInfoPanel;
			}
			infoPanel.alpha = Mathf.Lerp(infoPanel.alpha, Mathf.Clamp01(enableInfoPanel ? alpha : 0f), Time.deltaTime * 10f);
		}
		if (lookAtCamera)
		{
			if (activeState)
			{
				targetRotation = Quaternion.LookRotation(activator.position - base.transform.position);
			}
			else
			{
				targetRotation = originRotation;
			}
			base.transform.rotation = Quaternion.Slerp(base.transform.rotation, targetRotation, Time.deltaTime);
		}
	}
}
