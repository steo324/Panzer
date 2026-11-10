using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>Erzeugt beim Start deinen Panzer und die Zielpanzer. N = neue Ziele, T = aufrichten.</summary>
    public class TankRangeSetup : MonoBehaviour
    {
        TankController player;
        int playerType = 0;
        static TankSpec PlayerSpec(int i)
        {
            switch (i % 4)
            {
                case 0: return TankLibrary.Tiger;
                case 1: return TankLibrary.PanzerIV;
                case 2: return TankLibrary.Sherman;
                default: return TankLibrary.T34_85;
            }
        }
        readonly List<TankController> targets = new List<TankController>();

        void Start()
        {
            Time.fixedDeltaTime = 0.01f; // genauere Physik (100x pro Sekunde)
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SpawnPlayer();
            SpawnTargets();
        }

        void SpawnPlayer()
        {
            if (player != null) Destroy(player.gameObject);
            player = TankBuilder.Build(PlayerSpec(playerType), new Vector3(0f, 0.3f, 0f), Quaternion.identity, true);
            var cam = Camera.main;
            if (cam != null)
            {
                var tc = cam.GetComponent<TankCamera>();
                if (tc == null) tc = cam.gameObject.AddComponent<TankCamera>();
                tc.target = player;
            }
        }

        void SpawnTargets()
        {
            foreach (var t in targets) if (t != null) Destroy(t.gameObject);
            targets.Clear();
            // Gegner schauen zu dir (180°), teils angewinkelt
            Add(TankLibrary.Sherman,  new Vector3(-15f, 0.3f, 300f), 180f);
            Add(TankLibrary.T34_85,   new Vector3(25f, 0.3f, 500f), 150f);
            Add(TankLibrary.PanzerIV, new Vector3(45f, 0.3f, 200f), 270f); // Seite zu dir
            Add(TankLibrary.Sherman,  new Vector3(-60f, 0.3f, 800f), 210f);
            Add(TankLibrary.Tiger,    new Vector3(-40f, 0.3f, 150f), 135f);
        }

        void TestShot(bool side)
        {
            TankController tiger = null;
            foreach (var t in targets) if (t != null && t.spec.name == "Tiger I") tiger = t;
            if (tiger == null || player == null) return;
            Transform tt = tiger.transform;
            Vector3 aim = tt.position + tt.up * Random.Range(0.8f, 2.2f) + tt.right * Random.Range(-0.6f, 0.6f);
            Vector3 from = side ? aim + tt.right * 300f : aim + tt.forward * 300f;
            from += Vector3.up * 0.5f;
            Shell.Fire(player.Ammo, from, (aim - from).normalized);
        }

        void Add(TankSpec s, Vector3 pos, float yaw)
        {
            targets.Add(TankBuilder.Build(s, pos, Quaternion.Euler(0f, yaw, 0f), false));
        }

        void Update()
        {
            // alte Testszene: Esc gibt die Maus frei (im neuen Spiel macht das das Pausemenue)
            if (Inp.Down(KeyCode.Escape))
            {
                bool locked = Cursor.lockState == CursorLockMode.Locked;
                Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = locked;
            }
            if (Inp.Down(KeyCode.N))
            {
                if (player == null || player.destroyed) SpawnPlayer();
                SpawnTargets();
                RangeLog.Lines.Clear();
                foreach (var go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (go.name == "Treffer") Destroy(go);
            }
            if (Inp.Down(KeyCode.T) && player != null) player.ResetUpright();
            if (Inp.Down(KeyCode.B) && player != null) player.Teleport(new Vector3(-45f, 0.3f, 18f), 0f); // vor die steile Rampe
            // Testschuesse auf den Ziel-Tiger (K = von vorne, L = von der Seite)
            if (Inp.Down(KeyCode.K) || Inp.Down(KeyCode.L)) TestShot(Inp.Down(KeyCode.L));
            if (Inp.Down(KeyCode.P))
            {
                playerType++;
                SpawnPlayer();
                RangeLog.Add("Du faehrst jetzt: " + player.spec.name);
            }
        }
    }
}
