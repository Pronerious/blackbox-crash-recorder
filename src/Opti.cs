using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BlackBox
{
    // ---------------------------------------------------------------- Un gioco installato
    public class GameInfo
    {
        public string Name = "", Store = "", Root = "", ExeDir = "", Exe = "";
        public bool Dlss, DlssFg, Xess, Fsr31, Fsr31Vk, Fsr2;
        public string AntiCheat = "";             // "" = nessuno trovato
        public string OptiVersion = "", OptiProxy = "";
        public bool OptiByBlackBox, OptiByHand;
        public bool Vulkan;
        public bool Is64 = true;   // OptiScaler funziona solo con giochi a 64 bit
        public bool Blocked { get { return AntiCheat != "" || !Is64; } }

        public string Upscalers
        {
            get
            {
                var l = new List<string>();
                if (Dlss) l.Add("DLSS" + (DlssFg ? " + FG" : ""));
                if (Fsr31 || Fsr31Vk) l.Add("FSR 3.1");
                if (Fsr2) l.Add("FSR 2");
                if (Xess) l.Add("XeSS");
                return l.Count == 0 ? "nessuno riconosciuto" : string.Join(", ", l);
            }
        }

        public string OptiState
        {
            get
            {
                if (OptiByBlackBox) return "installato (" + OptiVersion + ", " + OptiProxy + ")";
                if (OptiByHand) return "installato a mano (" + OptiProxy + ")";
                return "non installato";
            }
        }

        // consiglio in parole semplici, adattato alla scheda video
        public string Advice
        {
            get
            {
                if (AntiCheat != "") return "Gioco online con anti-cheat (" + AntiCheat + "): OptiScaler bloccato, rischio ban.";
                if (!Is64) return "Gioco a 32 bit: OptiScaler funziona solo con i giochi a 64 bit. Bloccato (il gioco non partirebbe).";
                bool rx9000 = Hardware.Gpu.ToUpperInvariant().Contains("RX 9");
                if (Fsr31 || Fsr31Vk)
                    return rx9000 ? "Ha già FSR 3.1: basta attivare FSR 4 in Adrenalin, OptiScaler non serve."
                                  : "Ha già FSR 3.1: di solito basta così. OptiScaler serve solo per provare altre opzioni (es. FSR 4 o XeSS al posto di FSR).";
                if (Dlss || Xess || Fsr2) return "Ha solo " + Upscalers + ": OptiScaler consigliato (usa FSR 3.1/4 o XeSS al posto di questi" + (Dlss ? ", e la generazione di fotogrammi" : "") + ").";
                return "Nessun upscaler riconosciuto: OptiScaler di solito non funziona con questo gioco.";
            }
        }
    }

    // ---------------------------------------------------------------- OptiScaler: scoperta giochi, pacchetto, installa/rimuovi/aggiorna
    // Il pacchetto si scarica SOLO quando l'utente preme il pulsante, dalle release ufficiali su GitHub (optiscaler/OptiScaler).
    public static class Opti
    {
        public const string ManifestName = "blackbox_optiscaler.json";
        public static readonly string[] ProxyNames = { "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll", "d3d12.dll", "wininet.dll", "winhttp.dll" };
        static string baseDir;   // Documenti\BlackBox\optiscaler

        public static void Init(string logDir) { baseDir = Path.Combine(logDir, "optiscaler"); }

        // ================================================ scoperta dei giochi
        public static List<GameInfo> FindGames()
        {
            var list = new List<GameInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string, string, string> add = (name, store, root, exe) =>
            {
                try
                {
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root) || !seen.Add(root.TrimEnd('\\'))) return;
                    var g = new GameInfo { Name = name, Store = store, Root = root.TrimEnd('\\') };
                    Analyze(g, exe);
                    if (g.Exe != "") list.Add(g);
                }
                catch { }
            };
            // Steam: SteamPath -> libraryfolders.vdf -> appmanifest_*.acf
            try
            {
                string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string;
                if (!string.IsNullOrEmpty(steam))
                {
                    steam = steam.Replace('/', '\\');
                    var libs = new List<string> { steam };
                    string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                    foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        string sa = Path.Combine(lib, "steamapps");
                        if (!Directory.Exists(sa)) continue;
                        foreach (var acf in Directory.GetFiles(sa, "appmanifest_*.acf"))
                        {
                            string t = File.ReadAllText(acf);
                            string name = Regex.Match(t, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                            string dir = Regex.Match(t, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                            if (dir == "" || name.StartsWith("Steamworks") || name.StartsWith("Proton") || name.Contains("Redistributable")) continue;
                            add(name, "Steam", Path.Combine(sa, "common", dir), null);
                        }
                    }
                }
            }
            catch { }
            // Epic: manifest JSON
            try
            {
                string man = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
                if (Directory.Exists(man))
                    foreach (var item in Directory.GetFiles(man, "*.item"))
                    {
                        string t = File.ReadAllText(item);
                        Func<string, string> js = k => Regex.Unescape(Regex.Match(t, "\"" + k + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value);
                        string loc = js("InstallLocation"), exe = js("LaunchExecutable");
                        add(js("DisplayName"), "Epic", loc, exe == "" ? null : Path.Combine(loc, exe));
                    }
            }
            catch { }
            // GOG: Registro
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games"))
                    if (k != null)
                        foreach (var id in k.GetSubKeyNames())
                            using (var g = k.OpenSubKey(id))
                            {
                                string path = g.GetValue("path") as string, exe = g.GetValue("exe") as string;
                                add((g.GetValue("gameName") as string) ?? id, "GOG", path, exe);
                            }
            }
            catch { }
            // cartelle aggiunte a mano e giochi visti nelle sessioni
            foreach (var dir in ManualFolders()) add(Path.GetFileName(dir.TrimEnd('\\')), "a mano", dir, null);
            foreach (var exe in Settings.Get("giochi_visti", "").Split('|').Where(x => x != ""))
                if (File.Exists(exe)) add(Path.GetFileNameWithoutExtension(exe), "visto in gioco", Path.GetDirectoryName(exe), exe);
            return list.OrderBy(x => x.Name).ToList();
        }

        // chiave di impostazioni.ini per l'esito di OptiScaler in un gioco ('=' e a capo non possono stare in una chiave)
        public static string EsitoKey(string dir) { return "opti_esito:" + (dir ?? "").Replace('=', '_').Replace('\r', ' ').Replace('\n', ' ').ToLowerInvariant(); }

        public static List<string> ManualFolders() { return Settings.Get("opti_cartelle", "").Split('|').Where(x => x != "").ToList(); }
        public static void AddManualFolder(string dir)
        {
            var l = ManualFolders(); if (!l.Contains(dir, StringComparer.OrdinalIgnoreCase)) l.Add(dir);
            Settings.Set("opti_cartelle", string.Join("|", l));
        }

        // ricordati dell'eseguibile di ogni gioco visto in una sessione (anche quelli fuori da Steam/Epic/GOG)
        public static void RememberGame(string exe)
        {
            if (string.IsNullOrEmpty(exe) || exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) return;
            var l = Settings.Get("giochi_visti", "").Split('|').Where(x => x != "").ToList();
            if (l.Contains(exe, StringComparer.OrdinalIgnoreCase)) return;
            l.Add(exe); if (l.Count > 60) l.RemoveAt(0);
            Settings.Set("giochi_visti", string.Join("|", l));
        }

        // ================================================ analisi di una cartella di gioco
        static readonly string[] NotGameExe = { "crash", "report", "launcher", "setup", "unins", "redist", "vc_redist", "dxsetup", "helper", "easyanticheat", "beservice",
                                                "battleye", "prereq", "update", "dotnet", "installer", "cefprocess", "webhelper", "ue4prereq", "physx", "touchup" };

        public static void Analyze(GameInfo g, string preferredExe)
        {
            var files = Walk(g.Root, 6, 80000);
            // eseguibile: quello indicato dallo store, poi Unreal *-Win64-Shipping.exe, poi il piu' grande che non sia un launcher
            string exe = null;
            if (!string.IsNullOrEmpty(preferredExe) && File.Exists(preferredExe) && !Path.GetFileName(preferredExe).ToLowerInvariant().Contains("launcher")) exe = preferredExe;
            if (exe == null) exe = files.FirstOrDefault(f => f.EndsWith("-win64-shipping.exe", StringComparison.OrdinalIgnoreCase));
            if (exe == null)
                exe = files.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Depth(g.Root, f) <= 3 &&
                                       !NotGameExe.Any(n => Path.GetFileName(f).ToLowerInvariant().Contains(n)))
                           .OrderByDescending(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }).FirstOrDefault();
            if (exe == null) return;
            g.Exe = exe; g.ExeDir = Path.GetDirectoryName(exe);
            g.Is64 = Is64Bit(exe);

            // i file messi da OptiScaler non contano come upscaler del gioco (confronto sul percorso relativo alla cartella dell'eseguibile)
            var man = ReadManifest(g.ExeDir);
            // i file che il gioco aveva gia' (messi da parte in .blackbox-bak) restano upscaler del gioco
            var mine = new HashSet<string>(man.Files.Except(man.Backups, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            // (confronto sulla parte finale: lo stesso percorso puo' comparire con il nome breve "PRONER~1" o con quello lungo)
            string tail = "\\" + Path.GetFileName(g.ExeDir) + "\\";
            Func<string, bool> ours = f => mine.Any(rel => f.EndsWith(tail + rel, StringComparison.OrdinalIgnoreCase));
            Func<string, bool> has = n => files.Any(f => Path.GetFileName(f).Equals(n, StringComparison.OrdinalIgnoreCase) && !ours(f));
            g.Dlss = has("nvngx_dlss.dll"); g.DlssFg = has("nvngx_dlssg.dll");
            g.Xess = has("libxess.dll");
            g.Fsr31 = has("amd_fidelityfx_dx12.dll") || has("amd_fidelityfx_upscaler_dx12.dll");
            g.Fsr31Vk = has("amd_fidelityfx_vk.dll");
            g.Fsr2 = has("ffx_fsr2_api_x64.dll") || has("ffx_fsr2_api_dx12_x64.dll");
            g.Vulkan = g.Fsr31Vk || has("vulkan-1.dll");

            // anti-cheat: blocco di sicurezza
            var ac = new List<string>();
            foreach (var f in files)
            {
                string p = f.ToLowerInvariant(), n = Path.GetFileName(p.TrimEnd('\\'));
                if (p.EndsWith("\\")) p = p.TrimEnd('\\') + "\\";
                if (p.Contains("\\easyanticheat")) ac.Add("Easy Anti-Cheat");
                else if (p.Contains("\\battleye\\") || n.StartsWith("beservice")) ac.Add("BattlEye");
                else if (n == "vgk.sys" || p.Contains("\\riot vanguard")) ac.Add("Vanguard");
                else if (n.StartsWith("mhyprot")) ac.Add("mhyprot");
                else if (n.StartsWith("xigncode") || p.Contains("\\xigncode")) ac.Add("XIGNCODE");
                else if (p.Contains("\\nprotect") || p.Contains("\\gameguard")) ac.Add("nProtect GameGuard");
                else if (n.StartsWith("eaanticheat") || p.Contains("\\eaanticheat")) ac.Add("EA AntiCheat");
                else if (p.Contains("\\anticheatexpert") || n.StartsWith("sguard") || n.StartsWith("ace-base")) ac.Add("ACE (Tencent)");
                else if (n == "start_protected_game.exe" || n.StartsWith("eac_launcher")) ac.Add("Easy Anti-Cheat");
                else if (p.Contains("\\equ8") || n.StartsWith("equ8")) ac.Add("EQU8");
                else if (n.StartsWith("pnkbstr") || p.Contains("\\punkbuster")) ac.Add("PunkBuster");
                else if (p.Contains("\\faceit")) ac.Add("FACEIT");
                else if (n.StartsWith("xhunter") || p.Contains("\\wellbia")) ac.Add("XIGNCODE");
                else if (p.Contains("\\denuvo anti-cheat")) ac.Add("Denuvo Anti-Cheat");
            }
            // giochi online noti con anti-cheat che non lascia file riconoscibili (es. VAC di Steam) o con ban lato server
            string exeName = Path.GetFileNameWithoutExtension(g.Exe).ToLowerInvariant(), title = (g.Name ?? "").ToLowerInvariant();
            string[] known = OnlineGames.FirstOrDefault(k => exeName == k[0] || (k[1] != "" && title.Contains(k[1])));
            if (known != null && ac.Count == 0) ac.Add(KnownName(known));   // solo se i file non l'hanno gia' rivelato
            g.AntiCheat = string.Join(", ", ac.Distinct());

            var m = ReadManifest(g.ExeDir);
            if (m.Version != "") { g.OptiByBlackBox = true; g.OptiVersion = m.Version; g.OptiProxy = m.Proxy; }
            else
                foreach (var pn in ProxyNames.Concat(new[] { "OptiScaler.asi" }))
                {
                    string p = Path.Combine(g.ExeDir, pn);
                    if (File.Exists(p) && IsOptiDll(p)) { g.OptiByHand = true; g.OptiProxy = pn; break; }
                }
        }

        // { exe senza .exe, parte del nome del gioco, anti-cheat }: giochi online dove una DLL estranea puo' costare il ban
        static readonly string[][] OnlineGamesList = {
            new[] { "cs2", "counter-strike", "VAC (Steam)" }, new[] { "csgo", "", "VAC (Steam)" }, new[] { "dota2", "dota 2", "VAC (Steam)" },
            new[] { "tf_win64", "team fortress", "VAC (Steam)" }, new[] { "project8", "deadlock", "VAC (Steam)" },
            new[] { "valorant", "valorant", "Vanguard" }, new[] { "league of legends", "league of legends", "Vanguard" },
            new[] { "fortniteclient-win64-shipping", "fortnite", "Easy Anti-Cheat / BattlEye" }, new[] { "r5apex", "apex legends", "Easy Anti-Cheat" },
            new[] { "tslgame", "pubg", "BattlEye" }, new[] { "rainbowsix", "rainbow six", "BattlEye" }, new[] { "overwatch", "overwatch", "Defense Matrix" },
            new[] { "cod", "call of duty", "Ricochet" }, new[] { "destiny2", "destiny 2", "BattlEye" }, new[] { "escapefromtarkov", "escape from tarkov", "BattlEye" },
            new[] { "marvel-win64-shipping", "marvel rivals", "NetEase" }, new[] { "deltaforceclient-win64-shipping", "delta force", "ACE (Tencent)" },
            new[] { "discovery", "the finals", "Easy Anti-Cheat" }, new[] { "rustclient", "", "Easy Anti-Cheat" }, new[] { "dayz_x64", "dayz", "BattlEye" },
            new[] { "bf6", "battlefield", "EA AntiCheat" }, new[] { "bf2042", "", "EA AntiCheat" }, new[] { "fc25", "ea sports fc", "EA AntiCheat" }, new[] { "fc26", "", "EA AntiCheat" },
            new[] { "rocketleague", "rocket league", "Easy Anti-Cheat" }, new[] { "deadbydaylight-win64-shipping", "dead by daylight", "Easy Anti-Cheat" },
            new[] { "warframe.x64", "warframe", "anti-cheat di Warframe" }, new[] { "haloinfinite", "halo infinite", "Easy Anti-Cheat" },
            new[] { "naraka", "naraka", "NetEase" }, new[] { "aces", "war thunder", "Easy Anti-Cheat" }, new[] { "worldoftanks", "world of tanks", "anti-cheat di Wargaming" },
            new[] { "lostark", "lost ark", "Easy Anti-Cheat" }, new[] { "huntgame", "hunt: showdown", "Easy Anti-Cheat" }, new[] { "gta5", "grand theft auto v", "BattlEye (GTA Online)" },
            new[] { "fivem", "fivem", "anti-cheat di FiveM" }, new[] { "ragemp", "", "anti-cheat di RAGE:MP" }, new[] { "arma3_x64", "arma 3", "BattlEye" },
            new[] { "eldenring", "elden ring", "Easy Anti-Cheat" }, new[] { "newworld", "new world", "Easy Anti-Cheat" }, new[] { "thefinals", "", "Easy Anti-Cheat" } };
        static IEnumerable<string[]> OnlineGames { get { return OnlineGamesList; } }
        static string KnownName(string[] k) { return k[2] + " (gioco online)"; }

        // intestazione PE dell'eseguibile: OptiScaler esiste solo a 64 bit (x64)
        public static bool Is64Bit(string exe)
        {
            try
            {
                using (var fs = File.OpenRead(exe))
                using (var br = new BinaryReader(fs))
                {
                    if (br.ReadUInt16() != 0x5A4D) return true;            // non e' un eseguibile valido ("MZ"): non decido da qui
                    fs.Seek(0x3C, SeekOrigin.Begin);
                    int pe = br.ReadInt32();
                    if (pe <= 0 || pe > fs.Length - 6) return true;
                    fs.Seek(pe, SeekOrigin.Begin);
                    if (br.ReadUInt32() != 0x00004550) return true;        // firma "PE\0\0"
                    ushort machine = br.ReadUInt16();
                    return machine != 0x014C;                               // 0x14C = x86 a 32 bit; 0x8664 = x64; ARM64 lo lascio passare
                }
            }
            catch { return true; }   // illeggibile: non blocco per questo (gli altri controlli restano)
        }

        static int Depth(string root, string f) { return f.Substring(root.Length).Count(c => c == '\\'); }

        // elenco dei file fino a una certa profondita' (salta le cartelle illeggibili)
        static List<string> Walk(string root, int maxDepth, int maxFiles)
        {
            var res = new List<string>();
            var stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(root, 0));
            while (stack.Count > 0 && res.Count < maxFiles)
            {
                var cur = stack.Pop();
                try { res.AddRange(Directory.GetFiles(cur.Key)); } catch { }
                if (cur.Value > 0) res.Add(cur.Key + "\\");   // anche le cartelle (es. EasyAntiCheat vuota), con la barra finale
                if (cur.Value >= maxDepth) continue;
                try { foreach (var d in Directory.GetDirectories(cur.Key)) stack.Push(new KeyValuePair<string, int>(d, cur.Value + 1)); } catch { }
            }
            return res;
        }

        // descrizione di un file gia' presente con il nome scelto (es. ReShade), per avvisare prima di metterlo da parte
        public static string ExistingProxyInfo(string exeDir, string proxy)
        {
            string p = Path.Combine(exeDir, proxy);
            if (!File.Exists(p)) return null;
            try
            {
                var v = FileVersionInfo.GetVersionInfo(p);
                string what = !string.IsNullOrEmpty(v.ProductName) ? v.ProductName : !string.IsNullOrEmpty(v.FileDescription) ? v.FileDescription : "file sconosciuto";
                return what.Trim();
            }
            catch { return "file sconosciuto"; }
        }

        public static bool IsOptiDll(string path)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(path);
                return string.Equals(v.OriginalFilename, "OptiScaler.dll", StringComparison.OrdinalIgnoreCase) || string.Equals(v.ProductName, "OptiScaler", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ================================================ pacchetto (download dal pulsante)
        public class Release { public string Tag = "", Name = "", Date = "", Url = "", Digest = ""; public long Size; public bool Pre; }

        public static List<Release> ListReleases(bool includePre)
        {
            string json = Net.GetText("https://api.github.com/repos/optiscaler/OptiScaler/releases?per_page=15");
            var res = new List<Release>();
            // ogni release: tag, nome, data, pre-release e il primo asset .7z
            foreach (Match rm in Regex.Matches(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"(.*?)(?=\"tag_name\"|$)", RegexOptions.Singleline))
            {
                string body = rm.Groups[2].Value;
                var r = new Release { Tag = rm.Groups[1].Value };
                r.Pre = Regex.IsMatch(body, "\"prerelease\"\\s*:\\s*true");
                r.Date = Regex.Match(body, "\"published_at\"\\s*:\\s*\"([0-9-]{10})").Groups[1].Value;
                var asset = Regex.Match(body, "\"name\"\\s*:\\s*\"([^\"]+\\.7z)\"(.*?)\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Singleline);
                if (!asset.Success) continue;
                r.Name = asset.Groups[1].Value; r.Url = asset.Groups[3].Value;
                long size; if (long.TryParse(Regex.Match(asset.Groups[2].Value, "\"size\"\\s*:\\s*(\\d+)").Groups[1].Value, out size)) r.Size = size;
                r.Digest = Regex.Match(asset.Groups[2].Value, "\"digest\"\\s*:\\s*\"sha256:([0-9a-f]{64})\"").Groups[1].Value;
                if (r.Pre && !includePre) continue;
                res.Add(r);
            }
            return res;
        }

        // versioni gia' scaricate (cartelle in Documenti\BlackBox\optiscaler\<versione> con OptiScaler.dll)
        public static List<string> CachedVersions()
        {
            try { return Directory.GetDirectories(baseDir).Where(d => File.Exists(Path.Combine(d, "OptiScaler.dll"))).Select(Path.GetFileName).OrderByDescending(x => x).ToList(); }
            catch { return new List<string>(); }
        }
        public static string PackageDir(string version) { return Path.Combine(baseDir, version); }

        public static string Download(Release r)
        {
            Directory.CreateDirectory(baseDir);
            string archive = Path.Combine(baseDir, r.Name), dest = PackageDir(r.Tag), tmp = dest + ".tmp";
            Net.GetFile(r.Url, archive);
            if (r.Size > 0 && new FileInfo(archive).Length != r.Size) { File.Delete(archive); throw new Exception("download incompleto"); }
            if (r.Digest != "")
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(archive))
                    if (BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant() != r.Digest) { fs.Close(); File.Delete(archive); throw new Exception("il controllo SHA-256 non corrisponde: archivio scartato"); }
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            Extract(archive, tmp);
            // l'archivio puo' avere una cartella in piu' all'interno
            string root = tmp;
            if (!File.Exists(Path.Combine(root, "OptiScaler.dll")))
                root = Directory.GetDirectories(tmp).FirstOrDefault(d => File.Exists(Path.Combine(d, "OptiScaler.dll")));
            if (root == null) throw new Exception("nell'archivio non trovo OptiScaler.dll");
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.Move(root, dest);
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            File.Delete(archive);
            return r.Tag;
        }

        // .7z: prima 7-Zip se installato, poi tar.exe di Windows (libarchive legge i .7z)
        static void Extract(string archive, string dest)
        {
            string sevenZip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"7-Zip\7z.exe");
            ProcessStartInfo psi = File.Exists(sevenZip)
                ? new ProcessStartInfo(sevenZip, "x -y -o\"" + dest + "\" \"" + archive + "\"")
                : new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe"), "-xf \"" + archive + "\" -C \"" + dest + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            using (var p = Process.Start(psi))
            {
                if (!p.WaitForExit(180000)) { try { p.Kill(); } catch { } throw new Exception("estrazione troppo lenta"); }
                if (p.ExitCode != 0) throw new Exception("non riesco a estrarre il .7z: installa 7-Zip (7-zip.org) e riprova");
            }
        }

        // ================================================ manifest nella cartella del gioco
        public class Manifest { public string Version = "", Proxy = "", Date = ""; public List<string> Files = new List<string>(), Backups = new List<string>(), Dirs = new List<string>(); }

        static string J(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

        static void WriteManifest(string dir, Manifest m)
        {
            Func<List<string>, string> arr = l => "[" + string.Join(", ", l.Select(x => "\"" + J(x) + "\"")) + "]";
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"creato_da\": \"BlackBox\",");
            sb.AppendLine("  \"versione\": \"" + J(m.Version) + "\",");
            sb.AppendLine("  \"proxy\": \"" + J(m.Proxy) + "\",");
            sb.AppendLine("  \"data\": \"" + J(m.Date) + "\",");
            sb.AppendLine("  \"file\": " + arr(m.Files) + ",");
            sb.AppendLine("  \"backup\": " + arr(m.Backups) + ",");
            sb.AppendLine("  \"cartelle\": " + arr(m.Dirs));
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(dir, ManifestName), sb.ToString(), new UTF8Encoding(false));
        }

        public static Manifest ReadManifest(string dir)
        {
            var m = new Manifest();
            try
            {
                string p = Path.Combine(dir, ManifestName);
                if (!File.Exists(p)) return m;
                string t = File.ReadAllText(p);
                Func<string, string> s = k => Regex.Unescape(Regex.Match(t, "\"" + k + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value);
                Func<string, List<string>> a = k => Regex.Matches(Regex.Match(t, "\"" + k + "\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline).Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\"")
                                                         .Cast<Match>().Select(x => Regex.Unescape(x.Groups[1].Value)).ToList();
                m.Version = s("versione"); m.Proxy = s("proxy"); m.Date = s("data");
                m.Files = a("file"); m.Backups = a("backup"); m.Dirs = a("cartelle");
            }
            catch { }
            return m;
        }

        // ================================================ installa / rimuovi / aggiorna
        public static string RunningFrom(string dir)
        {
            foreach (var p in Process.GetProcesses())
                using (p)
                    try { if (p.MainModule.FileName.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) return p.ProcessName; } catch { }
            return null;
        }

        // proxy consigliato: dxgi.dll; winmm.dll per i giochi Vulkan
        public static string SuggestedProxy(GameInfo g) { string why; return SuggestedProxy(g, out why); }

        // nome della DLL consigliato per QUESTO gioco, con il perche' in parole semplici
        public static string SuggestedProxy(GameInfo g, out string why)
        {
            string p = (g.ExeDir ?? "").ToLowerInvariant();
            // se l'ultima prova con un nome non e' stata caricata dal gioco, si consiglia il nome successivo
            string esito = Settings.Get(EsitoKey(g.ExeDir), "");
            string[] order = { "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll", "d3d12.dll", "wininet.dll", "winhttp.dll" };
            string last = Settings.Get(EsitoKey(g.ExeDir) + ":dll", "");
            if (esito.StartsWith("problemi") && last != "")
            {
                int i = Array.IndexOf(order, last.ToLowerInvariant());
                if (i >= 0 && i < order.Length - 1) { why = "con " + last + " ci sono stati problemi nell'ultima partita: si prova il nome successivo"; return order[i + 1]; }
            }
            if (p.Contains("\\windowsapps\\") || p.Contains("\\xboxgames\\"))
            { why = "gioco del Microsoft Store / Xbox: qui funziona meglio winmm.dll"; return "winmm.dll"; }
            if (g.Vulkan && !g.Fsr31)
            { why = "il gioco usa Vulkan: con Vulkan si usa winmm.dll"; return "winmm.dll"; }
            if (File.Exists(Path.Combine(g.ExeDir ?? "", "dxgi.dll")) && !IsOptiDll(Path.Combine(g.ExeDir ?? "", "dxgi.dll")))
            { why = "nella cartella c'è già un dxgi.dll (spesso ReShade): con winmm.dll funzionano tutti e due"; return "winmm.dll"; }
            why = "gioco DirectX 11/12: dxgi.dll è il nome più compatibile";
            return "dxgi.dll";
        }

        public const string ProxyHelp =
            "Cos'è il «nome della DLL»: OptiScaler deve travestirsi da un file che il gioco carica da solo all'avvio. Quando il gioco cerca quel file, Windows guarda prima nella sua cartella: così carica OptiScaler.\n" +
            "Quale scegliere, in ordine:\n" +
            "1. dxgi.dll — il più compatibile, giochi DirectX 11 e 12 (quasi tutti). Consigliato.\n" +
            "2. winmm.dll — giochi Vulkan, giochi del Microsoft Store / Xbox, oppure se c'è già un dxgi.dll (ReShade).\n" +
            "3. version.dll — seconda alternativa, se i primi due non vanno.\n" +
            "4. dbghelp, d3d12, wininet, winhttp — ultime prove.\n" +
            "Se il gioco non carica OptiScaler o va in crash: Rimuovi e reinstalla con il nome successivo. BlackBox lo consiglia da solo.";

        public const string FakeNvapiHelp =
            "Cos'è fakenvapi: alcuni giochi mostrano DLSS (e DLSS Frame Generation, Reflex) solo se credono di avere una scheda NVIDIA. " +
            "fakenvapi «fa finta» di essere il pezzo del driver NVIDIA che il gioco interroga: così, su una scheda AMD o Intel, l'opzione DLSS compare nel menu del gioco " +
            "e OptiScaler la trasforma in FSR o XeSS (e DLSS Frame Generation nella generazione di fotogrammi di AMD).\n" +
            "Quando spuntarlo: solo se il gioco ha DLSS (BlackBox lo spunta da solo in quei giochi). Se il gioco non ha DLSS non serve. Su una scheda NVIDIA non serve mai.";

        public static void Install(GameInfo g, string version, string proxy, bool fakeNvapi, string keepIni)
        {
            if (g.AntiCheat != "") throw new Exception("gioco online con anti-cheat (" + g.AntiCheat + "): rischio ban, installazione bloccata");
            if (!g.Is64) throw new Exception("gioco a 32 bit: OptiScaler funziona solo con i giochi a 64 bit");
            string running = RunningFrom(g.ExeDir);
            if (running != null) throw new Exception("chiudi prima il gioco (" + running + " è aperto)");
            string pkg = PackageDir(version);
            if (!File.Exists(Path.Combine(pkg, "OptiScaler.dll"))) throw new Exception("pacchetto OptiScaler " + version + " non scaricato");
            if (ReadManifest(g.ExeDir).Version != "") throw new Exception("OptiScaler è già installato: usa Aggiorna o Rimuovi");
            foreach (var pn in ProxyNames.Concat(new[] { "OptiScaler.asi" }))
                if (File.Exists(Path.Combine(g.ExeDir, pn)) && IsOptiDll(Path.Combine(g.ExeDir, pn)))
                    throw new Exception("c'è già un OptiScaler installato a mano (" + pn + "): toglilo prima con Remove_OptiScaler.bat nella cartella del gioco");

            var m = new Manifest { Version = version, Proxy = proxy, Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
            var skip = new[] { "setup_windows.bat", "setup_linux.sh" };
            bool done = false;
            try
            {
                foreach (var src in Directory.GetFiles(pkg, "*", SearchOption.AllDirectories))
                {
                    string rel = src.Substring(pkg.Length + 1), name = Path.GetFileName(rel);
                    if (skip.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith("!!")) continue;
                    if (!fakeNvapi && (name.StartsWith("fakenvapi", StringComparison.OrdinalIgnoreCase) || name.StartsWith("dlssg_to_fsr3", StringComparison.OrdinalIgnoreCase))) continue;
                    if (name.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase)) rel = proxy;
                    string dst = Path.Combine(g.ExeDir, rel), dstDir = Path.GetDirectoryName(dst);
                    if (!Directory.Exists(dstDir))
                    {
                        Directory.CreateDirectory(dstDir);
                        m.Dirs.Add(dstDir.Substring(g.ExeDir.Length + 1));
                    }
                    if (File.Exists(dst))
                    {
                        // copia di sicurezza del file del gioco con lo stesso nome
                        string bak = dst + ".blackbox-bak";
                        if (!File.Exists(bak)) { File.Move(dst, bak); m.Backups.Add(rel); }
                        else File.Delete(dst);
                    }
                    File.Copy(src, dst);
                    m.Files.Add(rel);
                }
                string ini = Path.Combine(g.ExeDir, "OptiScaler.ini");
                if (keepIni != null) File.WriteAllText(ini, keepIni, new UTF8Encoding(false));
                else if (!g.Dlss && File.Exists(ini))
                    // senza DLSS nel gioco non serve fingersi una scheda NVIDIA (come sceglie lo script ufficiale)
                    File.WriteAllText(ini, Regex.Replace(File.ReadAllText(ini), @"(?m)^Dxgi=auto\s*$", "Dxgi=false"), new UTF8Encoding(false));
                WriteManifest(g.ExeDir, m);
                done = true;
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception("cartella del gioco protetta (serve amministratore): BlackBox non chiede permessi di amministratore. Copia a mano o sposta il gioco in una libreria Steam fuori da Programmi.");
            }
            finally
            {
                if (!done) { try { Undo(g.ExeDir, m); } catch { } }
            }
        }

        public static void Remove(GameInfo g)
        {
            string running = RunningFrom(g.ExeDir);
            if (running != null) throw new Exception("chiudi prima il gioco (" + running + " è aperto)");
            var m = ReadManifest(g.ExeDir);
            if (m.Version == "") throw new Exception("OptiScaler non è stato installato da BlackBox in questo gioco");
            Undo(g.ExeDir, m);
            foreach (var log in new[] { "OptiScaler.log", "fakenvapi.log", "dlssg_to_fsr3.log" })
                try { File.Delete(Path.Combine(g.ExeDir, log)); } catch { }
            File.Delete(Path.Combine(g.ExeDir, ManifestName));
        }

        // toglie i file aggiunti, rimette le copie di sicurezza, cancella le cartelle create (se vuote)
        static void Undo(string dir, Manifest m)
        {
            foreach (var f in m.Files) { string p = Path.Combine(dir, f); if (File.Exists(p)) File.Delete(p); }
            foreach (var b in m.Backups) { string p = Path.Combine(dir, b); if (File.Exists(p + ".blackbox-bak") && !File.Exists(p)) File.Move(p + ".blackbox-bak", p); }
            foreach (var d in m.Dirs.OrderByDescending(x => x.Length))
                try { string p = Path.Combine(dir, d); if (Directory.Exists(p) && !Directory.EnumerateFileSystemEntries(p).Any()) Directory.Delete(p); } catch { }
        }

        // aggiorna: rimuove e reinstalla la nuova versione tenendo le impostazioni dell'utente (OptiScaler.ini)
        public static void Update(GameInfo g, string version, bool fakeNvapi)
        {
            var m = ReadManifest(g.ExeDir);
            string ini = null;
            try { ini = File.ReadAllText(Path.Combine(g.ExeDir, "OptiScaler.ini")); } catch { }
            string proxy = m.Proxy == "" ? SuggestedProxy(g) : m.Proxy;
            Remove(g);
            Install(g, version, proxy, fakeNvapi, ini);
        }

        // ================================================ e' attivo nel gioco in esecuzione?
        // cerca tra le DLL caricate dal processo quella che "dentro" si chiama OptiScaler
        public static string LoadedIn(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                    foreach (ProcessModule mod in p.Modules)
                    {
                        string n = mod.ModuleName.ToLowerInvariant();
                        if (!ProxyNames.Contains(n) && n != "optiscaler.asi" && n != "optiscaler.dll") continue;
                        var v = mod.FileVersionInfo;
                        if (string.Equals(v.OriginalFilename, "OptiScaler.dll", StringComparison.OrdinalIgnoreCase) || string.Equals(v.ProductName, "OptiScaler", StringComparison.OrdinalIgnoreCase))
                            return mod.ModuleName + ", " + (v.FileVersion ?? "").Trim();
                    }
            }
            catch { }
            return null;
        }

        // il modulo in errore di un crash e' di OptiScaler?
        public static bool IsOptiModule(string module, string proxy)
        {
            if (string.IsNullOrEmpty(module)) return false;
            string m = module.ToLowerInvariant();
            return m == (proxy ?? "").ToLowerInvariant() || m.Contains("optiscaler") || m.Contains("fakenvapi") || m.Contains("nvapi64") ||
                   m.StartsWith("dlssg_to_fsr3") || m.StartsWith("libxess") || m.StartsWith("amd_fidelityfx");
        }
    }
}
