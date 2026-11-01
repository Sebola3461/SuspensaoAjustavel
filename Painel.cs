using System;
using System.Linq;
using PBSLoaderCommonUtils;
using UnityEngine;

namespace SuspensaoAjustavel
{
    // Painel (Ctrl+Alt+F3). IMGUI desenhado à mão no visual do PBSLoaderCommonInterface, igual ao do AjusteEspelhos:
    // painéis chapados escuros, destaque vermelho, seleção invertida em branco.
    internal static class Painel
    {
        internal static bool Aberto;
        static int _arrastando = -1;
        static string _dica = "";

        static Texture2D _fundo, _faixa, _trilho, _destaque, _branco, _verde;
        static GUIStyle _titulo, _texto, _fraco, _fracoDireita, _valor, _botao, _botaoAtivo, _dicaEstilo, _roda;

        static readonly Color CorFundo = new(0.07f, 0.075f, 0.085f, 0.96f);
        static readonly Color CorFaixa = new(0.12f, 0.125f, 0.14f, 1f);
        static readonly Color CorTrilho = new(0.22f, 0.23f, 0.25f, 1f);
        static readonly Color CorDestaque = new(0.839f, 0f, 0.184f, 1f);   // #D6002F
        static readonly Color CorTexto = new(0.93f, 0.94f, 0.95f, 1f);
        static readonly Color CorFraco = new(0.62f, 0.64f, 0.68f, 1f);

        internal static void Alternar()
        {
            Aberto = !Aberto;
            if (!Aberto) Suspensao.SalvarAgora();
        }

        // A cada frame: cursor solto enquanto o painel está aberto (quem decide o cursor é o PBSLoaderCommonUtils).
        internal static void ManterCursor() => Mouse.Soltar("SuspensaoAjustavel", Aberto);

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static GUIStyle Estilo(int tam, Color cor, FontStyle f = FontStyle.Normal, TextAnchor al = TextAnchor.MiddleLeft)
        {
            var s = new GUIStyle();
            s.fontSize = tam; s.fontStyle = f; s.alignment = al;
            s.normal.textColor = cor;
            s.wordWrap = false; s.clipping = TextClipping.Clip;
            return s;
        }

        static void Preparar()
        {
            if (_fundo != null) return;   // texturas morrem junto com a cena às vezes; recria
            _fundo = Tex(CorFundo); _faixa = Tex(CorFaixa); _trilho = Tex(CorTrilho);
            _destaque = Tex(CorDestaque); _branco = Tex(Color.white); _verde = Tex(new Color(0.2f, 0.75f, 0.45f, 1f));
            _titulo = Estilo(20, CorTexto, FontStyle.BoldAndItalic);
            _texto = Estilo(13, CorTexto);
            _fraco = Estilo(12, CorFraco);
            _fracoDireita = Estilo(12, CorFraco, FontStyle.Normal, TextAnchor.MiddleRight);
            _valor = Estilo(13, CorTexto, FontStyle.Bold, TextAnchor.MiddleRight);
            _roda = Estilo(12, CorTexto, FontStyle.Bold, TextAnchor.MiddleCenter);
            _dicaEstilo = Estilo(12, CorFraco); _dicaEstilo.wordWrap = true; _dicaEstilo.alignment = TextAnchor.UpperLeft;
            _botao = Estilo(12, CorTexto, FontStyle.Bold, TextAnchor.MiddleCenter);
            _botao.normal.background = _trilho; _botao.hover.background = _destaque; _botao.hover.textColor = Color.white;
            _botaoAtivo = Estilo(12, Color.black, FontStyle.Bold, TextAnchor.MiddleCenter);
            _botaoAtivo.normal.background = _branco; _botaoAtivo.hover.background = _branco; _botaoAtivo.hover.textColor = Color.black;
        }

        static void Caixa(Rect r, Texture2D t) => GUI.DrawTexture(r, t);

        internal static void Desenhar()
        {
            if (!Aberto) return;
            Preparar();
            var ev = Event.current;
            var a = Suspensao.A;
            float w = 440f, x0 = 24f, y0 = 24f, x = x0 + 20f, cw = w - 40f;
            float h = Mathf.Min(14f + 28f + 24f + 36f + 34f + 128f + 40f + 6 * 30f + 30f + 34f + 70f, Screen.height - 48f);
            var janela = new Rect(x0, y0, w, h);
            Caixa(janela, _fundo);
            Caixa(new Rect(janela.x, janela.y, 4f, h), _destaque);
            _dica = "";

            float y = y0 + 14f;
            GUI.Label(new Rect(x, y, cw, 26f), "SUSPENSÃO AJUSTÁVEL", _titulo);
            GUI.Label(new Rect(x, y, cw, 26f), Cfg.TeclaPainel.Combinacao.Legivel() + " fecha", _fracoDireita);
            y += 28f;
            string aviso = Suspensao.AvisoAtual;
            GUI.Label(new Rect(x, y, cw, 18f), aviso.Length > 0 ? aviso : (Suspensao.Onibus.Length > 0 ? Suspensao.Onibus + "  ·  " : "") + Suspensao.Estado, _fraco);
            y += 24f;
            if (!Suspensao.Pronto) { Rodape(janela); return; }

            // Níveis.
            float bw = (cw - 2 * 6f) / 3f;
            Nivel[] niveis = { Nivel.Ajoelhado, Nivel.Normal, Nivel.Elevado };
            string[] nomes = { "AJOELHAR", "NORMAL", "ELEVAR" };
            string[] dicas =
            {
                $"Baixa o lado das portas para o embarque. Só parado (até {Cfg.LimiteAjoelhado.Value:0} km/h). Tecla {Cfg.TeclaAjoelhar.Combinacao.Legivel()}.",
                "Altura de rodagem.",
                $"Sobe o ônibus para valetas, lombadas e rampas. Volta sozinho acima de {Cfg.LimiteElevado.Value:0} km/h. Tecla {Cfg.TeclaElevar.Combinacao.Legivel()}.",
            };
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(x + i * (bw + 6f), y, bw, 30f);
                if (GUI.Button(r, nomes[i], Suspensao.Pedido == niveis[i] ? _botaoAtivo : _botao)) Suspensao.Pedir(niveis[i]);
                if (r.Contains(ev.mousePosition)) _dica = dicas[i];
            }
            y += 36f;
            bool auto = Cfg.AutoAjoelhar.Value;
            var rAuto = new Rect(x, y, cw, 28f);
            if (GUI.Button(rAuto, "AJOELHAR SOZINHO AO ABRIR A PORTA", auto ? _botaoAtivo : _botao)) { Cfg.AutoAjoelhar.Value = !auto; MelonLoader.MelonPreferences.Save(); }
            if (rAuto.Contains(ev.mousePosition)) _dica = "Parado com porta aberta do lado " + (Suspensao.LadoPortas > 0 ? "direito" : "esquerdo") + " ajoelha; ao fechar as portas, levanta.";
            y += 34f;

            y = Diagrama(x, y, cw);

            // Reservatório.
            float p = Suspensao.Pressao;
            GUI.Label(new Rect(x + 2f, y, 120f, 26f), "Reservatório", _texto);
            var tr = new Rect(x + 126f, y + 9f, cw - 196f, 8f);
            Caixa(tr, _trilho);
            Caixa(new Rect(tr.x, tr.y, tr.width * Mathf.Clamp01(p / 10f), tr.height), p < 6f ? _destaque : _verde);
            GUI.Label(new Rect(x + cw - 64f, y, 62f, 26f), p.ToString("0.0") + " bar", _valor);
            if (new Rect(x, y, cw, 26f).Contains(ev.mousePosition))
                _dica = !Cfg.ArPeloMotor.Value ? "Reservatório desligado nas preferências (ArPeloMotor)."
                    : Ar.Simulando
                    ? "Mesmo reservatório do freio e das portas (simulação de ar do PBSLoaderCommonUtils). Abaixo de 6 bar a bolsa sobe devagar; abaixo de 4,5 não sobe."
                    : "O compressor só enche com o motor ligado (liga em 8,5 e desliga em 10 bar). Abaixo de 6 bar a bolsa sobe devagar; abaixo de 4,5 não sobe.";
            y += 26f;
            GUI.Label(new Rect(x + 2f, y, cw, 14f), Suspensao.SemAr ? "sem ar: as bolsas estão murchando até o batente"
                : Suspensao.Compressor ? "compressor enchendo" : (Suspensao.Ctrl != null && Suspensao.Ctrl.motorLigado ? "compressor parado" : "motor desligado: sem compressor"), _fraco);
            y += 20f;

            // Ajustes do ônibus.
            y = Deslizante(0, x, y, cw, "Altura dianteira", a.AlturaDianteira * 1000f, -100f, 100f, 0f, Mm,
                v => { a.AlturaDianteira = Mathf.Round(v) / 1000f; Suspensao.Sujar(); }, ev, "Altura de rodagem do eixo dianteiro em relação ao original do ônibus.");
            y = Deslizante(1, x, y, cw, "Altura traseira", a.AlturaTraseira * 1000f, -100f, 100f, 0f, Mm,
                v => { a.AlturaTraseira = Mathf.Round(v) / 1000f; Suspensao.Sujar(); }, ev, "Altura de rodagem dos eixos de trás.");
            y = Deslizante(2, x, y, cw, "Ajoelhamento", a.Ajoelhar * 1000f, 20f, 120f, 20f, v => v.ToString("0") + " mm",
                v => { a.Ajoelhar = Mathf.Round(v) / 1000f; Suspensao.Sujar(); }, ev, "Quanto o lado das portas baixa ao ajoelhar. Real: 60 a 90 mm.");
            y = Deslizante(3, x, y, cw, "Elevação", a.Elevar * 1000f, 20f, 120f, 20f, v => v.ToString("0") + " mm",
                v => { a.Elevar = Mathf.Round(v) / 1000f; Suspensao.Sujar(); }, ev, "Quanto o ônibus sobe ao elevar. Real: 50 a 80 mm.");
            y = Deslizante(4, x, y, cw, "Rigidez", a.Rigidez, 0.5f, 2f, 1f, v => (v * 100f).ToString("0") + "%",
                v => { a.Rigidez = Mathf.Round(v * 100f) / 100f; Suspensao.Sujar(); }, ev, "Mola em relação ao ônibus original. Mais pressão na bolsa = mais firme.");
            y = Deslizante(5, x, y, cw, "Amortecimento", a.Amortecimento, 0.5f, 2f, 1f, v => (v * 100f).ToString("0") + "%",
                v => { a.Amortecimento = Mathf.Round(v * 100f) / 100f; Suspensao.Sujar(); }, ev, "Amortecedor em relação ao original. Pouco = balança e quica; muito = seco.");
            float fd = Suspensao.Frequencia(true), ft = Suspensao.Frequencia(false);
            var rf = new Rect(x + 2f, y, cw, 24f);
            GUI.Label(rf, $"Frequência natural: dianteira {fd:0.00} Hz · traseira {ft:0.00} Hz", _fraco);
            if (rf.Contains(ev.mousePosition)) _dica = "Ônibus com bolsa de ar costumam ficar perto de 1,0 a 1,5 Hz; com feixe de molas, acima de 1,8 Hz.";
            y += 30f;

            if (GUI.Button(new Rect(x, y, bw, 28f), "ZERAR", _botao)) { Suspensao.A = new Ajustes(); Suspensao.Sujar(); }
            Rodape(janela);
        }

        static string Mm(float v) => (v > 0.5f ? "+" : "") + v.ToString("0") + " mm";

        // Vista de cima: cada roda com a altura atual do canto; vermelho baixando, verde subindo.
        static float Diagrama(float x, float y, float cw)
        {
            var rodas = Suspensao.Rodas.Where(r => r.W != null && !r.W.WasCollected).ToList();
            if (rodas.Count == 0) return y;
            float altura = 120f;
            var area = new Rect(x, y, cw, altura);
            Caixa(area, _faixa);
            float zMin = rodas.Min(r => r.Z), zMax = rodas.Max(r => r.Z);
            float corpoX = x + cw * 0.5f - 60f;
            Caixa(new Rect(corpoX, y + 8f, 120f, altura - 16f), _trilho);
            GUI.Label(new Rect(corpoX, y + 8f, 120f, 16f), "frente", _roda);
            foreach (var r in rodas)
            {
                float t = zMax - zMin > 0.1f ? (zMax - r.Z) / (zMax - zMin) : 0f;
                float ry = y + 26f + t * (altura - 52f);
                float rx = r.Lado > 0 ? corpoX + 120f + 4f : corpoX - 4f - 70f;
                var tex = r.Taxa < -0.002f ? _destaque : r.Taxa > 0.002f ? _verde : _trilho;
                var rr = new Rect(rx, ry - 11f, 70f, 22f);
                Caixa(rr, tex);
                GUI.Label(rr, Mm(r.H * 1000f), _roda);
            }
            return y + altura + 10f;
        }

        static void Rodape(Rect janela)
        {
            float x = janela.x + 20f, cw = janela.width - 40f, dicaH = 40f;
            var dr = new Rect(x, janela.yMax - dicaH - 10f, cw, dicaH);
            Caixa(new Rect(x, dr.y - 6f, cw, 1f), _trilho);
            GUI.Label(dr, !string.IsNullOrEmpty(_dica) ? _dica : "Os ajustes valem na hora e ficam salvos para este ônibus.", _dicaEstilo);
        }

        static float Deslizante(int id, float x, float y, float cw, string rotulo, float valor, float min, float max, float zero,
                                Func<float, string> fmt, Action<float> definir, Event ev, string dica)
        {
            var r = new Rect(x - 4f, y, cw + 8f, 30f);
            if (r.Contains(ev.mousePosition)) { Caixa(r, _faixa); _dica = dica; }
            GUI.Label(new Rect(x + 2f, y, 120f, 30f), rotulo, _texto);
            var trilho = new Rect(x + 126f, y + 11f, cw - 196f, 8f);
            float t = Mathf.InverseLerp(min, max, valor);
            Caixa(trilho, _trilho);
            float z = Mathf.InverseLerp(min, max, zero);
            float a = Mathf.Min(z, t), b = Mathf.Max(z, t);
            Caixa(new Rect(trilho.x + trilho.width * a, trilho.y, trilho.width * (b - a), trilho.height), _destaque);
            Caixa(new Rect(trilho.x + trilho.width * t - 3f, trilho.y - 5f, 6f, trilho.height + 10f), _branco);
            GUI.Label(new Rect(x + cw - 64f, y, 62f, 30f), fmt(valor), _valor);

            var alvo = new Rect(trilho.x - 6f, trilho.y - 10f, trilho.width + 12f, trilho.height + 20f);
            var tipo = ev.type;
            if (tipo == EventType.MouseDown && ev.button == 0 && alvo.Contains(ev.mousePosition)) { _arrastando = id; ev.Use(); }
            if (_arrastando == id && (tipo == EventType.MouseDrag || tipo == EventType.MouseDown))
            {
                float nt = Mathf.Clamp01((ev.mousePosition.x - trilho.x) / trilho.width);
                definir(Mathf.Lerp(min, max, nt));
                ev.Use();
            }
            if (tipo == EventType.MouseUp && _arrastando == id) { _arrastando = -1; ev.Use(); }
            return y + 30f;
        }
    }
}
