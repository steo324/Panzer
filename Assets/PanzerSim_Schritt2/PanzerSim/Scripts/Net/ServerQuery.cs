using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Kleine Server-Abfrage fuer die Serverliste (UDP-Port 7778, getrennt vom Spiel-Port 7777):
    /// Der Server antwortet auf "PANZER?" mit Name, Karte, Spielerzahl und Version.
    /// Clients fragen bekannte Adressen direkt und das Heimnetz per Rundruf (Broadcast) - so taucht ein Server
    /// im selben Netzwerk von selbst in der Liste auf.
    /// </summary>
    public static class ServerQuery
    {
        public const int Port = 7778;
        const string Ask = "PANZER?", Answer = "PANZER!";

        [Serializable]
        public class Info
        {
            public string name = "", map = "", version = "";
            public string code = "";      // Relay-Beitrittscode (leer = direkt ueber Port verbinden)
            public int players, max, port;
        }

        // ================= Server: antworten =================
        static UdpClient responder;
        static Thread responderThread;
        static volatile string infoJson = "{}";
        static volatile bool running;

        public static void StartResponder()
        {
            if (running) return;
            try
            {
                responder = new UdpClient(new IPEndPoint(IPAddress.Any, Port));
                running = true;
                responderThread = new Thread(() =>
                {
                    var any = new IPEndPoint(IPAddress.Any, 0);
                    while (running)
                    {
                        try
                        {
                            byte[] data = responder.Receive(ref any);
                            if (Encoding.UTF8.GetString(data) != Ask) continue;
                            byte[] reply = Encoding.UTF8.GetBytes(Answer + infoJson);
                            responder.Send(reply, reply.Length, any);
                        }
                        catch (SocketException) { if (!running) break; }
                        catch (ObjectDisposedException) { break; }
                    }
                }) { IsBackground = true, Name = "Serverabfrage" };
                responderThread.Start();
                Debug.Log($"[Server] Serverliste: Abfrage auf UDP-Port {Port}");
            }
            catch (Exception e) { Debug.LogWarning($"[Server] Abfrage-Port {Port} nicht verfuegbar: {e.Message}"); running = false; }
        }

        public static void SetInfo(Info info) => infoJson = JsonUtility.ToJson(info);

        public static void StopResponder()
        {
            running = false;
            try { responder?.Close(); } catch { }
            responder = null;
        }

        // ================= Client: suchen =================
        public class Entry
        {
            public string address;
            public Info info;
            public float pingMs = -1f;
            public float lastSeen;        // Time.unscaledTime der letzten Antwort
            public bool manual;           // vom Spieler eingetragen oder aus der Liste
            public bool internet;         // aus Unity Lobby (oeffentlicher Internet-Server)
        }

        static UdpClient asker;
        static Thread askerThread;
        static readonly ConcurrentQueue<(string addr, string json, long ticks)> replies = new ConcurrentQueue<(string, string, long)>();
        static readonly ConcurrentDictionary<string, long> sentAt = new ConcurrentDictionary<string, long>();
        static long broadcastAt;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        static void EnsureAsker()
        {
            if (asker != null) return;
            asker = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
            askerThread = new Thread(() =>
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                while (asker != null)
                {
                    try
                    {
                        byte[] data = asker.Receive(ref from);
                        string s = Encoding.UTF8.GetString(data);
                        if (s.StartsWith(Answer)) replies.Enqueue((from.Address.ToString(), s.Substring(Answer.Length), clock.ElapsedTicks));
                    }
                    catch (SocketException) { }
                    catch (ObjectDisposedException) { break; }
                    catch (NullReferenceException) { break; }
                }
            }) { IsBackground = true, Name = "Serversuche" };
            askerThread.Start();
        }

        /// <summary>Bekannte Adressen direkt fragen und das Heimnetz per Rundruf. Alle paar Sekunden aufrufen.</summary>
        public static void Probe(IEnumerable<string> addresses)
        {
            try
            {
                EnsureAsker();
                byte[] ask = Encoding.UTF8.GetBytes(Ask);
                foreach (var a in addresses)
                {
                    if (string.IsNullOrWhiteSpace(a)) continue;
                    try
                    {
                        IPAddress ip;
                        if (!IPAddress.TryParse(a.Trim(), out ip))
                        {
                            var list = Dns.GetHostAddresses(a.Trim());
                            if (list.Length == 0) continue;
                            ip = Array.Find(list, x => x.AddressFamily == AddressFamily.InterNetwork) ?? list[0];
                        }
                        sentAt[ip.ToString()] = clock.ElapsedTicks;
                        asker.Send(ask, ask.Length, new IPEndPoint(ip, Port));
                    }
                    catch (Exception) { }
                }
                broadcastAt = clock.ElapsedTicks;
                foreach (var b in BroadcastAddresses())
                    try { asker.Send(ask, ask.Length, new IPEndPoint(b, Port)); } catch (Exception) { }
            }
            catch (Exception e) { Debug.LogWarning("Serversuche: " + e.Message); }
        }

        /// <summary>Eingegangene Antworten in die Liste uebernehmen (im Hauptthread aufrufen).</summary>
        public static void Collect(Dictionary<string, Entry> list)
        {
            while (replies.TryDequeue(out var r))
            {
                Info info;
                try { info = JsonUtility.FromJson<Info>(r.json); } catch { continue; }
                if (info == null) continue;
                if (!list.TryGetValue(r.addr, out var e)) { e = new Entry { address = r.addr }; list[r.addr] = e; }
                e.info = info;
                long sent = sentAt.TryGetValue(r.addr, out long t) ? t : broadcastAt;
                e.pingMs = (r.ticks - sent) * 1000f / System.Diagnostics.Stopwatch.Frequency;
                e.lastSeen = Time.unscaledTime;
            }
        }

        public static void StopSearch()
        {
            var a = asker; asker = null;
            try { a?.Close(); } catch { }
        }

        /// <summary>Rundruf-Adressen aller Netzwerkkarten (z. B. 192.168.2.255) plus 255.255.255.255.</summary>
        static List<IPAddress> BroadcastAddresses()
        {
            var result = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        byte[] ip = ua.Address.GetAddressBytes(), mask = ua.IPv4Mask.GetAddressBytes();
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                        var addr = new IPAddress(b);
                        if (!result.Contains(addr) && !IPAddress.IsLoopback(ua.Address)) result.Add(addr);
                    }
                }
            }
            catch (Exception) { }
            return result;
        }
    }
}
