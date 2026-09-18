using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 소음 전파 시스템.
///
/// 게임 내에서 "소리가 나는 사건"은 전부 이곳을 거친다.
/// 소리를 낸 쪽은 어떤 몬스터가 듣는지 알 필요가 없고,
/// 듣는 쪽은 누가 소리를 냈는지 알 필요가 없다.
///
/// 기획 의도: 플레이어의 진행 행동(사격)은 반드시 위험을 동반한다.
/// 한 소리에 반응하는 몬스터는 들은 개체 중 가까운 <see cref="MaxResponders"/>마리까지 — 전부 몰려오면 줄줄이가 된다.
/// </summary>
public enum NoiseKind
{
    /// <summary>총을 쐈다. 발생 위치 = 플레이어. 쏘는 순간 자기 위치가 노출된다.</summary>
    Shot = 0,

    /// <summary>과녁이 부서졌다. 발생 위치 = 과녁. 플레이어 위치와 다르다.</summary>
    TargetDestroyed = 1
}

public static class NoiseSystem
{
    public static bool DrawDebug = true;

    /// <summary>소리 한 번에 확인하러 오는 최대 마릿수.</summary>
    public const int MaxResponders = 2;

    private static readonly List<KeyValuePair<float, MonsterAI>> hearers = new List<KeyValuePair<float, MonsterAI>>();

    /// <summary>
    /// 소음을 발생시킨다. 들을 수 있는 몬스터 중 가까운 순서로 최대 <see cref="MaxResponders"/>마리가 반응한다.
    /// </summary>
    /// <param name="position">소리가 난 지점 (플레이어 위치가 아닐 수 있음)</param>
    /// <param name="radius">이 소리의 기본 도달 반경</param>
    public static void Emit(Vector3 position, float radius, NoiseKind kind)
    {
        if (radius <= 0f) { return; }

        hearers.Clear();
        var monsters = MonsterAI.activeMonsters;
        for (int i = monsters.Count - 1; i >= 0; i--)
        {
            MonsterAI m = monsters[i];
            if (m != null && m.CanHearNoise(position, radius, out float distance))
            {
                hearers.Add(new KeyValuePair<float, MonsterAI>(distance, m));
            }
        }
        hearers.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (int i = 0; i < hearers.Count && i < MaxResponders; i++)
        {
            hearers[i].Value.OnHearNoise(position, radius, kind);
        }

        if (DrawDebug)
        {
            Color c = (kind == NoiseKind.Shot) ? Color.yellow : new Color(1f, 0.5f, 0f);
            DrawDebugRing(position, radius, c, 1.5f);
        }
    }

    private static void DrawDebugRing(Vector3 center, float radius, Color color, float duration)
    {
        const int segments = 24;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Debug.DrawLine(prev, next, color, duration);
            prev = next;
        }
    }
}
