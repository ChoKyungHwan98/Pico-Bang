using System;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class AnimatorMoveEvent : UnityEvent<Vector3, Quaternion>
{
}
