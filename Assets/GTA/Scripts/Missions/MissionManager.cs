using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Prowadzi wszystkie misje (tylko serwer). Wystarczy jeden w scenie.
    /// Misje definiujesz komponentem Mission na mapie – ten skrypt je znajduje automatycznie.
    /// </summary>
    public class MissionManager : MonoBehaviour
    {
        [Tooltip("Prefab wroga dla kroku KillEnemies (CombatNpcAI z frakcją Gang)")]
        public GameObject enemyPrefab;
        [Tooltip("Prefab auta dla kroków ze spawnVehicleAtTarget")]
        public GameObject vehiclePrefab;

        class ActiveMission
        {
            public Mission def;
            public readonly List<PlayerController> players = new List<PlayerController>();
            public int stepIndex;
            public float stepStartTime;
            public float surviveEnd;
            public readonly List<Health> enemies = new List<Health>();
            public Vehicle spawnedVehicle;
            public bool[] cpDone; public int wave; public bool talkStarted;
            public string lastText; public int lastSeconds = -2; public Vector3? lastTarget;
            public MissionStep Step => def.steps[stepIndex];
        }

        readonly List<ActiveMission> _active = new List<ActiveMission>();
        readonly Dictionary<ulong, HashSet<Mission>> _completed = new Dictionary<ulong, HashSet<Mission>>();
        readonly Dictionary<ulong, float> _cooldownUntil = new Dictionary<ulong, float>();
        Mission[] _missions;

        void Start() => _missions = FindObjectsByType<Mission>(FindObjectsSortMode.None);

        void Update()
        {
            if (!GameUtil.IsServerRunning) { _active.Clear(); return; }
            // wydajność: sprawdzanie startu misji 5×/s zamiast co klatkę, a kroki aktywnych misji 10×/s
            if (Time.time >= _nextStartCheck) { _nextStartCheck = Time.time + 0.2f; CheckMissionStarts(); }
            if (Time.time >= _nextTick) { _nextTick = Time.time + 0.1f; for (int i = _active.Count - 1; i >= 0; i--) Tick(_active[i]); }
        }

        float _nextStartCheck, _nextTick;

        // ================= START =================

        void CheckMissionStarts()
        {
            foreach (var m in _missions)
            {
                if (m == null || m.steps.Length == 0 || IsRunning(m) || !m.Available) continue;
                foreach (var p in PlayerController.All)
                {
                    if (!CanJoin(p)) continue;
                    if (_cooldownUntil.TryGetValue(p.OwnerClientId, out var cd) && Time.time < cd) continue;
                    if (FlatDistance(p.transform.position, m.transform.position) > m.startRadius) continue;
                    if (!m.repeatable && HasCompleted(p, m)) continue;
                    StartMission(m, p);
                    break;
                }
            }
        }

        static bool CanJoin(PlayerController p) => p != null && !p.IsDead && p.Mission != null && !p.Mission.InMission;

        bool IsRunning(Mission m)
        {
            foreach (var a in _active) if (a.def == m) return true;
            return false;
        }

        bool HasCompleted(PlayerController p, Mission m) =>
            _completed.TryGetValue(p.OwnerClientId, out var set) && set.Contains(m);

        void StartMission(Mission m, PlayerController initiator)
        {
            var am = new ActiveMission { def = m };
            foreach (var p in PlayerController.All)
                if (CanJoin(p) && (p == initiator || Vector3.Distance(p.transform.position, m.transform.position) < m.coopJoinRadius))
                    am.players.Add(p);

            foreach (var p in am.players) p.Mission.SetMission(m.missionName);
            DialogueUI.Play(m.intro, true);   // katscena ze sceną 3D i animowanymi postaciami
            _active.Add(am);
            StartStep(am, 0);
        }

        // ================= KROKI =================

        void StartStep(ActiveMission am, int index)
        {
            am.stepIndex = index;
            if (index >= am.def.steps.Length) { Complete(am); return; }

            var s = am.Step;
            am.stepStartTime = Time.time;
            am.enemies.Clear();

            if (s.setWantedOnStart > 0)
                foreach (var p in am.players) p.Wanted.SetAtLeast(s.setWantedOnStart);

            var carPrefab = s.vehicleOverride != null ? s.vehicleOverride : vehiclePrefab;
            if (s.spawnVehicleAtTarget && carPrefab != null && s.target != null)
                am.spawnedVehicle = GameUtil.SpawnNetworked(carPrefab, s.target.position + Vector3.up * 0.5f, s.target.rotation).GetComponent<Vehicle>();

            am.wave = 1; am.talkStarted = false;
            am.cpDone = s.type == MissionStepType.Checkpoints && s.points != null ? new bool[s.points.Length] : null;
            if (s.type == MissionStepType.KillEnemies) SpawnEnemies(am, s);

            if (s.type == MissionStepType.Survive) am.surviveEnd = Time.time + s.duration;
            UpdateObjective(am);
        }

        void SpawnEnemies(ActiveMission am, MissionStep s)
        {
            if (enemyPrefab == null || s.target == null) return;
            for (int i = 0; i < s.enemyCount; i++)
            {
                if (!GameUtil.RandomNavPoint(s.target.position, 1f, Mathf.Max(3f, s.radius), out var pos)) pos = s.target.position;
                var no = GameUtil.SpawnNetworked(enemyPrefab, pos, Quaternion.Euler(0, Random.Range(0, 360), 0));
                var ai = no.GetComponent<CombatNpcAI>();
                if (ai != null) foreach (var p in am.players) ai.HostileTo.Add(p.OwnerClientId);
                am.enemies.Add(no.GetComponent<Health>());
            }
        }

        void Tick(ActiveMission am)
        {
            am.players.RemoveAll(p => p == null || !p.IsSpawned);
            if (am.players.Count == 0) { Finish(am, false, "Brak graczy"); return; }

            bool allDead = true;
            foreach (var p in am.players) if (!p.IsDead) allDead = false;
            if (allDead) { Fail(am, "Wszyscy uczestnicy zginęli"); return; }

            var s = am.Step;
            if (s.timeLimit > 0f && Time.time - am.stepStartTime > s.timeLimit) { Fail(am, "Koniec czasu"); return; }
            if (am.spawnedVehicle != null && am.spawnedVehicle.Health.Dead &&
                (s.type == MissionStepType.EnterVehicle || s.type == MissionStepType.DriveTo))
            { Fail(am, "Pojazd zniszczony"); return; }

            if (IsStepDone(am, s)) StartStep(am, am.stepIndex + 1);
            else UpdateObjective(am);
        }

        bool IsStepDone(ActiveMission am, MissionStep s)
        {
            switch (s.type)
            {
                case MissionStepType.GoTo:
                    if (s.target == null) return true;
                    foreach (var p in am.players)
                        if (!p.IsDead && FlatDistance(p.transform.position, s.target.position) < s.radius) return true;
                    return false;

                case MissionStepType.DriveTo:
                    if (s.target == null) return true;
                    foreach (var p in am.players)
                    {
                        var v = p.Vehicles.CurrentVehicle;
                        if (v == null) continue;
                        if (am.spawnedVehicle != null && v != am.spawnedVehicle) continue;
                        if (FlatDistance(v.transform.position, s.target.position) < s.radius) return true;
                    }
                    return false;

                case MissionStepType.EnterVehicle:
                    foreach (var p in am.players)
                    {
                        var v = p.Vehicles.CurrentVehicle;
                        if (v != null && (am.spawnedVehicle == null || v == am.spawnedVehicle)) return true;
                    }
                    return false;

                case MissionStepType.KillEnemies:
                    foreach (var e in am.enemies) if (e != null && e.IsSpawned && !e.Dead) return false;
                    if (am.wave < s.waves) { am.wave++; am.enemies.Clear(); SpawnEnemies(am, s); return false; }
                    return true;

                case MissionStepType.Checkpoints:
                {
                    if (s.points == null || s.points.Length == 0) return true;
                    bool all = true;
                    for (int i = 0; i < s.points.Length; i++)
                    {
                        if (am.cpDone[i]) continue;
                        if (s.points[i] != null)
                            foreach (var p in am.players)
                            {
                                if (p.IsDead) continue;
                                var v = p.Vehicles.CurrentVehicle;
                                Vector3 pos = v != null ? v.transform.position : p.transform.position;
                                if (FlatDistance(pos, s.points[i].position) < s.radius) { am.cpDone[i] = true; break; }
                            }
                        else am.cpDone[i] = true;
                        if (!am.cpDone[i]) all = false;
                    }
                    return all;
                }

                case MissionStepType.Talk:
                case MissionStepType.Dialogue:
                    if (!am.talkStarted)
                    {
                        bool near = s.type == MissionStepType.Dialogue || s.target == null;
                        if (!near)
                            foreach (var p in am.players)
                                if (!p.IsDead && FlatDistance(p.transform.position, s.target.position) < s.radius) { near = true; break; }
                        if (near) { am.talkStarted = true; DialogueUI.Play(s.lines, true); }
                        return false;
                    }
                    return !DialogueUI.Active;

                case MissionStepType.LoseWanted:
                    foreach (var p in am.players) if (p.Wanted.Level > 0) return false;
                    return true;

                case MissionStepType.Survive:
                    return Time.time >= am.surviveEnd;
            }
            return true;
        }

        void UpdateObjective(ActiveMission am)
        {
            var s = am.Step;
            Vector3? target = null;
            string text = s.objective;

            switch (s.type)
            {
                case MissionStepType.GoTo:
                case MissionStepType.DriveTo:
                    if (s.target != null) target = s.target.position;
                    break;
                case MissionStepType.EnterVehicle:
                    if (am.spawnedVehicle != null) target = am.spawnedVehicle.transform.position;
                    break;
                case MissionStepType.Talk:
                    if (s.target != null && !am.talkStarted) target = s.target.position;
                    break;
                case MissionStepType.Checkpoints:
                {
                    if (s.points == null || am.cpDone == null) break;
                    int done = 0, total = s.points.Length;
                    float best = float.MaxValue;
                    Vector3 from = am.players.Count > 0 ? am.players[0].transform.position : Vector3.zero;
                    for (int i = 0; i < total; i++)
                    {
                        if (am.cpDone[i]) { done++; continue; }
                        if (s.points[i] == null) continue;
                        float d = (s.points[i].position - from).sqrMagnitude;
                        if (d < best) { best = d; target = s.points[i].position; }
                    }
                    text += $" ({done}/{total})";
                    break;
                }
                case MissionStepType.KillEnemies:
                    int alive = 0;
                    Vector3 any = Vector3.zero;
                    foreach (var e in am.enemies) if (e != null && e.IsSpawned && !e.Dead) { alive++; any = e.transform.position; }
                    if (alive > 0) target = any;
                    text += s.waves > 1 ? $" (fala {am.wave}/{s.waves}, {alive} pozostało)" : $" ({alive} pozostało)";
                    break;
            }

            int secondsLeft = -1;
            if (s.type == MissionStepType.Survive) secondsLeft = Mathf.CeilToInt(am.surviveEnd - Time.time);
            else if (s.timeLimit > 0f) secondsLeft = Mathf.CeilToInt(s.timeLimit - (Time.time - am.stepStartTime));

            // wydajność: do graczy wysyłamy tylko zmiany (sekundy i pozycja celu zmieniają się rzadko)
            bool same = text == am.lastText && secondsLeft == am.lastSeconds && target == am.lastTarget;
            if (same) return;
            am.lastText = text; am.lastSeconds = secondsLeft; am.lastTarget = target;
            foreach (var p in am.players) p.Mission.SetObjective(text, target, secondsLeft);
        }

        // ================= KONIEC =================

        void Complete(ActiveMission am)
        {
            Story.Set("done:" + am.def.missionName);
            Story.Set(am.def.setsFlag);
            Story.PayDebt(am.def.debtPayment);
            bool finale = am.def.setsFlag == "a3_4";
            DialogueUI.Play(am.def.outro, true);
            foreach (var p in am.players)
            {
                if (finale)
                {
                    if (Story.Debt <= 0) p.Stats.TriggerEnding();
                    else DialogueUI.Play(new[] { new DialogueLine("Narrator", $"Wszystkie zadania wykonane. Rico pozwala Ci teraz spłacić cały dług – zostało ${Story.Debt:N0}. Plecak (I) → Spłata długu.") });
                }
                p.Stats.AddMoney(am.def.moneyReward);
                if (!_completed.TryGetValue(p.OwnerClientId, out var set)) _completed[p.OwnerClientId] = set = new HashSet<Mission>();
                set.Add(am.def);
            }
            Finish(am, true, "");
        }

        void Fail(ActiveMission am, string reason)
        {
            // usuń pozostałych wrogów misji
            foreach (var e in am.enemies)
                if (e != null && e.IsSpawned && !e.Dead) e.NetworkObject.Despawn(true);
            Finish(am, false, reason);
        }

        void Finish(ActiveMission am, bool passed, string reason)
        {
            var fs = new FixedString128Bytes();
            fs.CopyFromTruncated(reason);
            foreach (var p in am.players)
            {
                if (p == null || !p.IsSpawned) continue;
                _cooldownUntil[p.OwnerClientId] = Time.time + 10f; // żeby misja nie odpaliła się od razu ponownie
                p.Mission.ClearMission();
                p.Mission.ShowResultRpc(passed, am.def.moneyReward, fs);
            }
            _active.Remove(am);
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0; b.y = 0;
            return Vector3.Distance(a, b);
        }
    }
}
