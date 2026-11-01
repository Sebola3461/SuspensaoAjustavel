using System;
using MelonLoader;
using PBSLoaderCommonUtils;
using UnityEngine;

[assembly: MelonInfo(typeof(SuspensaoAjustavel.SuspensaoAjustavelMod), "SuspensaoAjustavel", "0.2.0", "nzrn")]
[assembly: MelonGame(null, "Proton Bus Simulator")]

namespace SuspensaoAjustavel
{
    // Suspensão ajustável a ar no Proton.
    // O ônibus do jogador é Vehicle Physics Pro: cada roda é um VPWheelCollider que, a cada passo
    // físico, relê center/springRate/damperRate e repassa ao WheelCollider do Unity
    // (FixedUpdateVehicle -> UpdateWheelCollider). Baixar center.y de uma roda sobe a carroceria
    // naquele canto com a roda no chão, que é o que a bolsa de ar faz.
    // Módulos:
    //   Suspensao  acha as rodas, níveis (normal / ajoelhado / elevado), válvulas e reservatório
    //   Som        chiado do ar (escape ao baixar, enchimento ao subir) a partir de um WAV
    //   Salvos     ajustes por ônibus em UserData/SuspensaoAjustavel/<ônibus>.txt
    //   Painel     janela de ajuste
    // Linha de comando: --suspensao-teste roda ajoelhar/elevar/normal e grava teste.txt;
    // --suspensao-sair fecha o jogo depois.
    public class SuspensaoAjustavelMod : MelonMod
    {
        float _proxima;

        public override void OnLateInitializeMelon() => UiIntegracao.Registrar();


        public override void OnInitializeMelon()
        {
            Cfg.Criar();
            Seguro(Som.Carregar, "Som.Carregar");
            Ganchos.DepoisDaRodaVisual += Visual.DepoisDaRoda;
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            _proxima = 0f;
            Suspensao.CenaMudou();
        }

        public override void OnUpdate()
        {
            if (Time.unscaledTime >= _proxima)
            {
                _proxima = Time.unscaledTime + 1f;
                Seguro(Suspensao.Manter, "Suspensao");
            }
            Seguro(Suspensao.Quadro, "Quadro");
            Seguro(Som.Quadro, "Som");
            Seguro(Teste.Quadro, "Teste");
            Painel.ManterCursor();
        }

        public override void OnFixedUpdate() => Seguro(Suspensao.Passo, "Passo");

        public override void OnGUI() => Seguro(Painel.Desenhar, "Painel");

        public override void OnApplicationQuit() => Seguro(Suspensao.SalvarAgora, "Salvar");

        internal static void Seguro(Action a, string onde)
        {
            try { a(); }
            catch (Exception e) { Log.Erro(onde, e); }
        }
    }

    internal static class Log
    {
        static readonly System.Collections.Generic.Dictionary<string, float> _ultimo = new();
        static readonly System.Collections.Generic.HashSet<string> _comPilha = new();
        internal static void Msg(string s) => Melon<SuspensaoAjustavelMod>.Logger.Msg(s);
        internal static void Aviso(string s) => Melon<SuspensaoAjustavelMod>.Logger.Warning(s);
        // Erros repetidos (todo frame) aparecem no máximo a cada 10 s; a pilha só na primeira vez.
        internal static void Erro(string onde, Exception e)
        {
            float agora = Time.realtimeSinceStartup;
            if (_ultimo.TryGetValue(onde, out float t) && agora - t < 10f) return;
            _ultimo[onde] = agora;
            bool primeiro = _comPilha.Add(onde);
            Melon<SuspensaoAjustavelMod>.Logger.Warning(primeiro ? $"{onde}: {e}" : $"{onde}: {e.GetType().Name}: {e.Message}");
        }
    }

    internal static class Cfg
    {
        internal static MelonPreferences_Category Cat;
        // Atalhos (categoria "Atalhos", compartilhada pelos mods). Ajoelhar e elevar são comandos de dirigir:
        // tecla única, Page Down / Page Up, que o jogo não usa.
        internal static Atalho TeclaPainel, TeclaDiagnostico, TeclaAjoelhar, TeclaElevar;
        internal static MelonPreferences_Entry<bool> Ativo, AutoAjoelhar, ArPeloMotor;
        internal static MelonPreferences_Entry<string> LadoAjoelhar, ArquivoSom;
        internal static MelonPreferences_Entry<float> VelocidadeBaixar, VelocidadeSubir, LimiteAjoelhado, LimiteElevado,
            AjoelharOutroLado, AjoelharTraseira, PressaoInicial, Volume, VolumeEnchimento, VolumeDentro, AlturaSemAr, VelocidadeSemAr;

        internal static void Criar()
        {
            var c = Cat = MelonPreferences.CreateCategory("SuspensaoAjustavel", "SuspensaoAjustavel (suspensão ajustável a ar)");
            Ativo = c.CreateEntry("Ativo", true, description: "Liga o mod. Desligado, as rodas voltam aos valores do ônibus");
            const string M = "SuspensaoAjustavel";
            TeclaPainel = Atalhos.Registrar(M, "Painel", "Painel da suspensão", "Ctrl+Alt+F3", Painel.Alternar);
            TeclaDiagnostico = Atalhos.Registrar(M, "Diagnostico", "Diagnóstico da suspensão", "Ctrl+Alt+Shift+F3", Suspensao.Diagnostico,
                "Grava UserData/SuspensaoAjustavel/diagnostico.txt");
            TeclaAjoelhar = Atalhos.Registrar(M, "Ajoelhar", "Ajoelhar", "PageDown", () => Suspensao.PedirAlternar(Nivel.Ajoelhado), "Ajoelha / volta ao normal");
            TeclaElevar = Atalhos.Registrar(M, "Elevar", "Elevar", "PageUp", () => Suspensao.PedirAlternar(Nivel.Elevado), "Eleva / volta ao normal");
            AutoAjoelhar = c.CreateEntry("AutoAjoelhar", true, description: "Ajoelha sozinho parado com porta aberta do lado do ajoelhamento e levanta quando as portas fecham");
            LadoAjoelhar = c.CreateEntry("LadoAjoelhar", "auto", description: "Lado que baixa ao ajoelhar: auto (lado com mais portas), direito ou esquerdo");
            AjoelharOutroLado = c.CreateEntry("AjoelharOutroLado", 0.2f, description: "Quanto o lado sem portas baixa junto, em fração do ajoelhamento");
            AjoelharTraseira = c.CreateEntry("AjoelharTraseira", 0.8f, description: "Quanto os eixos de trás baixam, em fração do dianteiro");
            VelocidadeBaixar = c.CreateEntry("VelocidadeBaixar", 0.045f, description: "Velocidade da bolsa esvaziando (m/s); real ~0,04");
            VelocidadeSubir = c.CreateEntry("VelocidadeSubir", 0.018f, description: "Velocidade da bolsa enchendo com o reservatório cheio (m/s); real ~0,015-0,02");
            AlturaSemAr = c.CreateEntry("AlturaSemAr", 0.09f, description: "Quanto o ônibus baixa com as bolsas vazias, até o batente (m)");
            VelocidadeSemAr = c.CreateEntry("VelocidadeSemAr", 0.004f, description: "Velocidade das bolsas murchando quando o reservatório fica sem ar (m/s)");
            LimiteAjoelhado = c.CreateEntry("LimiteAjoelhado", 5f, description: "Acima disso (km/h) o ônibus sai do ajoelhamento sozinho");
            LimiteElevado = c.CreateEntry("LimiteElevado", 30f, description: "Acima disso (km/h) o ônibus sai da elevação sozinho");
            ArPeloMotor = c.CreateEntry("ArPeloMotor", true, description: "Simula o reservatório: o compressor só enche com o motor ligado e pouca pressão deixa a subida lenta");
            PressaoInicial = c.CreateEntry("PressaoInicial", 9f, description: "Pressão do reservatório ao carregar o ônibus (bar)");
            ArquivoSom = c.CreateEntry("ArquivoSom", "", description: "WAV do ar comprimido (PCM 16 bits). Vazio = o som embutido no mod");
            Volume = c.CreateEntry("Volume", 0.8f, description: "Volume do escape de ar ao baixar");
            VolumeEnchimento = c.CreateEntry("VolumeEnchimento", 0.3f, description: "Volume do enchimento ao subir, em fração do escape");
            VolumeDentro = c.CreateEntry("VolumeDentro", 0.45f, description: "Multiplicador do volume com a câmera dentro do ônibus");
        }
    }
}
