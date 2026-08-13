using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using CommunityToolkit.Maui.Views;

namespace SegurançaRe
{
    public partial class CameraYoloPage : ContentPage
    {
        private InferenceSession? _onnxSession;
        private bool _isProcessingFrame = false;

        // Instância reutilizável para o desenho (evita recriar a cada frame)
        private readonly YoloDrawable _yoloDrawable = new();

        // Dimensões padrão exigidas pelo YOLOv8 / YOLOv11
        private const int TargetWidth = 640;
        private const int TargetHeight = 640;

        public CameraYoloPage()
        {
            InitializeComponent();

            // Vincula o Drawable no GraphicsView uma única vez
            if (graphicsView != null)
            {
                graphicsView.Drawable = _yoloDrawable;
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await CarregarModeloOnnx();
        }

        // ================= 1. CARREGAR O MODELO .ONNX =================
        private async Task CarregarModeloOnnx()
        {
            try
            {
                // Carrega o arquivo do modelo a partir da pasta Resources/Raw
                using var stream = await FileSystem.OpenAppPackageFileAsync("yolov8n.onnx");
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);

                // Inicializa a sessão do ONNX Runtime
                _onnxSession = new InferenceSession(memoryStream.ToArray());

                if (LblStatus != null)
                    LblStatus.Text = "YOLO Carregado com Sucesso!";
            }
            catch (Exception ex)
            {
                if (LblStatus != null)
                    LblStatus.Text = $"Erro ao carregar modelo ONNX: {ex.Message}";

                System.Diagnostics.Debug.WriteLine($"Erro ONNX: {ex.Message}");
            }
        }

        // ================= 2. PROCESSAR O FRAME DA CÂMERA =================
        private async void OnMediaCaptured(object sender, MediaCapturedEventArgs e)
        {
            if (_onnxSession == null || _isProcessingFrame) return;

            _isProcessingFrame = true;

            try
            {
                // CORREÇÃO LINHA 78: Obtém o Stream usando e.Media (propriedade nativa do Toolkit)
                using var imageStream = e.Media;
                if (imageStream == null || imageStream.Length == 0) return;

                // Copia para MemoryStream para permitir leitura segura em background
                using var ms = new MemoryStream();
                await imageStream.CopyToAsync(ms);
                ms.Position = 0;

                var deteccoes = await Task.Run(() => ExecutarInferenciaYolo(ms));

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DesenharDeteccoes(deteccoes);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro no processamento do frame: {ex.Message}");
            }
            finally
            {
                _isProcessingFrame = false;
            }
        }

        // ================= 3. INFERÊNCIA COM ONNX RUNTIME =================
        private List<DeteccaoYolo> ExecutarInferenciaYolo(Stream streamImagem)
        {
            var listaDeteccoes = new List<DeteccaoYolo>();

            if (_onnxSession == null) return listaDeteccoes;

            // Transforma a imagem no Tensor de entrada (1, 3, 640, 640)
            var inputTensor = CriarTensorDaImagem(streamImagem);
            if (inputTensor == null) return listaDeteccoes;

            // Nome da entrada padrão do YOLOv8
            var inputName = _onnxSession.InputMetadata.Keys.FirstOrDefault() ?? "images";

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };

            // Executa o modelo
            using var results = _onnxSession.Run(inputs);
            var output = results.First().AsTensor<float>();

            // Processa as saídas da matriz (Bounding Boxes e Confiança)
            listaDeteccoes = ProcessarSaidaYolo(output);

            return listaDeteccoes;
        }

        // ================= 4. PRÉ-PROCESSAMENTO DA IMAGEM =================
        private DenseTensor<float>? CriarTensorDaImagem(Stream stream)
        {
            try
            {
                byte[] bytes;
                if (stream is MemoryStream ms)
                {
                    bytes = ms.ToArray();
                }
                else
                {
                    using var tempMs = new MemoryStream();
                    stream.CopyTo(tempMs);
                    bytes = tempMs.ToArray();
                }

                if (bytes.Length == 0) return null;

                // Preenche o tensor [1, 3, 640, 640] normalizado (RGB 0.0 a 1.0)
                var tensor = new DenseTensor<float>(new[] { 1, 3, TargetHeight, TargetWidth });

                int totalPixels = TargetWidth * TargetHeight;
                for (int i = 0; i < totalPixels; i++)
                {
                    int x = i % TargetWidth;
                    int y = i / TargetWidth;

                    // Mapeia os bytes do buffer para os canais R, G, B
                    byte r = bytes.Length > i * 3 ? bytes[i * 3] : (byte)0;
                    byte g = bytes.Length > i * 3 + 1 ? bytes[i * 3 + 1] : (byte)0;
                    byte b = bytes.Length > i * 3 + 2 ? bytes[i * 3 + 2] : (byte)0;

                    tensor[0, 0, y, x] = r / 255.0f; // R
                    tensor[0, 1, y, x] = g / 255.0f; // G
                    tensor[0, 2, y, x] = b / 255.0f; // B
                }

                return tensor;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao criar Tensor: {ex.Message}");
                return null;
            }
        }

        // ================= 5. PÓS-PROCESSAMENTO DAS CAIXAS =================
        private List<DeteccaoYolo> ProcessarSaidaYolo(Tensor<float> output)
        {
            var resultados = new List<DeteccaoYolo>();

            // Limiar de confiança mínimo (50%)
            float minConfidence = 0.5f;

            // O formato padrão de saída do YOLOv8 é [1, 84, 8400]
            int dimensions = output.Dimensions[1]; // 84 (4 coords + 80 classes)
            int anchors = output.Dimensions[2];    // 8400 caixas preditas

            for (int i = 0; i < anchors; i++)
            {
                float maxScore = 0;
                int classId = -1;

                // Procura a classe com maior pontuação
                for (int c = 4; c < dimensions; c++)
                {
                    float score = output[0, c, i];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        classId = c - 4;
                    }
                }

                if (maxScore >= minConfidence)
                {
                    float x = output[0, 0, i];
                    float y = output[0, 1, i];
                    float w = output[0, 2, i];
                    float h = output[0, 3, i];

                    resultados.Add(new DeteccaoYolo
                    {
                        X = x - (w / 2),
                        Y = y - (h / 2),
                        Largura = w,
                        Altura = h,
                        Confianca = maxScore,
                        ClasseId = classId
                    });
                }
            }

            return NmsSuppress(resultados); // Aplica Non-Maximum Suppression para remover caixas duplicadas
        }

        // Filtra caixas sobrepostas no mesmo objeto
        private List<DeteccaoYolo> NmsSuppress(List<DeteccaoYolo> boxes)
        {
            var result = new List<DeteccaoYolo>();
            var sorted = boxes.OrderByDescending(b => b.Confianca).ToList();

            while (sorted.Count > 0)
            {
                var current = sorted[0];
                result.Add(current);
                sorted.RemoveAt(0);

                sorted.RemoveAll(b => b.ClasseId == current.ClasseId && CalcularIoU(current, b) > 0.45f);
            }

            return result;
        }

        private float CalcularIoU(DeteccaoYolo a, DeteccaoYolo b)
        {
            float x1 = Math.Max(a.X, b.X);
            float y1 = Math.Max(a.Y, b.Y);
            float x2 = Math.Min(a.X + a.Largura, b.X + b.Largura);
            float y2 = Math.Min(a.Y + a.Altura, b.Y + b.Altura);

            float intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            float areaA = a.Largura * a.Altura;
            float areaB = b.Largura * b.Altura;

            return intersection / (areaA + areaB - intersection);
        }

        // ================= 6. DESENHAR RETÂNGULOS NA TELA =================
        private void DesenharDeteccoes(List<DeteccaoYolo> deteccoes)
        {
            if (graphicsView == null) return;

            // Atualiza a lista interna de detecções e força o redesenho da tela
            _yoloDrawable.Deteccoes = deteccoes;
            graphicsView.Invalidate();

            if (LblStatus != null)
            {
                LblStatus.Text = deteccoes.Count > 0
                    ? $"Objetos Detectados: {deteccoes.Count}"
                    : "Procurando objetos...";
            }
        }
    }

    // Estrutura para salvar cada detecção
    public class DeteccaoYolo
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Largura { get; set; }
        public float Altura { get; set; }
        public float Confianca { get; set; }
        public int ClasseId { get; set; }
    }

    // Desenha as caixas na tela usando Microsoft.Maui.Graphics
    public class YoloDrawable : IDrawable
    {
        public List<DeteccaoYolo> Deteccoes { get; set; } = new();

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Deteccoes == null || Deteccoes.Count == 0) return;

            canvas.StrokeColor = Colors.Red;
            canvas.StrokeSize = 3;
            canvas.FontColor = Colors.White;
            canvas.FontSize = 14;

            foreach (var item in Deteccoes)
            {
                // Converte as coordenadas da escala do YOLO (640x640) para o tamanho real da tela
                float scaleX = dirtyRect.Width / 640f;
                float scaleY = dirtyRect.Height / 640f;

                float rx = item.X * scaleX;
                float ry = item.Y * scaleY;
                float rw = item.Largura * scaleX;
                float rh = item.Altura * scaleY;

                // 1. Desenha a caixa delimitadora
                canvas.DrawRectangle(rx, ry, rw, rh);

                // 2. Desenha o fundo da etiqueta
                string label = $"Classe {item.ClasseId}: {item.Confianca * 100:F0}%";
                canvas.FillColor = Colors.Red;
                canvas.FillRectangle(rx, ry - 22, 140, 22);

                // CORREÇÃO LINHA 326: Desenha o texto informando a posição (x, y) diretamente
                canvas.DrawString(label, rx + 4, ry - 4, HorizontalAlignment.Left);
            }
        }
    }
}