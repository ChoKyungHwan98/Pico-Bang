using System;

namespace RPGCharacterAnims.Actions
{
	public abstract class BaseActionHandler<TContext> : IActionHandler
	{
		public bool active;

		public event Action OnStart = () =>
		{
		};

		public event Action OnEnd = () =>
		{
		};

		public abstract bool CanStartAction(RPGCharacterController controller);

		public virtual void StartAction(RPGCharacterController controller, object context)
		{
			if (CanStartAction(controller))
			{
				active = true;
				_StartAction(controller, (TContext)context);
				OnStart();
			}
		}

		public virtual void AddStartListener(Action callback)
		{
			OnStart += callback;
		}

		public virtual void RemoveStartListener(Action callback)
		{
			OnStart -= callback;
		}

		public virtual bool IsActive()
		{
			return active;
		}

		protected abstract void _StartAction(RPGCharacterController controller, TContext context);

		public abstract bool CanEndAction(RPGCharacterController controller);

		public virtual void EndAction(RPGCharacterController controller)
		{
			if (CanEndAction(controller))
			{
				active = false;
				_EndAction(controller);
				OnEnd();
			}
		}

		public virtual void AddEndListener(Action callback)
		{
			OnEnd += callback;
		}

		public virtual void RemoveEndListener(Action callback)
		{
			OnEnd -= callback;
		}

		protected abstract void _EndAction(RPGCharacterController controller);
	}
}
