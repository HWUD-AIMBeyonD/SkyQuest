using System.Collections.Generic;
using UnityEngine;

namespace DroneHud
{
    /// <summary>
    /// Anything that can feed drone telemetry to the HUD. The HUD only talks to this type,
    /// so the simulator can later be swapped for a network source (e.g. a PC running cflib).
    /// </summary>
    public abstract class DroneTelemetrySource : MonoBehaviour
    {
        public abstract string SourceName { get; }
        public abstract IReadOnlyList<DroneState> Drones { get; }
    }
}
