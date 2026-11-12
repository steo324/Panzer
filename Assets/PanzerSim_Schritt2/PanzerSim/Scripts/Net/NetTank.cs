using Unity.Netcode;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Ein Panzer im Mehrspieler. Jeder Rechner baut seinen eigenen TankController (Optik, Physik, Panzerung);
    /// dieses Objekt haelt ihn synchron:
    ///  - der Besitzer (Spieler bzw. Server bei Zielpanzern) faehrt/zielt und schreibt Lage, Tempo, Turm und Rohr,
    ///  - alle anderen folgen weich (ihr Panzer ist dort kinematisch),
    ///  - Schuesse laufen ueber den Server; Schaden, Brand, Zerstoerung, Ketten, Anbauteile und Marken
    ///    werden vom Server an alle Clients geschickt.
    /// </summary>
    public class NetTank : NetworkBehaviour
    {
        public readonly NetworkVariable<int> tankType = new NetworkVariable<int>(0);
        public readonly NetworkVariable<bool> isTarget = new NetworkVariable<bool>(false);

        readonly NetworkVariable<Vector3> netPos = new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<Quaternion> netRot = new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<Vector3> netVel = new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<float> netYaw = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<float> netElev = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> netMg = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>Turm-MG feuert gerade (fuer die Optik bei allen anderen).</summary>
        public bool MgFiring => netMg.Value;
        public void SetMg(bool on) { if (IsOwner && IsSpawned && netMg.Value != on) netMg.Value = on; }

        // Lenkrakete im Flug (der Schuetze lenkt, alle anderen sehen sie an dieser Stelle)
        readonly NetworkVariable<bool> netMissileOn = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<Vector3> netMissilePos = new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        GuidedMissile ghostMissile;

        public TankController tc;
        /// <summary>Lokaler Spielerpanzer (wir fahren ihn).</summary>
        public bool IsLocalPlayer => IsOwner && !isTarget.Value && !NetSync.Headless;

        public override void OnNetworkSpawn()
        {
            var spec = GameManager.TankSpecByIndex(tankType.Value);
            tc = TankBuilder.Build(spec, transform.position, transform.rotation, IsLocalPlayer);
            tc.net = this;
            if (!IsOwner) tc.rb.isKinematic = true;     // fremde Panzer folgen nur der uebertragenen Lage
            else WriteState();
            GameManager.Instance?.OnNetTankSpawned(this);
        }

        public override void OnNetworkDespawn()
        {
            GameManager.Instance?.OnNetTankDespawned(this);
            if (ghostMissile != null) { ghostMissile.Remove(); ghostMissile = null; }
            if (tc != null) Destroy(tc.gameObject);
        }

        void Update()
        {
            if (tc == null || !IsSpawned) return;
            if (IsOwner)
            {
                WriteState();
                var m = tc.activeMissile;
                if ((m != null) != netMissileOn.Value) netMissileOn.Value = m != null;
                if (m != null) netMissilePos.Value = m.transform.position;
                return;
            }
            // fremde Lenkrakete anzeigen
            if (netMissileOn.Value && !NetSync.Headless)
            {
                if (ghostMissile == null) ghostMissile = GuidedMissile.CreateGhost(tc, netMissilePos.Value);
                else ghostMissile.SetGhostTarget(netMissilePos.Value);
            }
            else if (ghostMissile != null) { ghostMissile.Remove(); ghostMissile = null; }

            // Fremder Panzer: weich zur uebertragenen Lage (bei grossem Sprung direkt)
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            Vector3 target = netPos.Value;
            Vector3 p = Vector3.Distance(tc.transform.position, target) > 6f ? target : Vector3.Lerp(tc.transform.position, target, k);
            Quaternion r = Quaternion.Slerp(tc.transform.rotation, netRot.Value, k);
            tc.transform.SetPositionAndRotation(p, r);
            tc.rb.position = p; tc.rb.rotation = r;
            tc.netVelocity = netVel.Value;
            if (tc.parts.turret != null && tc.parts.turret.parent != null)
                tc.parts.turret.localRotation = Quaternion.Slerp(tc.parts.turret.localRotation, Quaternion.Euler(0f, netYaw.Value, 0f), k);
            if (tc.parts.gunPivot != null)
                tc.parts.gunPivot.localRotation = Quaternion.Slerp(tc.parts.gunPivot.localRotation, Quaternion.Euler(-netElev.Value, 0f, 0f), k);
        }

        void WriteState()
        {
            netPos.Value = tc.transform.position;
            netRot.Value = tc.transform.rotation;
#if UNITY_6000_0_OR_NEWER
            netVel.Value = tc.rb.isKinematic ? Vector3.zero : tc.rb.linearVelocity;
#else
            netVel.Value = tc.rb.isKinematic ? Vector3.zero : tc.rb.velocity;
#endif
            if (tc.parts.turret != null) netYaw.Value = tc.parts.turret.localEulerAngles.y;
            if (tc.parts.gunPivot != null) netElev.Value = Mathf.DeltaAngle(0f, -tc.parts.gunPivot.localEulerAngles.x);
        }

        // ================= Schiessen =================
        /// <summary>Vom eigenen Panzer: Schuss beim Server anmelden (Host/Server schiesst direkt).</summary>
        public void RequestFire(int ammo, Vector3 origin, Vector3 dir, float speed = 0f)
        {
            if (IsServer) ServerFire(ammo, origin, dir, speed);
            else FireRpc(ammo, origin, dir, speed);
        }

        [Rpc(SendTo.Server)]
        void FireRpc(int ammo, Vector3 origin, Vector3 dir, float speed) => ServerFire(ammo, origin, dir, speed);

        void ServerFire(int ammo, Vector3 origin, Vector3 dir, float speed)
        {
            if (ammo < 0 || ammo >= AmmoLibrary.All.Length) return;
            Shell.Fire(AmmoLibrary.All[ammo], origin, dir, false, speed);   // echte Granate (Schaden zaehlt)
            if (!IsOwner && tc != null && !NetSync.Headless) tc.PlayFireEffects(ammo);   // Host sieht den Schuss des Clients
            FireVisualRpc(ammo, origin, dir, speed);
        }

        [Rpc(SendTo.NotServer)]
        void FireVisualRpc(int ammo, Vector3 origin, Vector3 dir, float speed)
        {
            if (IsOwner) return;                                            // Schuetze hat Muendungsfeuer und Geister-Granate schon
            if (tc != null) tc.PlayFireEffects(ammo);
            Shell.Fire(AmmoLibrary.All[ammo], origin, dir, true, speed);
        }

        /// <summary>Gefechtskopf einer Lenkrakete: Server startet die echte Hohlladung kurz vor dem Ziel (ohne Muendungsfeuer).</summary>
        public void RequestWarhead(int ammo, Vector3 origin, Vector3 dir)
        {
            if (IsServer) ServerWarhead(ammo, origin, dir);
            else { WarheadRpc(ammo, origin, dir); Shell.Fire(AmmoLibrary.All[ammo], origin, dir, true); }   // eigene Anzeige sofort
        }
        [Rpc(SendTo.Server)] void WarheadRpc(int ammo, Vector3 origin, Vector3 dir) => ServerWarhead(ammo, origin, dir);
        void ServerWarhead(int ammo, Vector3 origin, Vector3 dir)
        {
            if (ammo < 0 || ammo >= AmmoLibrary.All.Length) return;
            Shell.Fire(AmmoLibrary.All[ammo], origin, dir);
            WarheadVisualRpc(ammo, origin, dir);
        }
        [Rpc(SendTo.NotServer)]
        void WarheadVisualRpc(int ammo, Vector3 origin, Vector3 dir)
        {
            if (IsOwner) return;
            Shell.Fire(AmmoLibrary.All[ammo], origin, dir, true);
        }

        // ================= Ergebnisse vom Server =================
        public void SendModuleHp(int index, float hp) { if (index >= 0) ModuleHpRpc(index, hp); }
        [Rpc(SendTo.NotServer)]
        void ModuleHpRpc(int index, float hp)
        {
            if (tc == null || index >= tc.parts.modules.Count) return;
            tc.parts.modules[index].SetHpFromServer(hp);
        }

        public void SendModuleStun(int index, float seconds) { if (index >= 0) ModuleStunRpc(index, seconds); }
        [Rpc(SendTo.NotServer)]
        void ModuleStunRpc(int index, float seconds)
        {
            if (tc == null || index >= tc.parts.modules.Count) return;
            tc.parts.modules[index].stun = seconds;
        }

        public void SendDestroyed(string reason) => DestroyedRpc(reason);
        [Rpc(SendTo.NotServer)] void DestroyedRpc(string reason) { if (tc != null) tc.NetSetDestroyed(reason); }

        public void SendDetonate() => DetonateRpc();
        [Rpc(SendTo.NotServer)] void DetonateRpc() { if (tc != null) tc.NetDetonate(); }

        public void SendFire(bool on) => FireStateRpc(on);
        [Rpc(SendTo.NotServer)] void FireStateRpc(bool on) { if (tc != null) tc.NetFireState(on); }

        public void SendTrackBreak(Vector3 point, float damage, Vector3 blastFrom, bool explosive) => TrackBreakRpc(point, damage, blastFrom, explosive);
        [Rpc(SendTo.NotServer)]
        void TrackBreakRpc(Vector3 point, float damage, Vector3 blastFrom, bool explosive)
        {
            if (tc == null) return;
            NetSync.ApplyingServerState = true;
            try { tc.DamageTrack(point, damage, blastFrom, explosive); }
            finally { NetSync.ApplyingServerState = false; }
        }

        public void SendTrackRepaired() => TrackRepairedRpc();
        [Rpc(SendTo.NotServer)] void TrackRepairedRpc() { if (tc != null) tc.NetTrackRepaired(); }

        public void SendPartBreak(int index, Vector3 point, Vector3 dir, float speed) => PartBreakRpc(index, point, dir, speed);
        [Rpc(SendTo.NotServer)]
        void PartBreakRpc(int index, Vector3 point, Vector3 dir, float speed)
        {
            if (tc == null || index < 0 || index >= tc.destructibles.Count || tc.destructibles[index] == null) return;
            NetSync.ApplyingServerState = true;
            try { tc.destructibles[index].Break(point, dir, speed); }
            finally { NetSync.ApplyingServerState = false; }
        }

        public void SendDecal(byte kind, byte sel, Vector3 lp, Vector3 ln, Vector3 ld, float cal, float angle, float size)
            => DecalRpc(kind, sel, lp, ln, ld, cal, angle, size);
        [Rpc(SendTo.NotServer)]
        void DecalRpc(byte kind, byte sel, Vector3 lp, Vector3 ln, Vector3 ld, float cal, float angle, float size)
        {
            if (tc == null) return;
            Transform rf = tc.DecalRef(sel);
            Decals.AddLocal((Decals.Kind)kind, rf.TransformPoint(lp), rf.TransformDirection(ln), rf.TransformDirection(ld), cal, angle, tc.DecalParent(sel), size);
        }

        // ================= Wuensche des Besitzers an den Server =================
        public void RequestExtinguish() { if (IsServer) tc.Extinguish(); else ExtinguishRpc(); }
        [Rpc(SendTo.Server)] void ExtinguishRpc() { if (tc != null) tc.Extinguish(); }

        public void ReportRepairDone() { if (IsServer) { tc.NetTrackRepaired(); SendTrackRepaired(); } else RepairDoneRpc(); }
        [Rpc(SendTo.Server)] void RepairDoneRpc() { if (tc != null) { tc.NetTrackRepaired(); SendTrackRepaired(); } }

        public void RequestRespawn() => RespawnRpc();
        [Rpc(SendTo.Server)]
        void RespawnRpc()
        {
            if (isTarget.Value) return;
            ulong owner = OwnerClientId;
            int type = tankType.Value;
            NetworkObject.Despawn(true);
            NetSync.SpawnPlayerTank(owner, type);
        }
    }
}
