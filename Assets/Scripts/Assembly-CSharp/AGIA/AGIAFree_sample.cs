using UnityEngine;
using UnityEngine.EventSystems;

namespace AGIA
{
	public class AGIAFree_sample : MonoBehaviour
	{
		public Animator animator;

		public int animBase;

		public int animLayer;

		private void Start()
		{
			animator = GetComponent<Animator>();
			animator.SetInteger("animBaseInt", 1);
		}

		private void Update()
		{
		}

		public void animBaseChange()
		{
			animator.SetInteger("animOtherInt", 0);
			switch (EventSystem.current.currentSelectedGameObject.name)
			{
			case "Generic_01":
				animator.SetInteger("animBaseInt", 1);
				break;
			case "Angry_01":
				animator.SetInteger("animBaseInt", 2);
				break;
			case "Brave_01":
				animator.SetInteger("animBaseInt", 3);
				break;
			case "Calm_01":
				animator.SetInteger("animBaseInt", 4);
				break;
			case "Concern_01":
				animator.SetInteger("animBaseInt", 5);
				break;
			case "Energetic_01":
				animator.SetInteger("animBaseInt", 6);
				break;
			case "Energetic_02":
				animator.SetInteger("animBaseInt", 7);
				break;
			case "Pitiable_01":
				animator.SetInteger("animBaseInt", 8);
				break;
			case "Surprised_01":
				animator.SetInteger("animBaseInt", 9);
				break;
			}
		}

		public void animLayerChange()
		{
			switch (EventSystem.current.currentSelectedGameObject.name)
			{
			case "Reset":
				animator.Play("Layer_start", 1, 0f);
				break;
			case "LookAway_01":
				animator.Play("Layer_look_away", 1, 0f);
				break;
			case "NoddingOnce_01":
				animator.Play("Layer_nodding_once", 1, 0f);
				break;
			case "SwingingBody_01":
				animator.Play("Layer_swinging_body", 1, 0f);
				break;
			}
		}

		public void animOtherChange()
		{
			animator.SetInteger("animBaseInt", 0);
			string text = EventSystem.current.currentSelectedGameObject.name;
			if (text == "walking_01")
			{
				animator.SetInteger("animOtherInt", 1);
			}
			else if (text == "WavingArm_01")
			{
				animator.SetInteger("animOtherInt", 2);
			}
		}
	}
}
