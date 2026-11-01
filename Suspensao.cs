using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Il2Cpp;
using Il2CppMep;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SuspensaoAjustavel
{
    internal enum Nivel { Normal, Ajoelhado, Elevado }

    // Uma roda do ônibus do jogador e a "bolsa" dela.
    internal sealed class Roda
    {
        internal VPWheelCollider W;
        internal Rigidbody Rb;
        internal bool Dianteira;
        internal int Eixo;                 // eixo (par de bolsas) ao qual a roda pertence, da frente para trás
        internal int Lado;                 // +1 direita, -1 esquerda (x local do corpo dela)
        internal float Z;                  // posição ao longo do ônibus, no corpo principal
        // Valores do ônibus. Se o jogo trocar o campo por conta própria (recarregar o veículo),
        // o novo valor vira o original.
        internal Vector3 CentroOrig;
        internal float MolaOrig, AmortOrig;
        internal Vector3 CentroEscrito;
        internal float MolaEscrita, AmortEscrita;
        internal bool Escreveu;
        internal float H;                  // quanto a carroceria está acima do normal neste canto (m)
        internal float Alvo;
        internal float Taxa;               // dH/dt do último passo (m/s)
    }

    // Um eixo do ônibus: o par de bolsas (roda esquerda + direita) na mesma altura Z.
    internal sealed class Eixo
    {
        internal readonly List<Roda> Rodas = new();
        internal float Z;
        internal bool Dianteira;
    }

    // Ajustes salvos por ônibus.
    internal sealed class Ajustes
    {
        internal float[] Alturas = new float[2];   // uma altura por eixo (m, + sobe), da dianteira para trás
        internal bool TraseiraAntiga;              // arquivo do formato antigo: a altura traseira valia para todos os eixos de trás
        internal float Ajoelhar = 0.08f, Elevar = 0.06f;  // m
        internal float Rigidez = 1f, Amortecimento = 1f;  // fator sobre o valor do ônibus

        internal float Altura(int eixo) => eixo >= 0 && eixo < Alturas.Length ? Alturas[eixo] : 0f;

        internal void DefinirAltura(int eixo, float v)
        {
            if (eixo < 0) return;
            if (eixo >= Alturas.Length) Array.Resize(ref Alturas, eixo + 1);
            Alturas[eixo] = v;
        }

        // Ajusta o número de alturas ao número de eixos do ônibus (faltando, preenche com 0).
        internal void Garantir(int eixos)
        {
            if (Alturas.Length == eixos) return;
            var novo = new float[eixos];
            Array.Copy(Alturas, novo, Math.Min(Alturas.Length, eixos));
            Alturas = novo;
        }
    }

    internal static class Suspensao
    {
        internal static readonly List<Roda> Rodas = new();
        internal static readonly List<Eixo> Eixos = new();
        internal static Ajustes A = new();
        internal static string Onibus = "";
        internal static string Estado = "Nenhum ônibus";
        internal static bool Pronto;
        internal static Nivel Pedido = Nivel.Normal;
        internal static bool Automatico;          // o ajoelhamento atual veio das portas
        internal static float Pressao = 9f;       // reservatório (bar)
        internal static bool Compressor;
        internal static bool SemAr;               // reservatório abaixo da pressão das bolsas: elas murcham
        internal static int LadoPortas = 1;
        internal static bool PortaAberta;         // porta aberta do lado que ajoelha
        internal static float Kmh;
        internal static string Aviso = ""; static float _avisoAte;
        internal static Transform Corpo;          // transform do corpo principal (para o som e o painel)
        internal static Bounds Caixa;             // caixa aproximada da cabine, no espaço do Corpo

        static MEPControlesBus _ctrl;
        static IntPtr _ctrlPtr;
        static Transform _raiz;
        static float _desde;
        static readonly List<MEPCadaPortaBus> _portas = new();
        static bool _autoBloqueado;               // o motorista mexeu com porta aberta: espera fechar
        static bool _sujo; static float _sujoEm;
        static bool _restaurou = true;

        const float PressaoMax = 10f, PressaoLiga = 8.5f, PressaoMin = 4.5f;

        internal static MEPControlesBus Ctrl => _ctrl;

        internal static void CenaMudou()
        {
            SalvarAgora();
            Restaurar();
            Rodas.Clear(); Eixos.Clear(); _portas.Clear(); Visual.Mapa.Clear();
            _ctrl = null; _ctrlPtr = IntPtr.Zero; _raiz = null; Corpo = null;
            Pronto = false; Pedido = Nivel.Normal; Automatico = false; _autoBloqueado = false;
            Estado = "Nenhum ônibus";
            Som.Desmontar();
        }

        internal static void Sujar() { _sujo = true; _sujoEm = Time.unscaledTime; }

        internal static void SalvarAgora()
        {
            if (!_sujo || Onibus.Length == 0) { _sujo = false; return; }
            _sujo = false;
            Salvos.Gravar(Onibus, A);
        }

        internal static void Avisar(string s) { Aviso = s; _avisoAte = Time.unscaledTime + 3f; Log.Msg(s); }
        internal static string AvisoAtual => Time.unscaledTime < _avisoAte ? Aviso : "";

        // ---------------- descoberta (uma vez por segundo) ----------------

        internal static void Manter()
        {
            var c = Avancado.Controles;
            if (c == null)
            {
                if (_ctrl != null) CenaMudou();
                return;
            }
            if (c.Pointer != _ctrlPtr)
            {
                CenaMudou();
                _ctrl = c; _ctrlPtr = c.Pointer; _raiz = c.transform.root;
                _desde = Time.unscaledTime;
                Pressao = Cfg.PressaoInicial.Value;
                Estado = "Esperando o ônibus terminar de carregar";
            }
            if (_sujo && Time.unscaledTime - _sujoEm > 1.5f) SalvarAgora();
            if (Time.unscaledTime - _desde < 3f) return;

            bool mudou = Rodas.Count == 0 || Rodas.Any(r => r.W == null || r.W.WasCollected);
            if (!mudou)
            {
                var agora = _raiz.GetComponentsInChildren<VPWheelCollider>(true);
                mudou = agora.Length != Rodas.Count;
            }
            if (mudou) Montar();
            AcharPortas();
        }

        static void Montar()
        {
            Restaurar();
            Rodas.Clear(); Visual.Mapa.Clear();
            Pronto = false;
            var ws = _raiz.GetComponentsInChildren<VPWheelCollider>(true);
            if (ws.Length == 0) { Estado = "Ônibus sem rodas VPP"; return; }

            var corpo = _ctrl.corpoRigido;
            Corpo = corpo != null ? corpo.transform : ws[0].GetComponentInParent<Rigidbody>()?.transform ?? _raiz;
            foreach (var w in ws)
            {
                var rb = w.GetComponentInParent<Rigidbody>();
                var rbT = rb != null ? rb.transform : Corpo;
                var r = new Roda
                {
                    W = w, Rb = rb,
                    Lado = rbT.InverseTransformPoint(w.transform.position).x >= 0f ? 1 : -1,
                    Z = Corpo.InverseTransformPoint(w.transform.position).z,
                    CentroOrig = w.center, MolaOrig = w.springRate, AmortOrig = w.damperRate,
                };
                Rodas.Add(r);
                Visual.Mapa[w.Pointer] = r;
            }
            // Eixos: pares de bolsas agrupados pela posição Z (da frente para trás). Articulados e
            // biarticulados ganham um ajuste por eixo em vez de só dianteira/traseira.
            Eixos.Clear();
            foreach (var r in Rodas.OrderByDescending(r => r.Z))
            {
                if (Eixos.Count == 0 || Mathf.Abs(Eixos[^1].Z - r.Z) > 0.3f)
                    Eixos.Add(new Eixo { Z = r.Z });
                var eixo = Eixos[^1];
                eixo.Rodas.Add(r);
                r.Eixo = Eixos.Count - 1;
            }
            for (int i = 0; i < Eixos.Count; i++) Eixos[i].Dianteira = i == 0;
            foreach (var r in Rodas) r.Dianteira = r.Eixo == 0;
            float zMax = Eixos.Count > 0 ? Eixos[0].Z : 0f;

            // Caixa da cabine a partir das rodas: largura das rodas + folga, do chão ao teto.
            float xMax = 0f, yMin = float.MaxValue, zMin = float.MaxValue;
            foreach (var r in Rodas)
            {
                var p = Corpo.InverseTransformPoint(r.W.transform.position);
                xMax = Mathf.Max(xMax, Mathf.Abs(p.x)); yMin = Mathf.Min(yMin, p.y); zMin = Mathf.Min(zMin, p.z);
            }
            var min = new Vector3(-xMax - 0.35f, yMin - 0.3f, zMin - 3.2f);
            var max = new Vector3(xMax + 0.35f, yMin + 3.0f, zMax + 2.8f);
            Caixa = new Bounds((min + max) * 0.5f, max - min);

            Onibus = NomeDoOnibus();
            A = Salvos.Carregar(Onibus) ?? new Ajustes();
            A.Garantir(Eixos.Count);
            // Formato antigo (dianteira/traseira): a traseira valia para todos os eixos de trás, meio incluso.
            if (A.TraseiraAntiga)
                for (int i = 1; i < Eixos.Count; i++) A.DefinirAltura(i, A.Altura(1));
            Pronto = true;
            _restaurou = false;
            Estado = $"{Rodas.Count} bolsas · {Eixos.Count} eixos";
            Log.Msg($"Ônibus '{Onibus}': {Rodas.Count} rodas VPP, mola {Rodas[0].MolaOrig:0} N/m, amortecedor {Rodas[0].AmortOrig:0} N·s/m");
            Som.Montar(Corpo, _raiz, PosicaoSom());
        }

        static Vector3 PosicaoSom()
        {
            // Entre os eixos, embaixo do assoalho.
            var c = Caixa.center;
            return new Vector3(0f, Caixa.min.y + 0.5f, c.z);
        }

        static void AcharPortas()
        {
            _portas.Clear();
            if (_raiz == null) return;
            int dir = 0, esq = 0;
            foreach (var p in _raiz.GetComponentsInChildren<MEPCadaPortaBus>(true))
            {
                _portas.Add(p);
                if (Corpo != null && Corpo.InverseTransformPoint(p.transform.position).x >= 0f) dir++; else esq++;
            }
            switch ((Cfg.LadoAjoelhar.Value ?? "").Trim().ToLowerInvariant())
            {
                case "direito": case "direita": LadoPortas = 1; break;
                case "esquerdo": case "esquerda": LadoPortas = -1; break;
                default: LadoPortas = esq > dir ? -1 : 1; break;
            }
        }

        // ---------------- níveis ----------------

        internal static void PedirAlternar(Nivel n) => Pedir(Pedido == n ? Nivel.Normal : n);

        internal static void Pedir(Nivel n)
        {
            if (!Pronto) return;
            if (n == Nivel.Ajoelhado && Kmh > Cfg.LimiteAjoelhado.Value) { Avisar($"Ajoelhar só abaixo de {Cfg.LimiteAjoelhado.Value:0} km/h"); return; }
            if (n == Nivel.Elevado && Kmh > Cfg.LimiteElevado.Value) { Avisar($"Elevar só abaixo de {Cfg.LimiteElevado.Value:0} km/h"); return; }
            if (n != Pedido) Log.Msg("Nível: " + n);
            Pedido = n;
            Automatico = false;
            _autoBloqueado = PortaAberta;   // com a porta aberta, o automático respeita o motorista
        }

        // Todo quadro: velocidade, portas, travas e reservatório.
        internal static void Quadro()
        {
            if (!Pronto || _ctrl == null) return;
            Kmh = Mathf.Abs(_ctrl.speedkmh);

            bool aberta = false;
            foreach (var p in _portas)
            {
                if (p == null || p.WasCollected || !p.tahAberta) continue;
                if (Corpo == null || Math.Sign(Corpo.InverseTransformPoint(p.transform.position).x) == LadoPortas || LadoPortas == 0) aberta = true;
            }
            PortaAberta = aberta;
            if (!aberta) _autoBloqueado = false;

            if (Pedido == Nivel.Ajoelhado && Kmh > Cfg.LimiteAjoelhado.Value) { Pedido = Nivel.Normal; Automatico = false; Avisar("Andando: saindo do ajoelhamento"); }
            if (Pedido == Nivel.Elevado && Kmh > Cfg.LimiteElevado.Value) { Pedido = Nivel.Normal; Avisar($"Acima de {Cfg.LimiteElevado.Value:0} km/h: voltando ao normal"); }

            if (Cfg.AutoAjoelhar.Value && !_autoBloqueado)
            {
                if (aberta && Pedido == Nivel.Normal && Kmh < 2f) { Pedido = Nivel.Ajoelhado; Automatico = true; Log.Msg("Porta aberta: ajoelhando"); }
                else if (!aberta && Automatico && Pedido == Nivel.Ajoelhado) { Pedido = Nivel.Normal; Automatico = false; Log.Msg("Portas fechadas: levantando"); }
            }

            float media = 0f;
            foreach (var r in Rodas) media += r.H;
            Veiculo.Suspensao.Informar(SemAr ? "sem ar" : Pedido.ToString().ToLowerInvariant(), Rodas.Count > 0 ? media / Rodas.Count : 0f);
            Avisos.Definir("SuspensaoAjustavel", "sem-ar", SemAr && Cfg.Ativo.Value, Gravidade.Aviso,
                "Suspensão sem ar", $"bolsas murchando · {Pressao:0.0} bar, precisam de {PressaoMin:0.0}");

            // Com a simulação de ar do PBSLoaderCommonUtils, o reservatório é o dela (o mesmo do freio e das portas).
            if (Ar.Simulando && Cfg.ArPeloMotor.Value)
            {
                Pressao = Ar.Pressao;
                Compressor = Ar.CompressorCarregando;
                return;
            }

            // Reservatório próprio: liga em 8,5 bar e desliga em 10 bar, só com o motor girando.
            float dt = Time.deltaTime;
            bool motor = _ctrl.motorLigado;
            if (!Cfg.ArPeloMotor.Value) { Pressao = PressaoMax; Compressor = false; }
            else
            {
                if (!motor || Pressao >= PressaoMax) Compressor = false;
                else if (Pressao < PressaoLiga) Compressor = true;
                if (Compressor) Pressao = Mathf.Min(PressaoMax, Pressao + 0.15f * dt);
            }
        }

        static float AlvoDe(Roda r)
        {
            float h = A.Altura(r.Eixo);
            switch (Pedido)
            {
                case Nivel.Ajoelhado:
                    h -= A.Ajoelhar * (r.Lado == LadoPortas ? 1f : Cfg.AjoelharOutroLado.Value) * (r.Eixo == 0 ? 1f : Cfg.AjoelharTraseira.Value);
                    break;
                case Nivel.Elevado:
                    h += A.Elevar;
                    break;
            }
            return h;
        }

        // ---------------- física (passo fixo) ----------------

        internal static void Passo()
        {
            if (!Pronto) return;
            if (!Cfg.Ativo.Value) { Restaurar(); return; }
            _restaurou = false;
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;

            // Enchendo, a bolsa sobe mais devagar com pouca pressão e para abaixo de 4,5 bar.
            float fatorAr = Cfg.ArPeloMotor.Value ? Mathf.Sqrt(Mathf.Clamp01((Pressao - PressaoMin) / (9f - PressaoMin))) : 1f;
            float sobe = Cfg.VelocidadeSubir.Value * fatorAr, desce = Cfg.VelocidadeBaixar.Value;
            // Sem ar: o reservatório não segura mais a pressão das bolsas, as válvulas niveladoras não repõem e as
            // bolsas murcham devagar até o batente. Volta (gastando ar) com 0,5 bar de folga, para não ficar oscilando.
            if (Cfg.ArPeloMotor.Value)
            {
                if (Pressao < PressaoMin) SemAr = true;
                else if (Pressao > PressaoMin + 0.5f) SemAr = false;
            }
            else SemAr = false;
            float gasto = 0f;
            bool mexeu = false;
            foreach (var r in Rodas)
            {
                if (r.W == null || r.W.WasCollected) continue;
                r.Alvo = AlvoDe(r);
                float desceAqui = desce;
                if (SemAr)
                {
                    float vazio = A.Altura(r.Eixo) - Cfg.AlturaSemAr.Value;
                    if (r.Alvo > vazio || r.H > vazio) { r.Alvo = vazio; desceAqui = Cfg.VelocidadeSemAr.Value; }
                }
                // A válvula fecha suave perto do alvo (constante de 0,15 s) e no meio do caminho
                // vai na vazão máxima.
                float v = Mathf.Clamp((r.Alvo - r.H) / 0.15f, -desceAqui, sobe);
                if (Mathf.Abs(r.Alvo - r.H) < 0.0004f) v = 0f;
                float dh = v * dt;
                r.H += dh;
                r.Taxa = v;
                if (dh > 0f) gasto += dh;
                if (dh != 0f) mexeu = true;
                Escrever(r);
            }
            // ~0,3 bar para subir 8 cm as quatro bolsas (num reservatório de 100 L: 95 L livres por metro somado).
            if (Cfg.ArPeloMotor.Value)
            {
                if (Ar.Simulando)
                {
                    Ar.Gastar("suspensao", gasto * 95f);
                    Pressao = Ar.Pressao;
                }
                else Pressao = Mathf.Max(0f, Pressao - gasto * 0.95f);
            }
            if (mexeu)
                foreach (var r in Rodas)
                    if (r.Rb != null && r.Rb.IsSleeping()) r.Rb.WakeUp();
        }

        static void Escrever(Roda r)
        {
            var w = r.W;
            if (r.Escreveu)
            {
                if (w.center != r.CentroEscrito) r.CentroOrig = w.center;
                if (w.springRate != r.MolaEscrita) r.MolaOrig = w.springRate;
                if (w.damperRate != r.AmortEscrita) r.AmortOrig = w.damperRate;
            }
            var c = r.CentroOrig; c.y -= r.H;
            float k = r.MolaOrig * A.Rigidez, d = r.AmortOrig * A.Amortecimento;
            w.center = c; w.springRate = k; w.damperRate = d;
            // Lê de volta: o campo guarda exatamente o float escrito.
            r.CentroEscrito = w.center; r.MolaEscrita = w.springRate; r.AmortEscrita = w.damperRate;
            r.Escreveu = true;
        }

        static void Restaurar()
        {
            if (_restaurou) return;
            _restaurou = true;
            foreach (var r in Rodas)
            {
                if (r.W == null || r.W.WasCollected || !r.Escreveu) continue;
                try
                {
                    if (r.W.center == r.CentroEscrito) r.W.center = r.CentroOrig;
                    if (r.W.springRate == r.MolaEscrita) r.W.springRate = r.MolaOrig;
                    if (r.W.damperRate == r.AmortEscrita) r.W.damperRate = r.AmortOrig;
                }
                catch { }
                r.Escreveu = false; r.H = 0f; r.Taxa = 0f;
            }
        }

        // ---------------- leitura para painel / diagnóstico ----------------

        // Nome do eixo para o painel e o diagnóstico.
        internal static string NomeEixo(int eixo) =>
            Eixos.Count <= 1 ? "único" : eixo == 0 ? "dianteira" : eixo == Eixos.Count - 1 ? "traseira" : $"meio {eixo + 1}";

        internal static float Frequencia(int eixo)
        {
            double soma = 0; int n = 0;
            foreach (var r in Rodas)
            {
                if (r.Eixo != eixo || r.W == null || r.W.WasCollected) continue;
                var wc = r.W.m_wheelCollider;
                if (wc == null) continue;
                float m = wc.sprungMass, k = r.W.effectiveSpringRate;
                if (m <= 1f || k <= 0f) continue;
                soma += Math.Sqrt(k / m) / (2 * Math.PI); n++;
            }
            return n > 0 ? (float)(soma / n) : 0f;
        }

        internal static bool Dentro()
        {
            var cam = Camera.main;
            if (cam == null || Corpo == null) return false;
            return Caixa.Contains(Corpo.InverseTransformPoint(cam.transform.position));
        }

        internal static void Diagnostico()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("SuspensaoAjustavel diagnóstico " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine($"Ônibus: {Onibus}  estado: {Estado}  pronto: {Pronto}");
            sb.AppendLine($"Nível: {Pedido} (automático {Automatico})  kmh {Kmh:0.0}  lado portas {LadoPortas}  porta aberta {PortaAberta}  portas {_portas.Count}");
            sb.AppendLine($"Pressão {Pressao:0.00} bar  compressor {Compressor}  motor {(_ctrl != null && _ctrl.motorLigado)}");
            sb.AppendLine($"Ajustes: {string.Join(" · ", A.Alturas.Select((v, i) => $"eixo {i + 1} {v * 1000:0} mm"))}, ajoelhar {A.Ajoelhar * 1000:0} mm, elevar {A.Elevar * 1000:0} mm, rigidez {A.Rigidez:0.00}, amort {A.Amortecimento:0.00}");
            sb.AppendLine("Frequência natural: " + string.Join(", ", Eixos.Select((e, i) => $"{NomeEixo(i)} {Frequencia(i):0.00} Hz")));
            sb.AppendLine($"Caixa da cabine (local do corpo): {Caixa}  câmera dentro: {Dentro()}");
            sb.AppendLine("roda | eixo | lado | z | H mm | alvo mm | taxa mm/s | center orig | center atual | mola orig/atual/efetiva | amort | massa susp | travel | anchor | y mundo");
            foreach (var r in Rodas)
            {
                if (r.W == null || r.W.WasCollected) { sb.AppendLine("(destruída)"); continue; }
                var w = r.W; var wc = w.m_wheelCollider;
                sb.AppendLine(string.Format(ci, "{0} | {1} | {2} | {3:0.00} | {4:0.0} | {5:0.0} | {6:0.0} | {7} | {8} | {9:0}/{10:0}/{11:0} | {12:0} | {13:0} | {14:0.000} | {15:0.00} | {16:0.0000}",
                    w.name, r.Eixo + 1, r.Lado, r.Z, r.H * 1000, r.Alvo * 1000, r.Taxa * 1000, V(r.CentroOrig), V(w.center),
                    r.MolaOrig, w.springRate, w.effectiveSpringRate, w.damperRate, wc != null ? wc.sprungMass : -1f,
                    w.suspensionDistance, w.suspensionAnchor, w.transform.position.y));
            }
            sb.AppendLine(Som.Diagnostico());
            var pasta = Salvos.Pasta;
            Directory.CreateDirectory(pasta);
            File.WriteAllText(Path.Combine(pasta, "diagnostico.txt"), sb.ToString(), new UTF8Encoding(false));
            Avisar("Diagnóstico gravado em UserData/SuspensaoAjustavel/diagnostico.txt");
        }

        static string NomeDoOnibus()
        {
            string n = Veiculo.Nome;
            if (string.IsNullOrWhiteSpace(n)) n = _raiz != null ? _raiz.name : "onibus";
            return n.Trim();
        }

        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.000}, {1:0.000}, {2:0.000})", v.x, v.y, v.z);
    }
}
