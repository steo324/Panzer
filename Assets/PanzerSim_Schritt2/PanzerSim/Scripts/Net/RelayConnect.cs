using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Spielen ohne Tailscale/Portfreigabe: Unity Relay leitet die Verbindung ueber einen Unity-Server um.
    /// Der Host bekommt einen Beitrittscode (z. B. K7X2QP), Freunde geben ihn ein. Anmeldung anonym, kein Konto noetig.
    /// </summary>
    public static class RelayConnect
    {
        const string ConnType = "dtls";   // verschluesselt

        static async Task Init()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var opt = new InitializationOptions();
                // Mehrere Spiele auf einem PC (Test): eigenes Profil, sonst gleiche Spieler-ID
                var args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-profile");
                if (i >= 0 && i + 1 < args.Length) opt.SetProfile(args[i + 1]);
                await UnityServices.InitializeAsync(opt);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        static RelayServerEndpoint Pick(List<RelayServerEndpoint> eps)
        {
            foreach (var e in eps) if (e.ConnectionType == ConnType) return e;
            throw new System.Exception("Relay: kein DTLS-Endpunkt");
        }

        /// <summary>Host: Platz fuer maxClients Mitspieler reservieren, Transport einstellen, Beitrittscode liefern.</summary>
        public static async Task<string> Host(int maxClients)
        {
            await Init();
            Allocation a = await RelayService.Instance.CreateAllocationAsync(maxClients);
            string code = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);
            var ep = Pick(a.ServerEndpoints);
            Data = new RelayServerData(ep.Host, (ushort)ep.Port, a.AllocationIdBytes, a.ConnectionData, a.ConnectionData, a.Key, ep.Secure);
            return code;
        }

        /// <summary>Client: mit Beitrittscode verbinden (Transport einstellen).</summary>
        public static async Task Join(string code)
        {
            await Init();
            JoinAllocation j = await RelayService.Instance.JoinAllocationAsync(code.Trim().ToUpperInvariant());
            var ep = Pick(j.ServerEndpoints);
            Data = new RelayServerData(ep.Host, (ushort)ep.Port, j.AllocationIdBytes, j.ConnectionData, j.HostConnectionData, j.Key, ep.Secure);
        }

        public static RelayServerData Data;

        /// <summary>Verstaendliche Fehlermeldung.</summary>
        public static string Explain(System.Exception e)
        {
            while (e is System.AggregateException && e.InnerException != null) e = e.InnerException;
            Debug.LogWarning("[Relay] " + e);
            string m = e.Message ?? "";
            if (e is RelayServiceException rs && (rs.Reason == RelayExceptionReason.JoinCodeNotFound || rs.Reason == RelayExceptionReason.InvalidRequest))
                return "Code ungueltig oder Spiel beendet.";
            if (m.Contains("project") || m.Contains("Project") || m.Contains("403") || m.Contains("Forbidden"))
                return "Relay nicht freigeschaltet (Unity Cloud: Relay aktivieren).";
            return "Online-Verbindung fehlgeschlagen: " + (m.Length > 120 ? m.Substring(0, 120) : m);
        }
    }
}
