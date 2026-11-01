using System;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SuspensaoAjustavel
{
    // Chiado do ar a partir de um WAV só (o embutido é uma gravação de 6 s de ar comprimido).
    // Cada voz toca o arquivo do começo (o ataque da válvula abrindo) e, antes do fim, cruza para
    // um laço sem emenda feito do meio da gravação, então dura o quanto a bolsa estiver mexendo.
    //   Escape     bolsa esvaziando (baixar): alto, tom original
    //   Enchimento bolsa enchendo (subir): mais baixo, mais grave e abafado
    // O volume segue a vazão somada das bolsas.
    internal static class Som
    {
        sealed class Voz
        {
            internal GameObject Go;
            internal AudioSource Inicio, Laco;
            internal float G;           // envelope 0..1
            internal bool Tocando;
            internal float Pico;        // maior volume desde o último diagnóstico (para o teste)
        }

        static AudioClip _inicio, _laco;
        static float _durInicio;
        static Voz _escape, _enchimento;
        internal static string Origem = "";

        internal static void Carregar()
        {
            float[] x; int taxa;
            string arq = Cfg.ArquivoSom.Value;
            if (!string.IsNullOrWhiteSpace(arq))
            {
                if (!Path.IsPathRooted(arq)) arq = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, arq);
                x = LerWav(File.ReadAllBytes(arq), out taxa);
                Origem = arq;
            }
            else
            {
                using var s = typeof(Som).Assembly.GetManifestResourceStream("SuspensaoAjustavel.ar_comprimido.wav");
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                x = LerWav(ms.ToArray(), out taxa);
                Origem = "embutido";
            }
            if (x.Length < taxa / 2) throw new InvalidDataException("WAV curto demais");

            // Corta o silêncio do fim e 15 ms de rampa nas pontas (a gravação termina num corte seco).
            int fim = x.Length;
            while (fim > taxa / 2 && Math.Abs(x[fim - 1]) < 0.003f) fim--;
            int rampa = taxa * 15 / 1000;
            var inicio = new float[fim];
            Array.Copy(x, inicio, fim);
            for (int i = 0; i < rampa && i < fim; i++) inicio[fim - 1 - i] *= (float)i / rampa;
            _durInicio = (float)fim / taxa;

            // Laço: do meio (pula o ataque e o fim), com cruzamento de potência igual na emenda.
            int a = Math.Min(fim / 4, (int)(0.6f * taxa)), b = fim - (int)(0.2f * taxa);
            int cruz = Math.Min((int)(0.5f * taxa), (b - a) / 3);
            int n = b - a - cruz;
            var laco = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = x[a + i];
                if (i < cruz)
                {
                    double t = (double)i / cruz * Math.PI / 2;
                    v = (float)(v * Math.Sin(t) + x[a + n + i] * Math.Cos(t));
                }
                laco[i] = v;
            }

            _inicio = Clip("SuspensaoAjustavel_inicio", inicio, taxa);
            _laco = Clip("SuspensaoAjustavel_laco", laco, taxa);
            Log.Msg($"Som do ar: {Origem}, {taxa} Hz, {_durInicio:0.00} s, laço de {(float)n / taxa:0.00} s");
        }

        static AudioClip Clip(string nome, float[] dados, int taxa)
        {
            var c = AudioClip.Create(nome, dados.Length, 1, taxa, false);
            c.SetData(new Il2CppStructArray<float>(dados), 0);
            c.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Object.DontDestroyOnLoad(c);
            return c;
        }

        // PCM 8/16/24/32 bits ou float 32, qualquer número de canais (vira mono).
        static float[] LerWav(byte[] b, out int taxa)
        {
            if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
                throw new InvalidDataException("não é WAV");
            int p = 12, formato = 0, canais = 0, bits = 0; taxa = 0;
            while (p + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, p, 4);
                int tam = BitConverter.ToInt32(b, p + 4);
                int d = p + 8;
                if (id == "fmt ")
                {
                    formato = BitConverter.ToUInt16(b, d); canais = BitConverter.ToUInt16(b, d + 2);
                    taxa = BitConverter.ToInt32(b, d + 4); bits = BitConverter.ToUInt16(b, d + 14);
                    if (formato == 0xFFFE && tam >= 26) formato = BitConverter.ToUInt16(b, d + 24);
                }
                else if (id == "data")
                {
                    if (canais <= 0 || bits <= 0) throw new InvalidDataException("WAV sem fmt antes de data");
                    tam = Math.Min(tam, b.Length - d);
                    int bpa = bits / 8, quadro = bpa * canais, n = tam / quadro;
                    var x = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        float s = 0f;
                        for (int c = 0; c < canais; c++) s += Amostra(b, d + i * quadro + c * bpa, bits, formato);
                        x[i] = s / canais;
                    }
                    return x;
                }
                p = d + tam + (tam & 1);
            }
            throw new InvalidDataException("WAV sem dados");
        }

        static float Amostra(byte[] b, int i, int bits, int formato)
        {
            if (formato == 3 && bits == 32) return BitConverter.ToSingle(b, i);
            switch (bits)
            {
                case 8: return (b[i] - 128) / 128f;
                case 16: return BitConverter.ToInt16(b, i) / 32768f;
                case 24: return ((b[i] | (b[i + 1] << 8) | ((sbyte)b[i + 2] << 16))) / 8388608f;
                case 32: return BitConverter.ToInt32(b, i) / 2147483648f;
            }
            throw new InvalidDataException($"WAV de {bits} bits não suportado");
        }

        // ---------------- vozes no ônibus ----------------

        internal static void Montar(Transform corpo, Transform raiz, Vector3 local)
        {
            Desmontar();
            if (_inicio == null || corpo == null) return;
            UnityEngine.Audio.AudioMixerGroup mixer = null;
            foreach (var s in raiz.GetComponentsInChildren<AudioSource>(true))
                if (s.outputAudioMixerGroup != null) { mixer = s.outputAudioMixerGroup; break; }
            _escape = Criar("SuspensaoAjustavel_Escape", corpo, local, mixer, 1f, 0f);
            _enchimento = Criar("SuspensaoAjustavel_Enchimento", corpo, local, mixer, 0.78f, 1700f);
        }

        static Voz Criar(string nome, Transform corpo, Vector3 local, UnityEngine.Audio.AudioMixerGroup mixer, float pitch, float passaBaixa)
        {
            var v = new Voz { Go = new GameObject(nome) };
            v.Go.transform.SetParent(corpo, false);
            v.Go.transform.localPosition = local;
            v.Inicio = Fonte(v.Go, _inicio, false, mixer, pitch);
            v.Laco = Fonte(v.Go, _laco, true, mixer, pitch);
            if (passaBaixa > 0f)
            {
                var f = v.Go.AddComponent<AudioLowPassFilter>();
                f.cutoffFrequency = passaBaixa;
            }
            return v;
        }

        static AudioSource Fonte(GameObject go, AudioClip clip, bool laco, UnityEngine.Audio.AudioMixerGroup mixer, float pitch)
        {
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = laco; s.playOnAwake = false;
            s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 4f; s.maxDistance = 120f; s.dopplerLevel = 0f;
            s.priority = 40; s.volume = 0f; s.pitch = pitch;
            if (mixer != null) s.outputAudioMixerGroup = mixer;
            return s;
        }

        internal static void Desmontar()
        {
            foreach (var v in new[] { _escape, _enchimento })
                if (v?.Go != null) try { Object.Destroy(v.Go); } catch { }
            _escape = _enchimento = null;
        }

        internal static void Quadro()
        {
            if (_escape == null || _escape.Go == null || _escape.Go.WasCollected) return;
            float esc = 0f, enc = 0f;
            foreach (var r in Suspensao.Rodas)
            {
                if (r.Taxa < 0f) esc -= r.Taxa; else enc += r.Taxa;
            }
            // Vazão relativa: duas bolsas na velocidade nominal = 1.
            esc /= Mathf.Max(1e-4f, Cfg.VelocidadeBaixar.Value) * 2f;
            enc /= Mathf.Max(1e-4f, Cfg.VelocidadeSubir.Value) * 2f;
            float lugar = Suspensao.Dentro() ? Cfg.VolumeDentro.Value : 1f;
            float vol = Cfg.Volume.Value * lugar;
            float dt = Time.deltaTime;
            Atualizar(_escape, Alvo(esc) * vol, dt);
            Atualizar(_enchimento, Alvo(enc) * vol * Cfg.VolumeEnchimento.Value, dt);
        }

        // Volume pela raiz da vazão (o ouvido comprime), até 1,2.
        static float Alvo(float vazao) => vazao < 0.02f ? 0f : Mathf.Min(1.2f, Mathf.Sqrt(vazao));

        static void Atualizar(Voz v, float alvo, float dt)
        {
            // Ataque de 30 ms (válvula abrindo), soltura de 120 ms.
            float tau = alvo > v.G ? 0.03f : 0.12f;
            v.G += (alvo - v.G) * (1f - Mathf.Exp(-dt / tau));
            if (!v.Tocando)
            {
                if (alvo <= 0.001f) return;
                v.Inicio.time = 0f;
                v.Inicio.Play();
                v.Laco.time = UnityEngine.Random.Range(0f, _laco.length * 0.8f);
                v.Laco.Play();
                v.Tocando = true;
            }
            // Cruza do arquivo inteiro para o laço 1,2 s antes do fim do arquivo.
            float t = v.Inicio.isPlaying ? v.Inicio.time : _durInicio;
            float x0 = Mathf.Max(0.5f, _durInicio - 1.2f);
            float mixLaco = Mathf.Clamp01((t - x0) / 1f);
            v.Inicio.volume = v.G * Mathf.Sqrt(1f - mixLaco);
            v.Laco.volume = v.G * Mathf.Sqrt(mixLaco);
            if (v.G > v.Pico) v.Pico = v.G;
            if (alvo <= 0.001f && v.G < 0.003f)
            {
                v.Inicio.Stop(); v.Laco.Stop();
                v.Inicio.volume = v.Laco.volume = 0f;
                v.G = 0f; v.Tocando = false;
            }
        }

        // Picos desde a última chamada (o teste usa para conferir que tocou).
        internal static (float escape, float enchimento) TirarPicos()
        {
            float a = _escape?.Pico ?? 0f, b = _enchimento?.Pico ?? 0f;
            if (_escape != null) _escape.Pico = 0f;
            if (_enchimento != null) _enchimento.Pico = 0f;
            return (a, b);
        }

        internal static string Diagnostico()
        {
            string V(Voz v) => v == null ? "não montada" : $"tocando {v.Tocando}, envelope {v.G:0.000}, início vol {v.Inicio.volume:0.000} ({v.Inicio.isPlaying}), laço vol {v.Laco.volume:0.000} ({v.Laco.isPlaying}), mixer {(v.Inicio.outputAudioMixerGroup != null ? v.Inicio.outputAudioMixerGroup.name : "nenhum")}";
            return $"Som ({Origem}, {_durInicio:0.00} s): escape: {V(_escape)}; enchimento: {V(_enchimento)}";
        }
    }
}
