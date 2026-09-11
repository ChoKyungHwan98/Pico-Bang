using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RPGCharacterAnims
{
	public class CoroutineQueue
	{
		private readonly uint maxActive;

		private readonly Func<IEnumerator, Coroutine> coroutineStarter;

		private readonly Queue<IEnumerator> queue;

		private uint numActive;

		public CoroutineQueue(uint maxActive, Func<IEnumerator, Coroutine> coroutineStarter)
		{
			if (maxActive == 0)
			{
				throw new ArgumentException("Must be at least one", "maxActive");
			}
			this.maxActive = maxActive;
			this.coroutineStarter = coroutineStarter;
			queue = new Queue<IEnumerator>();
		}

		public void Run(IEnumerator coroutine)
		{
			if (numActive < maxActive)
			{
				IEnumerator arg = CoroutineRunner(coroutine);
				coroutineStarter(arg);
			}
			else
			{
				queue.Enqueue(coroutine);
			}
		}

		public void RunCallback(Action callback)
		{
			Run(CoroutineCallback(callback));
		}

		private IEnumerator CoroutineCallback(Action callback)
		{
			callback();
			yield return null;
		}

		private IEnumerator CoroutineRunner(IEnumerator coroutine)
		{
			numActive++;
			while (coroutine.MoveNext())
			{
				yield return coroutine.Current;
			}
			numActive--;
			if (queue.Count > 0)
			{
				IEnumerator coroutine2 = queue.Dequeue();
				Run(coroutine2);
			}
		}
	}
}
