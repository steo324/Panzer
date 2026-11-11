using Unity.Netcode;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>Spielrunde im Mehrspieler: welche Karte, und das Trefferprotokoll fuer alle.</summary>
    public class NetSession : NetworkBehaviour
    {
        public static NetSession Instance { get; private set; }
        public readonly NetworkVariable<int> mapIndex = new NetworkVariable<int>(0);

        public override void OnNetworkSpawn()
        {
            Instance = this;
            // Client: Karte jetzt bauen (Server/Host hat sie schon vor dem Start gebaut)
            if (!IsServer) GameManager.Instance?.BeginClientGame(mapIndex.Value);
        }

        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

        /// <summary>Protokollzeile vom Server an alle Clients.</summary>
        public void Log(string line) { if (IsServer && IsSpawned) LogRpc(line); }

        [Rpc(SendTo.NotServer)]
        void LogRpc(string line) => RangeLog.AddLocal(line);
    }
}
