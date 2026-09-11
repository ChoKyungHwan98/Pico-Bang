using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DisplayAnim
{
	public class DisplayAnimation : MonoBehaviour
	{
		public AnimationClip animationClip;

		private Animator animator;

		private PlayableGraph playableGraph;

		private AnimationClipPlayable clipPlayable;

		private Vector3 initialPosition;

		private Quaternion initialRotation;

		private void Awake()
		{
			animator = GetComponent<Animator>();
			if (animationClip == null)
			{
				Debug.LogError("Animator or AnimationClip is not assigned!");
				return;
			}
			initialPosition = base.transform.position;
			initialRotation = base.transform.rotation;
			playableGraph = PlayableGraph.Create("AnimationGraph");
			PlayOneShot();
		}

		private IEnumerator ResetPositionRoutine(float duration)
		{
			yield return new WaitForSeconds(duration);
			base.transform.position = initialPosition;
			base.transform.rotation = initialRotation;
			playableGraph.Stop();
			PlayOneShot();
		}

		private void PlayOneShot()
		{
			AnimationPlayableOutput output = AnimationPlayableOutput.Create(playableGraph, "AnimationOutput", animator);
			clipPlayable = AnimationClipPlayable.Create(playableGraph, animationClip);
			clipPlayable.SetDuration(animationClip.length);
			output.SetSourcePlayable(clipPlayable);
			StartCoroutine(ResetPositionRoutine((float)clipPlayable.GetDuration()));
			playableGraph.Play();
		}

		private void OnDestroy()
		{
			playableGraph.Destroy();
		}
	}
}
