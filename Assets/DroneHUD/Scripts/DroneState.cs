using System;
using UnityEngine;

namespace DroneHud
{
    public enum DroneFlightState
    {
        Idle,
        TakingOff,
        Flying,
        Landing,
        Landed,
    }

    /// <summary>
    /// Snapshot of one drone's telemetry. Positions are in Unity space (Y up, metres).
    /// Crazyflie reports Z-up, so a real telemetry source must convert (x, y, z) -> (x, z, y).
    /// </summary>
    [Serializable]
    public class DroneState
    {
        // Crazyflie 1S LiPo: ~4.2 V full, ~3.0 V cut-off. Linear mapping is a rough estimate.
        public const float BatteryFullVoltage = 4.2f;
        public const float BatteryEmptyVoltage = 3.0f;

        public int Id;
        public string Name;
        public DroneFlightState State;
        public Vector3 Position;
        public Vector3 Velocity;
        public float YawDegrees;
        public float BatteryVoltage;
        public float LinkQuality; // 0-100
        public float LastUpdateTime; // Time.time of the last packet received

        public float BatteryPercent =>
            Mathf.Clamp01((BatteryVoltage - BatteryEmptyVoltage) / (BatteryFullVoltage - BatteryEmptyVoltage)) * 100f;

        public bool IsStale(float now, float timeout) => now - LastUpdateTime > timeout;
    }
}
