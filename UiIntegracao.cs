using System;
using MelonLoader;

[assembly: MelonAdditionalDependencies("PBSLoaderCommonUtils", "PBSLoaderCommonInterface")]

namespace SuspensaoAjustavel
{
    // Coloca o mod no menu da PBSLoaderCommonInterface (ações sem atalho de teclado e sliders com faixa). Ela é base do PBSLoader,
    // sempre instalada junto com a PBSLoaderCommonUtils.
    internal static class UiIntegracao
    {
        internal static void Registrar()
        {
            try { Fazer(); }
            catch (Exception e) { MelonLogger.Warning("Integração com a PBSLoaderCommonInterface falhou: " + e.Message); }
        }

        static void Fazer()
        {
            const string C = "SuspensaoAjustavel";
            PBSLoaderCommonInterface.PainelMods.Nome(C, "Suspensão Ajustável");
            PBSLoaderCommonInterface.PainelMods.Acao(C, "Painel da suspensão", Painel.Alternar, "Altura, ajoelhamento e calibração por ônibus");
            PBSLoaderCommonInterface.PainelMods.Acao(C, "Ajoelhar ou voltar ao normal", () => Suspensao.PedirAlternar(Nivel.Ajoelhado));
            PBSLoaderCommonInterface.PainelMods.Acao(C, "Elevar ou voltar ao normal", () => Suspensao.PedirAlternar(Nivel.Elevado));
            PBSLoaderCommonInterface.PainelMods.Acao(C, "Gravar diagnóstico", Suspensao.Diagnostico, "UserData/SuspensaoAjustavel/diagnostico.txt");
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "AjoelharOutroLado", 0f, 1f, 0.05f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "AjoelharTraseira", 0f, 1f, 0.05f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "VelocidadeBaixar", 0.01f, 0.2f, 0.005f);
        }
    }
}
