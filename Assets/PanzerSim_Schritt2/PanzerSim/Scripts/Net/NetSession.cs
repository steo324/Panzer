using Unity.Netcode;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>Spielrunde im Mehrspieler: welche Karte, welcher Spielmodus, Eroberungs-Stand und das Trefferprotokoll fuer alle.</summary>
    public class NetSession : NetworkBehaviour
    {
        public static NetSession Instance { get; private set; }
        public readonly NetworkVariable<int> mapIndex = new NetworkVariable<int>(0);
        /// <summary>Spielmodus (NetSync.ModeFree, ModeConquest ...).</summary>
        public readonly NetworkVariable<int> gameMode = new NetworkVariable<int>(0);
        // Eroberung: Tickets (Blau*1000 + Rot), Besitzer der Flaggen + Sieger (je 3er-Stelle), Einnahme-Fortschritt A/B/C
        public readonly NetworkVariable<int> cqTickets = new NetworkVariable<int>(Conquest.StartTickets * 1000 + Conquest.StartTickets);
        public readonly NetworkVariable<int> cqState = new NetworkVariable<int>(13);   // alle neutral (1+3+9), kein Sieger (0)
        public readonly NetworkVariable<Vector3> cqProgress = new NetworkVariable<Vector3>(Vector3.zero);
        // Lage der Flaggen (vom Server, damit alle dieselben Plaetze haben)
        public readonly NetworkVariable<Vector3> cqFlagA = new NetworkVariable<Vector3>(Vector3.zero);
        public readonly NetworkVariable<Vector3> cqFlagB = new NetworkVariable<Vector3>(Vector3.zero);
        public readonly NetworkVariable<Vector3> cqFlagC = new NetworkVariable<Vector3>(Vector3.zero);

        public override void OnNetworkSpawn()
        {
            Instance = this;
            // Client: Karte jetzt bauen (Server/Host hat sie schon vor dem Start gebaut)
            if (!IsServer) GameManager.Instance?.BeginClientGame(mapIndex.Value, gameMode.Value);
        }

        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

        /// <summary>Protokollzeile vom Server an alle Clients.</summary>
        public void Log(string line) { if (IsServer && IsSpawned) LogRpc(line); }

        [Rpc(SendTo.NotServer)]
        void LogRpc(string line) => RangeLog.AddLocal(line);
    }
}
