using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SuspensaoAjustavel
{
    // --suspensao-teste (junto com --pbsloadercommoninterface-partida): com o ônibus parado, mede a altura de cada
    // canto (y do VPWheelCollider no mundo) no normal, ajoelhado, elevado e de volta ao normal, e
    // grava UserData/SuspensaoAjustavel/teste.txt. --suspensao-sair fecha o jogo no fim.
    internal static class Teste
    {
        static readonly bool Ativo = Environment.GetCommandLineArgs().Contains("--suspensao-teste");
        static readonly bool Sair = Environment.GetCommandLineArgs().Contains("--suspensao-sair");
        static int _fase;
        static float _t;
        static float[] _y0, _yr0;
        static readonly StringBuilder _sb = new();
        static readonly (Nivel nivel, float espera, string nome)[] Roteiro =
        {
            (Nivel.Normal, 6f, "normal"),
            (Nivel.Ajoelhado, 6f, "ajoelhado"),
            (Nivel.Elevado, 10f, "elevado"),
            (Nivel.Normal, 7f, "normal de novo"),
        };

        internal static void Quadro()
        {
            if (!Ativo || !Suspensao.Pronto || _fase > Roteiro.Length) return;
            float agora = Time.unscaledTime;
            if (_fase == 0)
            {
                Cfg.AutoAjoelhar.Value = false;   // só nesta sessão; não é salvo
                _sb.AppendLine("SuspensaoAjustavel teste " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + Suspensao.Onibus);
                _sb.AppendLine($"ajoelhar {Suspensao.A.Ajoelhar * 1000:0} mm, elevar {Suspensao.A.Elevar * 1000:0} mm, lado portas {Suspensao.LadoPortas}, pressão {Suspensao.Pressao:0.0} bar");
                _fase = 1; _t = agora;
                Suspensao.Pedir(Roteiro[0].nivel);
                Som.TirarPicos();
                return;
            }
            var etapa = Roteiro[_fase - 1];
            if (agora - _t < etapa.espera) return;

            var rodas = Suspensao.Rodas;
            var y = rodas.Select(r => r.W != null && !r.W.WasCollected ? r.W.transform.position.y : float.NaN).ToArray();
            var yr = rodas.Select(r => r.W != null && !r.W.WasCollected && r.W.wheelTransform != null ? r.W.wheelTransform.position.y : float.NaN).ToArray();
            if (_y0 == null) { _y0 = y; _yr0 = yr; }
            var picos = Som.TirarPicos();
            _sb.AppendLine($"-- {etapa.nome}: pressão {Suspensao.Pressao:0.00} bar, som escape pico {picos.escape:0.00}, enchimento pico {picos.enchimento:0.00}");
            for (int i = 0; i < rodas.Count; i++)
            {
                var r = rodas[i];
                _sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   {0,-28} {1} {2}  H {3,6:0.0} mm (alvo {4,6:0.0})  canto medido {5,6:0.0} mm  roda desenhada {6,6:0.0} mm",
                    r.W != null ? r.W.name : "?", r.Dianteira ? "diant" : "tras ", r.Lado > 0 ? "dir" : "esq",
                    r.H * 1000, r.Alvo * 1000, (y[i] - _y0[i]) * 1000, (yr[i] - _yr0[i]) * 1000));
            }
            Log.Msg($"Teste: {etapa.nome} medido");

            if (_fase < Roteiro.Length)
            {
                Suspensao.Pedir(Roteiro[_fase].nivel);
                _fase++; _t = agora;
                return;
            }
            _fase++;
            Directory.CreateDirectory(Salvos.Pasta);
            File.WriteAllText(Path.Combine(Salvos.Pasta, "teste.txt"), _sb.ToString(), new UTF8Encoding(false));
            Suspensao.Diagnostico();
            Log.Msg("Teste da suspensão gravado em UserData/SuspensaoAjustavel/teste.txt" + (Sair ? ", fechando o jogo" : ""));
            if (Sair) Application.Quit();
        }
    }
}
