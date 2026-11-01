using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace SuspensaoAjustavel
{
    // A roda visual. VPWheelCollider.UpdateVisualWheel (desmontado) mede o chão com um raio que
    // sai de TransformPoint(wc.center), mas posiciona suspensão/pinça/roda a partir de
    // transform.position, sem somar o center. Então os H que o mod tira do center.y aparecem na
    // roda desenhada: ela sobe junto com a carroceria (flutua) ou afunda no chão. Aqui, depois do
    // VPP (Ganchos.DepoisDaRodaVisual), cada peça posicionada por ele volta H ao longo do "up" da roda.
    internal static class Visual
    {
        internal static readonly Dictionary<IntPtr, Roda> Mapa = new();

        internal static void DepoisDaRoda(VPWheelCollider __instance)
        {
            try
            {
                if (Mapa.Count == 0 || !Mapa.TryGetValue(__instance.Pointer, out var r) || r.H == 0f || !r.Escreveu) return;
                var desloc = __instance.transform.up * -r.H;
                var susp = __instance.suspensionTransform;
                var pinca = __instance.caliperTransform;
                var roda = __instance.wheelTransform;
                bool temSusp = susp != null;
                if (temSusp) susp.position += desloc;
                if (pinca != null && !(temSusp && __instance.m_isCaliperChildOfSuspension)) pinca.position += desloc;
                if (roda != null && !(temSusp && __instance.m_isWheelChildOfSuspension) && !(pinca != null && __instance.m_isWheelChildOfCaliper))
                    roda.position += desloc;
            }
            catch (Exception e) { Log.Erro("Visual", e); }
        }
    }
}
