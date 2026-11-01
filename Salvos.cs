using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SuspensaoAjustavel
{
    // Ajustes por ônibus em UserData/SuspensaoAjustavel/<ônibus>.txt (chave=valor, alturas em mm).
    internal static class Salvos
    {
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        internal static string Pasta => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "SuspensaoAjustavel");

        static string Arquivo(string onibus)
        {
            var sb = new StringBuilder();
            foreach (char ch in onibus) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
            var nome = sb.ToString().Trim();
            if (nome.Length == 0) nome = "sem_nome";
            return Path.Combine(Pasta, nome + ".txt");
        }

        internal static Ajustes Carregar(string onibus)
        {
            var arq = Arquivo(onibus);
            if (!File.Exists(arq)) return null;
            var a = new Ajustes();
            foreach (var bruta in File.ReadAllLines(arq))
            {
                var l = bruta.Split('#')[0].Trim();
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                var chave = l.Substring(0, i).Trim().ToLowerInvariant();
                if (!float.TryParse(l.Substring(i + 1).Trim(), NumberStyles.Float, Ci, out float v)) continue;
                switch (chave)
                {
                    // Formato novo: altura_eixo1_mm, altura_eixo2_mm, ... (dianteira para trás).
                    case "altura_dianteira_mm": a.DefinirAltura(0, Limite(v, -100, 100) / 1000f); break;
                    case "altura_traseira_mm": a.DefinirAltura(1, Limite(v, -100, 100) / 1000f); a.TraseiraAntiga = true; break;
                    default:
                        if (chave.StartsWith("altura_eixo") && chave.EndsWith("_mm"))
                        {
                            int n;
                            if (int.TryParse(chave.Substring("altura_eixo".Length, chave.Length - "altura_eixo".Length - 3), out n) && n >= 1)
                                a.DefinirAltura(n - 1, Limite(v, -100, 100) / 1000f);
                        }
                        break;
                    case "ajoelhar_mm": a.Ajoelhar = Limite(v, 10, 150) / 1000f; break;
                    case "elevar_mm": a.Elevar = Limite(v, 10, 150) / 1000f; break;
                    case "rigidez": a.Rigidez = Limite(v, 0.4f, 2.5f); break;
                    case "amortecimento": a.Amortecimento = Limite(v, 0.4f, 2.5f); break;
                }
            }
            return a;
        }

        internal static void Gravar(string onibus, Ajustes a)
        {
            Directory.CreateDirectory(Pasta);
            var sb = new StringBuilder();
            sb.AppendLine("# Suspensão Ajustável - " + onibus);
            sb.AppendLine("# alturas por eixo (par de bolsas) em mm (+ sobe), da dianteira para trás; rigidez e amortecimento multiplicam os valores do ônibus");
            for (int i = 0; i < a.Alturas.Length; i++)
                sb.AppendLine($"altura_eixo{i + 1}_mm=" + (a.Alturas[i] * 1000f).ToString("0.#", Ci));
            sb.AppendLine("ajoelhar_mm=" + (a.Ajoelhar * 1000f).ToString("0.#", Ci));
            sb.AppendLine("elevar_mm=" + (a.Elevar * 1000f).ToString("0.#", Ci));
            sb.AppendLine("rigidez=" + a.Rigidez.ToString("0.###", Ci));
            sb.AppendLine("amortecimento=" + a.Amortecimento.ToString("0.###", Ci));
            var arq = Arquivo(onibus);
            File.WriteAllText(arq + ".tmp", sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(arq)) File.Delete(arq);
            File.Move(arq + ".tmp", arq);
        }

        static float Limite(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
