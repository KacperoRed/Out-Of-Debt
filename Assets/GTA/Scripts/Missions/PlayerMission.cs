using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Stan misji widoczny dla gracza (cel, znacznik, czas). Zapisuje serwer (MissionManager),
    /// czyta HUD. Rysuje też lokalny znacznik celu (słup światła).
    /// </summary>
    public class PlayerMission : NetworkBehaviour
    {
        public readonly NetworkVariable<FixedString64Bytes> MissionName = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<FixedString128Bytes> Objective = new NetworkVariable<FixedString128Bytes>();
        public readonly NetworkVariable<bool> HasTarget = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<Vector3> TargetPos = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<int> SecondsLeft = new NetworkVariable<int>(-1);

        public bool InMission => MissionName.Value.Length > 0;

        GameObject _beacon;

        public override void OnNetworkSpawn()
        {
            if (IsOwner) MissionName.OnValueChanged += OnMissionChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner) MissionName.OnValueChanged -= OnMissionChanged;
            if (_beacon != null) Destroy(_beacon);
        }

        void OnMissionChanged(FixedString64Bytes oldV, FixedString64Bytes newV)
        {
            bool inMission = newV.Length > 0;
            foreach (var m in FindObjectsByType<Mission>(FindObjectsSortMode.None)) m.SetMarkerVisible(!inMission);
            if (inMission) HUD.ShowBigMessage(newV.ToString(), new Color(1f, 0.85f, 0.2f), 3f);
        }

        void Update()
        {
            if (!IsOwner) return;
            bool show = InMission && HasTarget.Value;
            if (show && _beacon == null) _beacon = CreateBeacon();
            if (_beacon != null)
            {
                _beacon.SetActive(show);
                if (show) _beacon.transform.position = TargetPos.Value;
            }
        }

        static GameObject CreateBeacon()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "ObjectiveBeacon";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(3f, 40f, 3f);
            var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.9f, 0.1f, 0.25f) };
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ---------- API serwera ----------

        public void SetMission(string missionName)
        {
            if (!IsServer) return;
            MissionName.Value = Truncate64(missionName);
        }

        public void SetObjective(string text, Vector3? target, int secondsLeft = -1)
        {
            if (!IsServer) return;
            var fs = new FixedString128Bytes();
            fs.CopyFromTruncated(text ?? "");
            Objective.Value = fs;
            HasTarget.Value = target.HasValue;
            if (target.HasValue) TargetPos.Value = target.Value;
            SecondsLeft.Value = secondsLeft;
        }

        public void ClearMission()
        {
            if (!IsServer) return;
            MissionName.Value = default;
            Objective.Value = default;
            HasTarget.Value = false;
            SecondsLeft.Value = -1;
        }

        static FixedString64Bytes Truncate64(string s)
        {
            var fs = new FixedString64Bytes();
            fs.CopyFromTruncated(s ?? "");
            return fs;
        }

        [Rpc(SendTo.Owner)]
        public void ShowResultRpc(bool passed, int reward, FixedString128Bytes reason)
        {
            if (passed)
            {
                HUD.ShowBigMessage("MISJA ZALICZONA", new Color(1f, 0.85f, 0.2f), 4f);
                HUD.Notify($"Nagroda: ${reward}", Color.green);
            }
            else
            {
                HUD.ShowBigMessage("MISJA NIEUDANA", new Color(0.9f, 0.2f, 0.2f), 4f);
                if (reason.Length > 0) HUD.Notify(reason.ToString(), Color.red);
            }
        }
    }
}
