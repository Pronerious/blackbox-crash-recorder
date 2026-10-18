using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BlackBox
{
    // ---------------------------------------------------------------- Tabella dei limiti ufficiali (data/limiti.json)
    // Inclusa nell'exe; se in Documenti\BlackBox c'e' una versione piu' nuova (scaricata con «Aggiorna i limiti») si usa quella.
    // Ogni riga: regola (espressione sul nome del componente in maiuscolo), limite, fonte ufficiale, verificato.
    public class LimitRule
    {
        public string Regola = "", Nome = "", Fonte = "", Tipo = "";
        public double Valore, Rallenta, Potenza;
        public bool Verificato;
    }

    public static class LimitsDb
    {
        public const string UpdateUrl = "https://raw.githubusercontent.com/Pronerious/blackbox-crash-recorder/main/data/limiti.json";
        public static string Version = "";
        public static string Origin = "";   // "incorporata" o "aggiornata"
        static List<LimitRule> cpu = new List<LimitRule>(), gpu = new List<LimitRule>();
        static string userFile;

        public static void Load(string logDir)
        {
            userFile = Path.Combine(logDir, "limiti.json");
            string embedded = null, user = null;
            try { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("limiti.json")) using (var r = new StreamReader(s, Encoding.UTF8)) embedded = r.ReadToEnd(); } catch { }
            try { if (File.Exists(userFile)) user = File.ReadAllText(userFile, Encoding.UTF8); } catch { }
            string ve = VersionOf(embedded), vu = VersionOf(user);
            // la tabella scaricata vale solo se e' valida e piu' nuova di quella dentro il programma
            if (vu != "" && string.CompareOrdinal(vu, ve) > 0 && Parse(user)) { Version = vu; Origin = "aggiornata"; return; }
            if (Parse(embedded)) { Version = ve; Origin = "incorporata"; }
        }

        static string VersionOf(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            var m = Regex.Match(json, "\"versione\"\\s*:\\s*\"(\\d{4}-\\d{2}-\\d{2})\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        static bool Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                var c = Rules(root, "cpu", "tjmax"); var g = Rules(root, "gpu", "limite");
                if (c.Count == 0 || g.Count == 0) return false;
                foreach (var r in c.Concat(g)) new Regex(r.Regola);   // regola non valida = tabella scartata
                cpu = c; gpu = g;
                return true;
            }
            catch { return false; }
        }

        static List<LimitRule> Rules(Dictionary<string, object> root, string key, string valueKey)
        {
            var res = new List<LimitRule>();
            object arr;
            if (!root.TryGetValue(key, out arr) || !(arr is IEnumerable)) return res;
            foreach (var o in (IEnumerable)arr)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                Func<string, string> s = k => { object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; };
                Func<string, double> n = k => { object v; double x; return d.TryGetValue(k, out v) && v != null && double.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) ? x : 0; };
                var r = new LimitRule { Regola = s("regola"), Nome = s("nome"), Fonte = s("fonte"), Tipo = s("tipo"), Valore = n(valueKey), Rallenta = n("rallenta"), Potenza = n("potenza") };
                object ver; r.Verificato = d.TryGetValue("verificato", out ver) && ver is bool && (bool)ver;
                if (r.Regola != "" && r.Valore > 40 && r.Valore < 130) res.Add(r);   // valori assurdi scartati
            }
            return res;
        }

        public static LimitRule ForCpu(string name) { return Find(cpu, name); }
        public static LimitRule ForGpu(string name) { return Find(gpu, name); }

        static LimitRule Find(List<LimitRule> list, string name)
        {
            string up = (name ?? "").ToUpperInvariant();
            if (up == "") return null;
            foreach (var r in list)
                try { if (Regex.IsMatch(up, r.Regola)) return r; } catch { }
            return null;
        }

        // pulsante «Aggiorna i limiti»: scarica la tabella dal repository di BlackBox su GitHub (solo su richiesta)
        public static string Update()
        {
            string json;
            try { json = Net.GetText(UpdateUrl); }
            catch (System.Net.WebException ex)
            {
                var resp = ex.Response as System.Net.HttpWebResponse;
                if (resp != null && resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new Exception("la tabella non è raggiungibile sul sito di BlackBox (il repository non è pubblico). Restano validi i limiti già presenti nel programma");
                throw new Exception("nessuna connessione con GitHub (" + ex.Message + "). Restano validi i limiti già presenti nel programma");
            }
            string v = VersionOf(json);
            if (v == "") throw new Exception("il file scaricato non è una tabella dei limiti valida");
            if (string.CompareOrdinal(v, Version) <= 0) return "Hai già la tabella più recente (" + Version + ").";
            string before = Version;
            var saveCpu = cpu; var saveGpu = gpu;
            if (!Parse(json)) { cpu = saveCpu; gpu = saveGpu; throw new Exception("il file scaricato non è una tabella dei limiti valida"); }
            File.WriteAllText(userFile, json, new UTF8Encoding(false));
            Version = v; Origin = "aggiornata";
            return "Tabella dei limiti aggiornata: " + before + " → " + v + ".";
        }
    }
}
